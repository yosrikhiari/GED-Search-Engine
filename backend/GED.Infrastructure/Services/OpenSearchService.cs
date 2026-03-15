using OpenSearch.Client;
using OpenSearch.Net;
using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using CoreSearchRequest = GED.Core.Models.SearchRequest;

namespace GED.Infrastructure.Services;

/// <summary>
/// Hybrid search service: BM25 (keyword) + kNN (semantic vector) combined.
///
/// Why hybrid?
///   • BM25 is great for exact matches ("facture 2024", "contract PDF").
///   • kNN semantic search handles multilingual queries — a French query finds
///     English-titled documents because both share a semantic embedding space.
///   • Combining both gives precision (BM25) + recall (semantic).
///
/// Scoring: hybridScore = 0.6 × bm25_normalized + 0.4 × cosine_similarity
///
/// Degradation: if Ollama is unavailable, embedding returns null and the service
/// falls back to BM25-only. No exceptions, no downtime.
/// </summary>
public class OpenSearchService : ISearchService
{
    private readonly IOpenSearchClient _client;
    private readonly INlpService _nlpService;
    private readonly ILogger<OpenSearchService> _logger;

    private const string DocumentIndex   = "ged-documents";
    private const int    EmbeddingDim    = 768;   // nomic-embed-text output size
    private const float  Bm25Weight      = 0.6f;
    private const float  SemanticWeight  = 0.4f;
    // Minimum cosine similarity below which a semantic-only result is discarded
    private const float  SemanticThreshold = 0.30f;

    // Maps FR/AR query words → stored English category values
    private static readonly Dictionary<string, string> CategoryAliases =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // French → English
        { "contrat",      "Contract"     }, { "contrats",     "Contract"     },
        { "facture",      "Invoice"      }, { "factures",     "Invoice"      },
        { "rapport",      "Report"       }, { "rapports",     "Report"       },
        { "lettre",       "Letter"       }, { "lettres",      "Letter"       },
        { "courrier",     "Letter"       },
        { "devis",        "Invoice"      },
        { "note",         "Memo"         },
        { "présentation", "Presentation" }, { "presentation", "Presentation" },
        // Arabic → English
        { "عقد",          "Contract"     }, { "عقود",         "Contract"     },
        { "فاتورة",       "Invoice"      }, { "فواتير",       "Invoice"      },
        { "تقرير",        "Report"       }, { "تقارير",       "Report"       },
        { "رسالة",        "Letter"       }, { "رسائل",        "Letter"       },
        { "مذكرة",        "Memo"         },
        { "عرض",          "Presentation" },
    };


    public OpenSearchService(
        IOpenSearchClient client,
        INlpService nlpService,
        ILogger<OpenSearchService> logger)
    {
        _client     = client;
        _nlpService = nlpService;
        _logger     = logger;
    }

    // ── SearchAsync (hybrid pipeline) ─────────────────────────────────────────

    public async Task<SearchResult> SearchAsync(
        CoreSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            // ── Step 1: NLP understanding (fully local, <5ms) ─────────────────
            NaturalLanguageQuery? nlQuery = null;
            var processedQuery = request.Query;

            if (request.SearchType == GED.Core.Models.SearchType.Natural &&
                !string.IsNullOrWhiteSpace(request.Query))
            {
                nlQuery = await _nlpService.UnderstandQueryAsync(
                    request.Query, cancellationToken);

                processedQuery = nlQuery.ProcessedQuery;

                _logger.LogInformation(
                    "NLP: '{Original}' → lang={Lang}, keywords=[{KW}], understood={OK}",
                    request.Query, nlQuery.DetectedLanguage,
                    string.Join(",", nlQuery.Keywords), nlQuery.IsUnderstood);

                // Query is all stop-words — return empty immediately
                if (!nlQuery.IsUnderstood)
                {
                    return new SearchResult
                    {
                        TotalResults     = 0,
                        Page             = request.Page,
                        PageSize         = request.PageSize,
                        TotalPages       = 0,
                        Documents        = new(),
                        IsUnderstood     = false,
                        DetectedLanguage = nlQuery.DetectedLanguage,
                        SearchTimeMs     = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
                    };
                }

                // Apply NLP-extracted hard filters
                ApplyNlpFilters(request, nlQuery);

                // Generic "show all" detection
                var lowerRaw = request.Query.ToLower().Trim();
                var genericPhrases = new[] { "all documents", "show all", "list all", "get all",
                                             "tous les documents", "كل الوثائق", "جميع الملفات" };
                if (genericPhrases.Any(p => lowerRaw.Contains(p)) ||
                    (!nlQuery.Keywords.Any() && lowerRaw.Split(' ').All(w => w.Length <= 3)))
                {
                    processedQuery = string.Empty;
                    _logger.LogInformation("Generic 'show all' query detected — returning all documents");
                }
            }

            // ── Step 2: Generate query embedding (Ollama, ~50-100ms) ──────────
            // Run in parallel with BM25 to hide latency
            var embeddingText  = string.IsNullOrWhiteSpace(processedQuery)
                ? request.Query : processedQuery;
            var embeddingTask  = string.IsNullOrWhiteSpace(embeddingText)
                ? Task.FromResult<float[]?>(null)
                : _nlpService.GenerateEmbeddingAsync(embeddingText, cancellationToken);

            // ── Step 3: BM25 search ───────────────────────────────────────────
            var bm25Task = RunBm25SearchAsync(
                request, processedQuery, nlQuery, cancellationToken);

            // Both run concurrently
            await Task.WhenAll(bm25Task, embeddingTask);

            var bm25Response  = await bm25Task;
            var queryEmbedding = await embeddingTask;

            // ── Step 4: Semantic / kNN search (if embedding available) ─────────
            List<(Guid Id, float Score)> semanticHits = new();
            var searchMode = SearchMode.BM25;

            if (queryEmbedding != null)
            {
                semanticHits = await RunKnnSearchAsync(
                    request, queryEmbedding, cancellationToken);

                searchMode = (bm25Response?.Hits?.Any() == true) || semanticHits.Any()
                    ? SearchMode.Hybrid
                    : SearchMode.BM25;
            }

            // ── Step 5: Merge BM25 + semantic ─────────────────────────────────
            var documents = await MergeHybridResultsAsync(bm25Response, semanticHits, cancellationToken);

            // ── Step 6: Determine IsUnderstood from results ───────────────────
            // A query "understood" = NLP said OK AND we got at least one result.
            // If both search modes returned 0, the query is likely too vague/gibberish.
            var totalBm25 = (int)(bm25Response?.Total ?? 0);
            var isUnderstood = nlQuery?.IsUnderstood != false &&
                               (documents.Any() || totalBm25 > 0 ||
                                !string.IsNullOrWhiteSpace(request.Query));

            // ── Step 7: Normalize scores to 0–1 range ─────────────────────────
            if (documents.Any())
            {
                var maxScore = documents.Max(d => d.Score);
                if (maxScore > 0)
                    foreach (var d in documents)
                        d.Score = d.Score / maxScore;
            }

            var result = new SearchResult
            {
                TotalResults     = Math.Max(totalBm25, documents.Count),
                Page             = request.Page,
                PageSize         = request.PageSize,
                TotalPages       = (int)Math.Ceiling((double)Math.Max(totalBm25, documents.Count) / request.PageSize),
                Documents        = documents,
                Facets           = bm25Response != null ? ExtractFacets(bm25Response.Aggregations) : null,
                SearchTimeMs     = (long)(DateTime.UtcNow - startTime).TotalMilliseconds,
                ProcessedQuery   = processedQuery,
                IsUnderstood     = isUnderstood,
                DetectedLanguage = nlQuery?.DetectedLanguage,
                NlpSummary       = BuildNlpSummary(nlQuery),
                SearchMode       = searchMode
            };

            if (request.IncludeSuggestions && result.Documents.Any())
                result.Suggestions = await GetRelatedDocumentsAsync(
                    result.Documents.First().Id, 5, cancellationToken);

            _logger.LogInformation(
                "Search complete: {Mode}, {Count} docs, {Ms}ms, understood={OK}",
                searchMode, documents.Count, result.SearchTimeMs, isUnderstood);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing search for '{Query}'", request.Query);
            throw;
        }
    }

    // ── BM25 search ───────────────────────────────────────────────────────────

    private async Task<ISearchResponse<DocumentIndexModel>?> RunBm25SearchAsync(
        CoreSearchRequest request,
        string processedQuery,
        NaturalLanguageQuery? nlQuery,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.SearchAsync<DocumentIndexModel>(s => s
                .Index(DocumentIndex)
                .From((request.Page - 1) * request.PageSize)
                .Size(request.PageSize)
                .Query(q => BuildPrecisionQuery(q, processedQuery, request, nlQuery))
                .Sort(ss => BuildSort(ss, request.SortBy, request.SortDescending))
                .Highlight(h => h
                    .Fields(
                        f => f.Field(d => d.Title).NumberOfFragments(0),
                        f => f.Field(d => d.Description).NumberOfFragments(3).FragmentSize(150),
                        f => f.Field(d => d.ExtractedText).NumberOfFragments(3).FragmentSize(150),
                        f => f.Field(d => d.OcrText).NumberOfFragments(3).FragmentSize(150)
                    )
                )
                .Aggregations(a => BuildAggregations(a))
                .MinScore(request.MinScore ?? 0),
                cancellationToken);

            if (!response.IsValid)
                _logger.LogWarning("BM25 search issue: {Error}", response.DebugInformation);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BM25 search failed");
            return null;
        }
    }

    // ── kNN semantic search ───────────────────────────────────────────────────

    private async Task<List<(Guid Id, float Score)>> RunKnnSearchAsync(
        CoreSearchRequest request,
        float[] queryEmbedding,
        CancellationToken cancellationToken)
    {
        try
        {
            // Fetch more candidates (k = pageSize * 3) then filter/merge with BM25
            var k = Math.Min(request.PageSize * 3, 50);

            var response = await _client.SearchAsync<DocumentIndexModel>(s => s
                .Index(DocumentIndex)
                .Size(k)
                .Query(q => q
                    .Bool(b => b
                        // Hard filter: only indexed documents
                        .Filter(f => f.Term(t => t.Field("status").Value("Indexed")))
                        // kNN vector similarity
                        .Must(m => m.Knn(knn => knn
                            .Field("embedding")
                            .Vector(queryEmbedding)
                            .K(k)
                        ))
                    )
                ),
                cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogWarning("kNN search issue: {Error}", response.DebugInformation);
                return new();
            }

            return response.Hits
                .Where(h => (float)(h.Score ?? 0) >= SemanticThreshold)
                .Select(h => (h.Source.Id, (float)(h.Score ?? 0)))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "kNN search failed — BM25-only mode");
            return new();
        }
    }

    // ── Hybrid merge ──────────────────────────────────────────────────────────

    /// <summary>
    /// Merges BM25 and semantic results using Reciprocal Rank Fusion (RRF).
    ///
    /// RRF: score(d) = Σ 1/(k + rank(d)) for each ranked list.
    /// With k=60 this is robust to score scale differences between BM25 and cosine.
    /// Documents from both lists are rewarded; documents from only one are penalized.
    /// </summary>
    private async Task<List<DocumentSearchHit>> MergeHybridResultsAsync(
        ISearchResponse<DocumentIndexModel>? bm25Response,
        List<(Guid Id, float Score)> semanticHits,
        CancellationToken cancellationToken)
    {
        const int rrfK = 60;
        var scores = new Dictionary<Guid, float>();
        var hitMap = new Dictionary<Guid, DocumentSearchHit>();

        if (bm25Response?.Hits != null)
        {
            int rank = 1;
            foreach (var hit in bm25Response.Hits)
            {
                var docHit = MapToSearchHit(hit);
                scores[docHit.Id] = scores.GetValueOrDefault(docHit.Id) + 1f / (rrfK + rank++);
                hitMap.TryAdd(docHit.Id, docHit);
            }
        }

        var missingIds = new List<Guid>();
        for (int i = 0; i < semanticHits.Count; i++)
        {
            var (id, _) = semanticHits[i];
            scores[id] = scores.GetValueOrDefault(id) + 1f / (rrfK + i + 1);
            if (!hitMap.ContainsKey(id))
                missingIds.Add(id);
        }

        // Fetch semantic-only hits that BM25 didn't return
        if (missingIds.Any())
        {
            var fetchResponse = await _client.MultiGetAsync(mg => mg
                .Index(DocumentIndex)
                .GetMany<DocumentIndexModel>(missingIds.Select(id => id.ToString())),
                cancellationToken);

            foreach (var hit in fetchResponse.Hits.OfType<MultiGetHit<DocumentIndexModel>>()
                        .Where(h => h.Found))
            {
                var doc = hit.Source;
                if (doc == null) continue;

                if (!Guid.TryParse(hit.Id, out var docId)) continue;

                var docHit = new DocumentSearchHit
                {
                    Id           = doc.Id,
                    Title        = doc.Title,
                    Description  = doc.Description,
                    FileName     = doc.FileName,
                    ContentType  = doc.ContentType,
                    FileSize     = doc.FileSize,
                    CreatedAt    = doc.CreatedAt,
                    DocumentDate = doc.DocumentDate,
                    ModifiedAt   = doc.ModifiedAt,
                    Category     = doc.Category,
                    Tags         = doc.Tags,
                    Score        = 0f,   // will be overwritten by RRF score below
                    Highlights   = null,
                    Metadata     = doc.Metadata,
                    OcrText      = doc.OcrText,
                    ExtractedText = doc.ExtractedText
                };

                hitMap.TryAdd(docHit.Id, docHit);
            }

        }

        return hitMap.Values
            .OrderByDescending(d => scores.GetValueOrDefault(d.Id))
            .Select(d => { d.Score = scores.GetValueOrDefault(d.Id); return d; })
            .ToList();
    }
    
    // ── Index document with embedding ─────────────────────────────────────────

    public async Task<bool> IndexDocumentAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexModel = MapToIndexModel(document);

            // Generate semantic embedding for the document
            // Text = Title + Description + first 3000 chars of content (context window limit)
            var embeddingText = BuildEmbeddingText(document);
            if (!string.IsNullOrWhiteSpace(embeddingText))
            {
                var embedding = await _nlpService.GenerateEmbeddingAsync(
                    embeddingText, cancellationToken);

                if (embedding != null)
                {
                    indexModel.Embedding = embedding;
                    _logger.LogInformation(
                        "Generated {Dims}-dim embedding for document {Id}",
                        embedding.Length, document.Id);
                }
                else
                {
                    _logger.LogInformation(
                        "Ollama unavailable — indexing document {Id} without embedding",
                        document.Id);
                }
            }

            var response = await _client.IndexAsync(indexModel, i => i
                .Index(DocumentIndex)
                .Id(document.Id.ToString()),
                cancellationToken);

            if (response.IsValid)
            {
                await _client.Indices.RefreshAsync(DocumentIndex, r => r, cancellationToken);
                _logger.LogInformation("✅ Document {Id} indexed (embedding={HasEmb})",
                    document.Id, indexModel.Embedding != null);
                return true;
            }

            _logger.LogError("❌ Failed to index document {Id}: {Error}",
                document.Id, response.DebugInformation);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing document {Id}", document.Id);
            return false;
        }
    }

    /// <summary>
    /// Builds the text used for embedding generation.
    /// Priority: title (highest signal) + description + first 3000 chars of content.
    /// Truncated to ~4000 chars total (nomic-embed-text context window).
    /// </summary>
    private static string BuildEmbeddingText(Document document)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(document.Title))
            parts.Add(document.Title);

        if (!string.IsNullOrWhiteSpace(document.Description))
            parts.Add(document.Description);

        if (!string.IsNullOrWhiteSpace(document.Category))
            parts.Add(document.Category);

        // Prefer OCR text (scanned docs) over extracted text (native PDFs)
        var content = !string.IsNullOrWhiteSpace(document.OcrText)
            ? document.OcrText
            : document.ExtractedText;

        if (!string.IsNullOrWhiteSpace(content))
            parts.Add(content.Length > 3000 ? content[..3000] : content);

        return string.Join(". ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    // ── Chunk-level indexing ──────────────────────────────────────────────────────

/// <summary>
/// Embeds and indexes all chunks for a document into the ged-chunks index.
/// Deletes any existing chunks for the document first (idempotent re-index).
/// </summary>
public async Task IndexChunksAsync(
    Document document,
    List<DocumentChunk> chunks,
    CancellationToken cancellationToken = default)
{
    if (!chunks.Any()) return;

    // Delete existing chunks for this document (handles re-indexing case)
    await DeleteChunksForDocumentAsync(document.Id, cancellationToken);

    int indexed = 0;

    foreach (var chunk in chunks)
    {
        try
        {
            // Get embedding for this chunk's text
            chunk.Embedding = await _nlpService.GenerateEmbeddingAsync(chunk.Text, cancellationToken);


            var chunkDoc = new
            {
                document_id   = document.Id,
                chunk_id      = chunk.ChunkId,
                chunk_index   = chunk.ChunkIndex,
                text          = chunk.Text,
                title         = document.Title,
                category      = document.Category,
                document_date = document.DocumentDate,
                created_at    = document.CreatedAt,
                file_name     = document.FileName,
                content_type  = document.ContentType,
                tags          = document.Tags,
                embedding     = chunk.Embedding
            };

            var response = await _client.IndexAsync(
                chunkDoc,
                i => i.Index("ged-chunks").Id(chunk.ChunkId),
                cancellationToken);

            if (response.IsValid)
                indexed++;
            else
                _logger.LogWarning(
                    "Failed to index chunk {ChunkId}: {Error}",
                    chunk.ChunkId, response.ServerError?.Error?.Reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error indexing chunk {ChunkId} — skipping", chunk.ChunkId);
        }
    }

    _logger.LogInformation(
        "✅ Indexed {Indexed}/{Total} chunks for document {DocId}",
        indexed, chunks.Count, document.Id);
}

/// <summary>
/// Searches the ged-chunks index using KNN vector search on the query embedding.
/// Returns the top-K most relevant chunks with their parent document metadata.
/// </summary>
public async Task<List<ChunkSearchHit>> SearchChunksAsync(
    string query,
    int topK = 5,
    List<string>? categories = null,
    CancellationToken cancellationToken = default)
{
    var queryEmbedding = await _nlpService.GenerateEmbeddingAsync(query, cancellationToken);
    if (queryEmbedding == null || queryEmbedding.Length == 0)
        return new List<ChunkSearchHit>();

    // Build KNN query on chunks index
    var searchResponse = await _client.SearchAsync<ChunkIndexModel>(s => s
        .Index("ged-chunks")
        .Size(topK)
        .Query(q =>
        {
            // KNN vector query
            var knn = q.Knn(k => k
                .Field("embedding")
                .Vector(queryEmbedding)
                .K(topK));

            // Optionally filter by category
            if (categories?.Any() == true)
            {
                return q.Bool(b => b
                    .Must(knn)
                    .Filter(f => f.Terms(t => t
                        .Field("category.keyword")
                        .Terms(categories))));
            }

            return knn;
        }),
        cancellationToken);

    if (!searchResponse.IsValid)
    {
        _logger.LogWarning(
            "Chunk search failed: {Error}",
            searchResponse.ServerError?.Error?.Reason);
        return new List<ChunkSearchHit>();
    }

    return searchResponse.Hits.Select(h => new ChunkSearchHit
    {
        ChunkId      = h.Source.ChunkId,
        DocumentId   = h.Source.DocumentId,
        ChunkIndex   = h.Source.ChunkIndex,
        Text         = h.Source.Text,
        Title        = h.Source.Title,
        Category     = h.Source.Category,
        DocumentDate = h.Source.DocumentDate,
        FileName     = h.Source.FileName,
        ContentType  = h.Source.ContentType,
        Tags         = h.Source.Tags,
        Score        = (float)(h.Score ?? 0)
    }).ToList();
}

private async Task DeleteChunksForDocumentAsync(
    Guid documentId,
    CancellationToken cancellationToken)
{
    try
    {
        await _client.DeleteByQueryAsync<ChunkIndexModel>(d => d
            .Index("ged-chunks")
            .Query(q => q.Term(t => t.Field("document_id").Value(documentId.ToString()))),
            cancellationToken);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to delete existing chunks for document {DocId}", documentId);
    }
}

    // ── NLP filter application ─────────────────────────────────────────────────

private void ApplyNlpFilters(CoreSearchRequest request, NaturalLanguageQuery nlQuery)
{
    if (nlQuery.ExtractedFilters == null || !nlQuery.ExtractedFilters.Any()) return;

    // Only apply year-based date filter when the year is the ONLY meaningful token.
    // e.g. "2024" alone → date filter OK
    // e.g. "contrats 2024" → year should boost relevance, NOT hard-filter (kills results)
    bool hasNonYearKeywords = nlQuery.Keywords
        .Any(k => !System.Text.RegularExpressions.Regex.IsMatch(k, @"^\d{4}$"));

    if (!hasNonYearKeywords)
    {
        if (nlQuery.ExtractedFilters.TryGetValue("fromDate", out var fromDate))
        {
            request.FromDate = DateTime.Parse(fromDate);
            _logger.LogInformation("✅ NLP fromDate filter: {Date}", request.FromDate);
        }
        if (nlQuery.ExtractedFilters.TryGetValue("toDate", out var toDate))
        {
            request.ToDate = DateTime.Parse(toDate);
            _logger.LogInformation("✅ NLP toDate filter: {Date}", request.ToDate);
        }
    }
    else
    {
        _logger.LogInformation(
            "⏭️  Skipping year date filter — other keywords present ({KW})",
            string.Join(", ", nlQuery.Keywords));
    }

    // Filetype filter — only apply as hard filter when it's the only token
    // "pdf" alone → filter by ContentType
    // "pdf contracts" → let BM25 handle it via text matching on FileName/Tags
    if (nlQuery.ExtractedFilters.TryGetValue("filetype", out var fileType))
    {
        bool hasNonFiletypeKeywords = nlQuery.Keywords
            .Any(k => !new[] { "pdf","doc","docx","xls","xlsx","jpg","jpeg","png","txt" }
                .Contains(k.ToLower()));

        if (!hasNonFiletypeKeywords)
        {
            var contentType = MapFileTypeToContentType(fileType);
            if (!string.IsNullOrEmpty(contentType))
            {
                request.ContentTypes = new List<string> { contentType };
                _logger.LogInformation("✅ NLP contentType filter: {Type}", contentType);
            }
        }
    }

    // Category from entities — only if frontend didn't already set one
    var categoryEntity = nlQuery.Entities
        .FirstOrDefault(e => e.StartsWith("CATEGORY:"));
    if (categoryEntity != null && request.Categories == null)
    {
        var category = categoryEntity["CATEGORY:".Length..];
        request.Categories = new List<string> { category };
        _logger.LogInformation("✅ NLP category filter: {Cat}", category);
    }
}
    // ── NLP summary for the frontend banner ───────────────────────────────────

    private static string? BuildNlpSummary(NaturalLanguageQuery? nlQuery)
    {
        if (nlQuery == null) return null;

        var parts = new List<string>();

        var category = nlQuery.Entities
            .FirstOrDefault(e => e.StartsWith("CATEGORY:"))
            ?["CATEGORY:".Length..];
        if (!string.IsNullOrEmpty(category)) parts.Add(category);

        var fileType = nlQuery.ExtractedFilters?.GetValueOrDefault("filetype");
        if (!string.IsNullOrEmpty(fileType)) parts.Add(fileType.ToUpper());

        var fromDate = nlQuery.ExtractedFilters?.GetValueOrDefault("fromDate");
        if (!string.IsNullOrEmpty(fromDate))
            parts.Add($"depuis {DateTime.Parse(fromDate):yyyy-MM-dd}");

        var toDate = nlQuery.ExtractedFilters?.GetValueOrDefault("toDate");
        if (!string.IsNullOrEmpty(toDate))
            parts.Add($"jusqu'au {DateTime.Parse(toDate):yyyy-MM-dd}");

        if (nlQuery.Keywords.Any())
            parts.Add($"\"{string.Join(", ", nlQuery.Keywords.Take(3))}\"");

        return parts.Any() ? string.Join(" · ", parts) : null;
    }

    // ── BM25 query builder (unchanged from original) ──────────────────────────

    private QueryContainer BuildPrecisionQuery(
        QueryContainerDescriptor<DocumentIndexModel> q,
        string query,
        CoreSearchRequest request,
        NaturalLanguageQuery? nlQuery)
    {
        var shouldQueries = new List<Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>>();
        var filterQueries = new List<Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>>();

        // ── Hard filters ─────────────────────────────────────────────────────
        filterQueries.Add(fq => fq.Term(t => t.Field("status").Value("Indexed")));

        if (request.ContentTypes?.Any() == true)
            filterQueries.Add(ctq => ctq.Terms(t => t
                .Field(d => d.ContentType).Terms(request.ContentTypes)));

        var categoriesToFilter = new List<string>();
        if (request.Categories?.Any() == true)
            categoriesToFilter.AddRange(request.Categories);

        if (categoriesToFilter.Any())
            filterQueries.Add(cq => cq.Bool(b => b
                .Should(categoriesToFilter.Select(cat =>
                    new Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>(
                        sq => sq.Term(t => t.Field("category.keyword").Value(cat).CaseInsensitive(true))
                    )).ToArray())
                .MinimumShouldMatch(1)));

        if (request.Tags?.Any() == true)
            filterQueries.Add(tq => tq.Terms(t => t
                .Field(d => d.Tags).Terms(request.Tags)));

        if (request.FromDate.HasValue || request.ToDate.HasValue)
            filterQueries.Add(dq => dq.Bool(b => b
                .Should(
                    s => s.Bool(b1 => b1.Must(
                        m => m.Exists(e => e.Field(d => d.DocumentDate)),
                        m => m.DateRange(dr => dr.Field(d => d.DocumentDate)
                            .GreaterThanOrEquals(request.FromDate)
                            .LessThanOrEquals(request.ToDate)))),
                    s => s.Bool(b2 => b2.Must(
                        m => m.Bool(nb => nb.MustNot(mn => mn.Exists(e => e.Field(d => d.DocumentDate)))),
                        m => m.DateRange(dr => dr.Field(d => d.CreatedAt)
                            .GreaterThanOrEquals(request.FromDate)
                            .LessThanOrEquals(request.ToDate))))
                ).MinimumShouldMatch(1)));

        // ── Scoring queries ───────────────────────────────────────────────────
if (!string.IsNullOrWhiteSpace(query))
        {
            var cleanQuery   = query.Trim();
            var normalized   = NormalizeSearchQuery(cleanQuery);
            var variations   = GenerateQueryVariations(cleanQuery);

            // Add cross-language alias (e.g. "contrats" → "Contract")
            foreach (var token in cleanQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (CategoryAliases.TryGetValue(token, out var aliasedCategory))
                    variations.Add(aliasedCategory);

            if (cleanQuery.Contains(' '))
                shouldQueries.Add(sq => sq.MatchPhrase(mp => mp
                    .Field(d => d.Title).Query(cleanQuery).Boost(100.0)));

            foreach (var v in variations)
            {
                shouldQueries.Add(sq => sq.Term(t => t.Field("category.keyword").Value(v).CaseInsensitive(true).Boost(80.0)));
                shouldQueries.Add(sq => sq.Term(t => t.Field("title.keyword").Value(v).CaseInsensitive(true).Boost(70.0)));
                shouldQueries.Add(sq => sq.Prefix(p => p.Field(d => d.Title).Value(v.ToLower()).CaseInsensitive(true).Boost(50.0)));
                shouldQueries.Add(sq => sq.MultiMatch(m => m
                    .Query(v)
                    .Fields(f => f.Field(d => d.Title, 20.0).Field(d => d.Category, 15.0).Field(d => d.FileName, 10.0).Field(d => d.Description, 5.0))
                    .Type(TextQueryType.BestFields).Operator(Operator.Or).Boost(40.0)));
                shouldQueries.Add(sq => sq.Wildcard(w => w.Field(d => d.Title).Value($"*{v.ToLower()}*").CaseInsensitive(true).Boost(20.0)));
            }

            shouldQueries.Add(sq => sq.MatchPhrasePrefix(mpp => mpp
                .Field(d => d.Title).Query(cleanQuery).MaxExpansions(20).Boost(35.0)));

            shouldQueries.Add(sq => sq.Match(m => m
                .Field(d => d.ExtractedText).Query(normalized).Operator(Operator.Or).MinimumShouldMatch("30%").Boost(10.0)));
            shouldQueries.Add(sq => sq.Match(m => m
                .Field(d => d.OcrText).Query(normalized).Operator(Operator.Or).MinimumShouldMatch("30%").Boost(5.0)));

            // Boost by NLP keywords (with cross-language aliases)
            if (nlQuery?.Keywords?.Any() == true)
            {
                foreach (var kw in nlQuery.Keywords)
                {
                    // Skip pure year tokens — already handled via date filter or used as text boost
                    if (System.Text.RegularExpressions.Regex.IsMatch(kw, @"^\d{4}$"))
                    {
                        // Still boost text fields that mention the year
                        shouldQueries.Add(sq => sq.Match(m => m
                            .Field(d => d.ExtractedText).Query(kw).Boost(8.0)));
                        shouldQueries.Add(sq => sq.Match(m => m
                            .Field(d => d.OcrText).Query(kw).Boost(4.0)));
                        shouldQueries.Add(sq => sq.Match(m => m
                            .Field(d => d.Title).Query(kw).Boost(6.0)));
                        continue;
                    }

                    var kwVariations = GenerateQueryVariations(kw);
                    // Add alias for this keyword too
                    if (CategoryAliases.TryGetValue(kw, out var kwAlias))
                        kwVariations.Add(kwAlias);

                    foreach (var v in kwVariations)
                    {
                        shouldQueries.Add(sq => sq.Term(t => t.Field("category.keyword").Value(v).CaseInsensitive(true).Boost(30.0)));
                        shouldQueries.Add(sq => sq.MultiMatch(m => m
                            .Query(v)
                            .Fields(f => f.Field(d => d.Title, 8.0).Field(d => d.Category, 6.0).Field(d => d.Description, 3.0))
                            .Operator(Operator.Or)
                            .Boost(12.0)));
                    }
                }
            }
        }
        var msm = CalculateMinimumShouldMatch(query, shouldQueries.Count);

        return q.Bool(b =>
        {
            var bq = b;
            if (filterQueries.Any()) bq = bq.Filter(filterQueries.ToArray());
            if (shouldQueries.Any())
                bq = bq.Should(shouldQueries.ToArray()).MinimumShouldMatch(msm);
            else
                bq = bq.Must(m => m.MatchAll());
            return bq;
        });
    }

    // ── Helpers: normalize, variations, sort, aggregations ───────────────────

    private static string NormalizeSearchQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return query;
        var n = query.ToLower().Trim();
        if (n.EndsWith("ies") && n.Length > 4) return n[..^3] + "y";
        if (n.EndsWith("s") && n.Length > 2 && !n.EndsWith("ss")) return n.TrimEnd('s');
        return n;
    }

    private static List<string> GenerateQueryVariations(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();
        var cleaned  = query.Trim();
        var lower    = cleaned.ToLower();
        var norm     = NormalizeSearchQuery(lower);
        var cap      = char.ToUpper(lower[0]) + lower[1..];

        return new[] { cleaned, norm, lower, cap, lower + "s" }
            .Distinct()
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
    }

    private static string CalculateMinimumShouldMatch(string query, int total)
    {
        if (string.IsNullOrWhiteSpace(query)) return "0";
        return total <= 10 ? "1" : total <= 30 ? "2" : "3";
    }

    private static SortDescriptor<DocumentIndexModel> BuildSort(
        SortDescriptor<DocumentIndexModel> sort, SortField sortBy, bool descending)
    {
        var order = descending ? SortOrder.Descending : SortOrder.Ascending;
        return sortBy switch
        {
            SortField.Relevance    => sort.Descending(SortSpecialField.Score),
            SortField.CreatedDate  => sort.Field(d => d.CreatedAt, order),
            SortField.ModifiedDate => sort.Field(d => d.ModifiedAt, order),
            SortField.Title        => sort.Field("title.keyword", order),
            SortField.FileSize     => sort.Field(d => d.FileSize, order),
            _                      => sort.Descending(SortSpecialField.Score)
        };
    }

    private static AggregationContainerDescriptor<DocumentIndexModel> BuildAggregations(
        AggregationContainerDescriptor<DocumentIndexModel> agg)
        => agg
            .Terms("categories",     t => t.Field("category.keyword").Size(10))
            .Terms("content_types",  t => t.Field(d => d.ContentType).Size(10))
            .Terms("tags",           t => t.Field(d => d.Tags).Size(20))
            .DateHistogram("created_dates", d => d.Field(doc => doc.CreatedAt).CalendarInterval(DateInterval.Month));

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static DocumentSearchHit MapToSearchHit(IHit<DocumentIndexModel> hit)
    {
        var highlights = hit.Highlight?.Values.SelectMany(v => v).ToList() ?? new();
        var doc        = hit.Source;

        return new DocumentSearchHit
        {
            Id          = doc.Id,
            Title       = doc.Title,
            Description = doc.Description,
            FileName    = doc.FileName,
            ContentType = doc.ContentType,
            FileSize    = doc.FileSize,
            CreatedAt   = doc.CreatedAt,
            DocumentDate = doc.DocumentDate,
            ModifiedAt  = doc.ModifiedAt,
            Category    = doc.Category,
            Tags        = doc.Tags,
            Score       = (float)(hit.Score ?? 0),
            Highlights  = highlights.Any() ? highlights : null,
            Metadata    = doc.Metadata,
            OcrText       = hit.Source.OcrText,
            ExtractedText = hit.Source.ExtractedText,
        };
    }

    private DocumentIndexModel MapToIndexModel(Document document)
        => new()
        {
            Id            = document.Id,
            Title         = document.Title,
            Description   = document.Description,
            FileName      = document.FileName,
            ContentType   = document.ContentType,
            FileSize      = document.FileSize,
            CreatedAt     = document.CreatedAt,
            DocumentDate  = document.DocumentDate,
            ModifiedAt    = document.ModifiedAt,
            Status        = document.Status.ToString(),
            ExtractedText = document.ExtractedText,
            OcrText       = document.OcrText,
            Tags          = document.Tags,
            Category      = document.Category,
            Metadata      = SanitizeMetadata(document.Metadata)
        };

    private static Dictionary<string, object>? SanitizeMetadata(Dictionary<string, object>? metadata)
    {
        if (metadata == null) return null;
        var result = new Dictionary<string, object>(metadata.Count);
        foreach (var (key, value) in metadata)
            result[key] = FlattenValue(value);
        return result;
    }

    private static object FlattenValue(object? value)
    {
        if (value is null) return string.Empty;
        if (value is JsonElement je)
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString() ?? string.Empty,
                JsonValueKind.Number => je.TryGetInt64(out var l) ? (object)l : je.GetDouble(),
                JsonValueKind.True   => true,
                JsonValueKind.False  => false,
                JsonValueKind.Null   => string.Empty,
                _                    => je.GetRawText()
            };
        return value;
    }

    private static string MapFileTypeToContentType(string fileType)
        => fileType.ToLower() switch
        {
            "pdf"  => "application/pdf",
            "doc"  => "application/msword",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "xls"  => "application/vnd.ms-excel",
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "jpg" or "jpeg" => "image/jpeg",
            "png"  => "image/png",
            "txt"  => "text/plain",
            _      => string.Empty
        };

    private Dictionary<string, List<FacetValue>> ExtractFacets(
        IReadOnlyDictionary<string, IAggregate> aggregations)
    {
        var facets = new Dictionary<string, List<FacetValue>>();
        foreach (var agg in aggregations)
            if (agg.Value is BucketAggregate ba)
                facets[agg.Key] = ba.Items.OfType<KeyedBucket<object>>()
                    .Select(b => new FacetValue { Value = b.Key?.ToString() ?? "", Count = (int)(b.DocCount ?? 0) })
                    .ToList();
        return facets;
    }

    // ── Pass-through implementations ──────────────────────────────────────────

    public async Task<NaturalLanguageQuery> ProcessNaturalLanguageQueryAsync(
        string query, CancellationToken cancellationToken = default)
        => await _nlpService.UnderstandQueryAsync(query, cancellationToken);

    public async Task<bool> UpdateDocumentIndexAsync(
        Document document, CancellationToken cancellationToken = default)
        => await IndexDocumentAsync(document, cancellationToken);

    public async Task<bool> DeleteDocumentIndexAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.DeleteAsync<DocumentIndexModel>(
                documentId.ToString(), d => d.Index(DocumentIndex), cancellationToken);
            return response.IsValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document index {Id}", documentId);
            return false;
        }
    }

    public async Task<bool> BulkIndexDocumentsAsync(
        IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        try
        {
            // Generate embeddings in parallel (max 4 concurrent Ollama calls)
            var docs         = documents.ToList();
            var indexModels  = new List<DocumentIndexModel>(docs.Count);
            var semaphore    = new SemaphoreSlim(4, 4);

            var tasks = docs.Select(async doc =>
            {
                var model         = MapToIndexModel(doc);
                var embeddingText = BuildEmbeddingText(doc);

                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var embedding = await _nlpService.GenerateEmbeddingAsync(
                        embeddingText, cancellationToken);
                    model.Embedding = embedding;
                }
                finally { semaphore.Release(); }

                return model;
            });

            indexModels.AddRange(await Task.WhenAll(tasks));

            var response = await _client.BulkAsync(b => b
                .Index(DocumentIndex)
                .IndexMany(indexModels),
                cancellationToken);

            if (response.IsValid && !response.ItemsWithErrors.Any())
            {
                await _client.Indices.RefreshAsync(DocumentIndex, r => r, cancellationToken);
                return true;
            }

            _logger.LogError("Bulk index errors: {Count}", response.ItemsWithErrors.Count());
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk indexing documents");
            return false;
        }
    }

    public async Task<List<DocumentSuggestion>> GetRelatedDocumentsAsync(
        Guid documentId, int count = 5, CancellationToken cancellationToken = default)
    {
        try
        {
            // Try semantic "more like this" via the embedding first
            var docResponse = await _client.GetAsync<DocumentIndexModel>(
                documentId.ToString(), g => g.Index(DocumentIndex), cancellationToken);

            if (docResponse.Found && docResponse.Source.Embedding != null)
            {
                var embedding = docResponse.Source.Embedding;
                var knnResponse = await _client.SearchAsync<DocumentIndexModel>(s => s
                    .Index(DocumentIndex)
                    .Size(count + 1)
                    .Query(q => q.Bool(b => b
                        .Filter(f => f.Term(t => t.Field("status").Value("Indexed")))
                        .Must(m => m.Knn(knn => knn
                            .Field("embedding")
                            .Vector(embedding)
                            .K(count + 1))))),
                    cancellationToken);

                var semanticSuggestions = knnResponse.Hits
                    .Where(h => h.Source.Id != documentId)
                    .Take(count)
                    .Select(h => new DocumentSuggestion
                    {
                        DocumentId     = h.Source.Id,
                        Title          = h.Source.Title,
                        SimilarityScore = (float)(h.Score ?? 0),
                        Reason         = "Semantic similarity"
                    })
                    .ToList();

                if (semanticSuggestions.Any()) return semanticSuggestions;
            }

            // Fallback to MoreLikeThis
            var mltResponse = await _client.SearchAsync<DocumentIndexModel>(s => s
                .Index(DocumentIndex)
                .Size(count + 1)
                .Query(q => q.MoreLikeThis(mlt => mlt
                    .Fields(f => f.Field(d => d.Title).Field(d => d.Description)
                        .Field(d => d.ExtractedText).Field(d => d.Tags))
                    .Like(l => l.Document(d => d.Id(documentId.ToString())))
                    .MinTermFrequency(1).MinDocumentFrequency(1))),
                cancellationToken);

            return mltResponse.Documents
                .Where(d => d.Id != documentId)
                .Take(count)
                .Select(d => new DocumentSuggestion
                {
                    DocumentId = d.Id, Title = d.Title,
                    SimilarityScore = 0.7f, Reason = "Similar content"
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting related documents for {Id}", documentId);
            return new();
        }
    }
}

/// <summary>
/// OpenSearch index document model.
/// The <c>Embedding</c> field is mapped as a knn_vector in Program.cs index setup.
/// </summary>
public class DocumentIndexModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DocumentDate { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public string Status { get; set; } = "Indexed";
    public string? ExtractedText { get; set; }
    public string? OcrText { get; set; }
    public List<string>? Tags { get; set; }
    public string? Category { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// 768-dimensional nomic-embed-text vector.
    /// Stored as a knn_vector field in OpenSearch for approximate nearest-neighbor search.
    /// Null for documents indexed before the semantic search feature was enabled.
    /// </summary>
    public float[]? Embedding { get; set; }
}
// ── Chunk index model ─────────────────────────────────────────────────────────

public class ChunkIndexModel
{
    public Guid DocumentId { get; set; }
    public string ChunkId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Category { get; set; }
    public DateTime? DocumentDate { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public List<string>? Tags { get; set; }
}
public class ChunkSearchHit
{
    public string   ChunkId      { get; set; } = string.Empty;
    public Guid     DocumentId   { get; set; }
    public int      ChunkIndex   { get; set; }
    public string   Text         { get; set; } = string.Empty;
    public string   Title        { get; set; } = string.Empty;
    public string?  Category     { get; set; }
    public DateTime? DocumentDate { get; set; }
    public string   FileName     { get; set; } = string.Empty;
    public string   ContentType  { get; set; } = string.Empty;
    public List<string>? Tags    { get; set; }
    public float    Score        { get; set; }
}