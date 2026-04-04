using OpenSearch.Client;
using OpenSearch.Net;
using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using CoreSearchRequest = GED.Core.Models.SearchRequest;

namespace GED.Infrastructure.Services;

/// <summary>
/// Hybrid search service combining BM25 (keyword) and kNN (semantic vector) search.
/// 
/// <para>
/// Access control is enforced at query time via OpenSearch filters:
/// <list type="bullet">
///   <item>
///     <term>Admin users</term>
///     <description>See all documents (no filter injected).</description>
///   </item>
///   <item>
///     <term>Other users</term>
///     <description>
///       Must satisfy at least one of:
///       <list type="number">
///         <item>Document's accessLevel == "open"</item>
///         <item>User's ID is in allowedUserIds</item>
///         <item>Document's category is in user's AllowedCategories</item>
///       </list>
///     </description>
///   </item>
/// </list>
/// </para>
/// 
/// <para>
/// ACL fields (allowedUserIds, accessLevel, createdByUserId) are written to
/// OpenSearch at index time and kept in sync whenever ACL grants change.
/// </para>
/// </summary>
public class OpenSearchService : ISearchService
{
    private readonly IOpenSearchClient _client;
    private readonly INlpService       _nlpService;
    private readonly GedDbContext      _db;
    private readonly ILogger<OpenSearchService> _logger;

    /// <summary>
    /// Name of the documents index.
    /// </summary>
    private readonly string _documentIndex;

    /// <summary>
    /// Embedding vector dimension (bge-m3 produces 1024-dim vectors).
    /// </summary>
    private const int EmbeddingDim = 1024;

    /// <summary>
    /// Weight for BM25 scores in hybrid ranking (0.6 = 60% BM25 influence).
    /// </summary>
    private readonly float _bm25Weight;

    /// <summary>
    /// Weight for semantic similarity scores in hybrid ranking (0.4 = 40% kNN influence).
    /// </summary>
    private readonly float _semanticWeight;

    /// <summary>
    /// Minimum semantic similarity score to include kNN results.
    /// </summary>
    private readonly float _semanticThreshold;
    /// <summary>
    /// Minimum semantic similarity score for chunk search (can be lower than document search).
    /// </summary>
    private readonly float _chunkSemanticThreshold;

    /// <summary>
    /// BM25 weight for hybrid chunk search (configurable).
    /// </summary>
    private readonly float _chunkBm25Weight;

    /// <summary>
    /// Semantic (kNN) weight for hybrid chunk search (configurable).
    /// </summary>
    private readonly float _chunkSemanticWeight;

    /// <summary>
    /// RRF k parameter for hybrid chunk search fusion.
    /// </summary>
    private readonly int _chunkRerankFusionK;

    /// <summary>
    /// Multilingual category aliases mapping FR/AR terms to English values.
    /// Used to normalize category queries across languages.
    /// </summary>
    private static readonly Dictionary<string, string> CategoryAliases =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // French aliases
        { "contrat",      "Contract"     }, { "contrats",     "Contract"     },
        { "facture",      "Invoice"      }, { "factures",     "Invoice"      },
        { "rapport",      "Report"       }, { "rapports",     "Report"       },
        { "lettre",       "Letter"       }, { "lettres",      "Letter"       },
        { "courrier",     "Letter"       },
        { "devis",        "Invoice"      },
        { "note",         "Memo"         },
        { "présentation", "Presentation" }, { "presentation", "Presentation" },
        // Arabic aliases
        { "عقد",          "Contract"     }, { "عقود",         "Contract"     },
        { "فاتورة",       "Invoice"      }, { "فواتير",       "Invoice"      },
        { "تقرير",        "Report"       }, { "تقارير",       "Report"       },
        { "رسالة",        "Letter"       }, { "رسائل",        "Letter"       },
        { "مذكرة",        "Memo"         },
        { "عرض",          "Presentation" },
    };

    /// <summary>
    /// Initializes a new instance of <see cref="OpenSearchService"/>.
    /// </summary>
    /// <param name="client">OpenSearch client instance.</param>
    /// <param name="nlpService">NLP service for embeddings and query processing.</param>
    /// <param name="db">Database context for ACL lookups.</param>
    /// <param name="logger">Logger for search events.</param>
    /// <param name="configuration">Application configuration with Search:* settings.</param>
    public OpenSearchService(
        IOpenSearchClient           client,
        INlpService                 nlpService,
        GedDbContext                db,
        ILogger<OpenSearchService>  logger,
        IConfiguration              configuration)
    {
        _client            = client;
        _nlpService        = nlpService;
        _db                = db;
        _logger            = logger;
        _documentIndex     = configuration["Search:IndexName"] ?? "ged-documents";
        _bm25Weight        = configuration.GetValue<float>("Search:Bm25Weight",       0.6f);
        _semanticWeight    = configuration.GetValue<float>("Search:SemanticWeight",    0.4f);
        _semanticThreshold = configuration.GetValue<float>("Search:SemanticThreshold", 0.30f);
        _chunkSemanticThreshold = configuration.GetValue<float>("Search:ChunkSemanticThreshold", 0.20f);
        _chunkBm25Weight = configuration.GetValue<float>("Search:ChunkHybridBm25Weight", 0.4f);
        _chunkSemanticWeight = configuration.GetValue<float>("Search:ChunkHybridSemanticWeight", 0.6f);
        _chunkRerankFusionK = configuration.GetValue<int>("Search:ChunkRerankFusionK", 60);

        _logger.LogInformation(
            "Chunk hybrid search config: BM25={Bm25Weight}, Semantic={SemanticWeight}, RRF_K={RrfK}",
            _chunkBm25Weight, _chunkSemanticWeight, _chunkRerankFusionK);
    }

    /// <inheritdoc />
    public async Task<SearchResult> SearchAsync(
        CoreSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            NaturalLanguageQuery? nlQuery = null;
            var processedQuery = request.Query;

            // Process natural language query if requested
            if (request.SearchType == GED.Core.Models.SearchType.Natural &&
                !string.IsNullOrWhiteSpace(request.Query))
            {
                nlQuery = await _nlpService.UnderstandQueryAsync(request.Query, cancellationToken);
                processedQuery = nlQuery.ProcessedQuery;

                _logger.LogInformation(
                    "NLP: '{Original}' → lang={Lang}, keywords=[{KW}], understood={OK}",
                    request.Query, nlQuery.DetectedLanguage,
                    string.Join(",", nlQuery.Keywords), nlQuery.IsUnderstood);

                // Return empty results if query couldn't be understood
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

                // Apply NLP-extracted filters to the search request
                ApplyNlpFilters(request, nlQuery);

                // Handle "show all" queries
                var lowerRaw = request.Query.ToLower().Trim();
                var genericPhrases = new[]
                {
                    "all documents", "show all", "list all", "get all",
                    "tous les documents", "كل الوثائق", "جميع الملفات"
                };
                if (genericPhrases.Any(p => lowerRaw.Contains(p)) ||
                    (!nlQuery.Keywords.Any() && lowerRaw.Split(' ').All(w => w.Length <= 3)))
                {
                    _logger.LogInformation("Generic 'show all' query detected — returning all documents");

                    return await GetAllDocumentsAsync(request, cancellationToken);
                }
            }

            // Handle wildcard search type - return all documents for "*" query
            if (request.SearchType == GED.Core.Models.SearchType.Wildcard)
            {
                var lowerQuery = request.Query?.ToLower().Trim() ?? "";
                if (lowerQuery == "*" || string.IsNullOrWhiteSpace(request.Query))
                {
                    _logger.LogInformation("Wildcard search detected — returning all documents");
                    return await GetAllDocumentsAsync(request, cancellationToken);
                }
            }

            // Generate query embedding for semantic search (parallel with BM25)
            var embeddingText = string.IsNullOrWhiteSpace(processedQuery)
                ? request.Query : processedQuery;
            var embeddingTask = string.IsNullOrWhiteSpace(embeddingText)
                ? Task.FromResult<float[]?>(null)
                : _nlpService.GenerateEmbeddingAsync(embeddingText, cancellationToken);

            var bm25Task = RunBm25SearchAsync(request, processedQuery, nlQuery, cancellationToken);

            // Run BM25 and embedding in parallel
            await Task.WhenAll(bm25Task, embeddingTask);

            var bm25Response   = await bm25Task;
            var queryEmbedding = await embeddingTask;

            // Run kNN semantic search if we have an embedding
            List<(Guid Id, float Score)> semanticHits = new();
            var searchMode = SearchMode.BM25;

            if (queryEmbedding != null)
            {
                semanticHits = await RunKnnSearchAsync(request, queryEmbedding, cancellationToken);
                searchMode   = (bm25Response?.Hits?.Any() == true) || semanticHits.Any()
                    ? SearchMode.Hybrid : SearchMode.BM25;
            }

            // Merge results using Reciprocal Rank Fusion
            var documents = await MergeHybridResultsAsync(bm25Response, semanticHits, cancellationToken);

            // Fetch real-time status from database to ensure accuracy
            if (documents.Any())
            {
                await EnrichWithRealTimeStatusAsync(documents, cancellationToken);
            }

            // Normalize scores to 0-1 range
            var totalBm25    = (int)(bm25Response?.Total ?? 0);
            var isUnderstood = nlQuery?.IsUnderstood != false &&
                               (documents.Any() || totalBm25 > 0 ||
                                !string.IsNullOrWhiteSpace(request.Query));

            if (documents.Any())
            {
                var bm25Count = bm25Response?.Hits?.Count() ?? 0;
                _logger.LogInformation(
                    "[SCORE_DEBUG] Search query '{Query}': BM25 hits={Bm25Count}, Total docs={TotalDocs}, MaxScore={MaxScore}",
                    request.Query, bm25Count, documents.Count, documents.Max(d => d.Score));

                // When BM25 finds NO matches, ignore kNN results
                // Semantic search should not be used as a fallback when keyword search finds nothing
                // This prevents showing unrelated documents with fake high scores
                if (bm25Count == 0)
                {
                    _logger.LogWarning(
                        "Search query '{Query}' found no BM25 matches. " +
                        "Ignoring {KnnCount} kNN results to prevent irrelevant matches. " +
                        "Returning empty results.",
                        request.Query, documents.Count);

                    documents.Clear();
                }

                // Normalize scores for remaining documents
                if (documents.Any())
                {
                    var maxScore = documents.Max(d => d.Score);
                    if (maxScore > 0)
                    {
                        foreach (var d in documents)
                        {
                            d.Score = d.Score / maxScore;
                        }
                    }
                }
            }

            // Fetch pending documents from database
            var indexedDocIds = documents.Select(d => d.Id).ToList();
            var (pendingDocs, pendingCount) = await FetchPendingDocumentsAsync(indexedDocIds, request, cancellationToken);

            var result = new SearchResult
            {
                // Use the actual indexed count from OpenSearch + pending count
                // This ensures totalResults reflects the real state as documents process
                TotalResults     = Math.Max((int)(bm25Response?.Total ?? 0), documents.Count),
                Page             = request.Page,
                PageSize         = request.PageSize,
                TotalPages       = (int)Math.Ceiling((double)(Math.Max((int)(bm25Response?.Total ?? 0), documents.Count) + pendingCount) / request.PageSize),
                Documents        = documents,
                PendingDocuments = pendingDocs,
                PendingCount     = pendingCount,
                Facets           = bm25Response != null ? ExtractFacets(bm25Response.Aggregations) : null,
                SearchTimeMs     = (long)(DateTime.UtcNow - startTime).TotalMilliseconds,
                ProcessedQuery   = processedQuery,
                IsUnderstood     = isUnderstood,
                DetectedLanguage = nlQuery?.DetectedLanguage,
                NlpSummary       = BuildNlpSummary(nlQuery),
                SearchMode       = searchMode
            };

            // Get related document suggestions if requested
            if (request.IncludeSuggestions && result.Documents.Any())
                result.Suggestions = await GetRelatedDocumentsAsync(
                    result.Documents.First().Id, 5, cancellationToken);

            _logger.LogInformation(
                "Search complete: {Mode}, {Count} indexed + {Pending} pending docs, {Ms}ms, understood={OK}",
                searchMode, documents.Count, pendingDocs.Count, result.SearchTimeMs, isUnderstood);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing search for '{Query}'", request.Query);
            throw;
        }
    }

    /// <summary>
    /// Returns all documents for "show all" queries with proper sorting and pagination.
    /// </summary>
    private async Task<SearchResult> GetAllDocumentsAsync(
        CoreSearchRequest request,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            var response = await _client.SearchAsync<DocumentIndexModel>(s => s
                .Index(_documentIndex)
                .From((request.Page - 1) * request.PageSize)
                .Size(request.PageSize)
                .Query(q => q.MatchAll(m => m))
                .Sort(ss => BuildSort(ss, request.SortBy, request.SortDescending))
                .Aggregations(a => BuildAggregations(a)),
                cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogWarning("Show all query failed: {Error}", response.DebugInformation);
                return new SearchResult
                {
                    TotalResults = 0,
                    Page = request.Page,
                    PageSize = request.PageSize,
                    TotalPages = 0,
                    Documents = new(),
                    SearchTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
                };
            }

            var documents = response.Hits
                .Select(h => MapToSearchHit(h))
                .ToList();

            // Fetch real-time status from database for indexed documents
            if (documents.Any())
            {
                await EnrichWithRealTimeStatusAsync(documents, cancellationToken);
            }

            // Fetch pending documents from database
            var indexedDocIds = documents.Select(d => d.Id).ToList();
            var (pendingDocs, pendingCount) = await FetchPendingDocumentsAsync(indexedDocIds, request, cancellationToken);

            _logger.LogInformation(
                "Show all query: returned {Count}/{Total} indexed + {Pending} pending documents, {Ms}ms",
                documents.Count, response.Total, pendingDocs.Count, (long)(DateTime.UtcNow - startTime).TotalMilliseconds);

            return new SearchResult
            {
                TotalResults     = (int)(response.Total) + pendingCount,
                Page             = request.Page,
                PageSize         = request.PageSize,
                TotalPages       = (int)Math.Ceiling((double)(response.Total + pendingCount) / request.PageSize),
                Documents        = documents,
                PendingDocuments = pendingDocs,
                PendingCount     = pendingCount,
                Facets           = ExtractFacets(response.Aggregations),
                SearchTimeMs     = (long)(DateTime.UtcNow - startTime).TotalMilliseconds,
                ProcessedQuery   = request.Query,
                IsUnderstood     = true,
                SearchMode       = SearchMode.BM25
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all documents");
            throw;
        }
    }

    /// <summary>
    /// Enriches search results with real-time status from database.
    /// This ensures the status reflects any changes that happened after indexing.
    /// </summary>
    private async Task EnrichWithRealTimeStatusAsync(
        List<DocumentSearchHit> documents,
        CancellationToken cancellationToken)
    {
        try
        {
            var docIds = documents.Select(d => d.Id).ToList();
            
            var dbStatuses = await _db.Documents
                .Where(d => docIds.Contains(d.Id))
                .Select(d => new { d.Id, d.Status, d.IsOcrProcessed, d.Metadata })
                .ToListAsync(cancellationToken);

            var statusMap = dbStatuses.ToDictionary(d => d.Id);

            foreach (var doc in documents)
            {
                if (statusMap.TryGetValue(doc.Id, out var dbDoc))
                {
                    doc.Status = dbDoc.Status.ToString();
                    
                    // IsFullyProcessed: true only when OCR is done AND stage is "completed"
                    // This means the entire pipeline (Tesseract + LLM) is complete
                    // Handle both string and JsonElement types from EF Core
                    var stageValue = dbDoc.Metadata?.GetValueOrDefault("ocr_stage");
                    string? stage = null;
                    if (stageValue is JsonElement je)
                        stage = je.GetString();
                    else if (stageValue is string s)
                        stage = s;
                    
                    doc.IsFullyProcessed = dbDoc.IsOcrProcessed && stage == "completed";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich search results with real-time status");
        }
    }

    /// <summary>
    /// Fetches documents from the database that are not yet indexed in OpenSearch.
    /// These are shown in a separate "En cours de traitement" section.
    /// </summary>
    private async Task<(List<DocumentSearchHit> PendingDocuments, int PendingCount)> FetchPendingDocumentsAsync(
        List<Guid> indexedDocIds,
        CoreSearchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get pending documents (Status != Indexed and not already in search results)
            var pendingQuery = _db.Documents
                .Where(d => d.Status != DocumentStatus.Indexed && d.Status != DocumentStatus.Deleted);

            // Exclude documents already in the indexed results
            if (indexedDocIds.Any())
            {
                pendingQuery = pendingQuery.Where(d => !indexedDocIds.Contains(d.Id));
            }

            // Get pending documents ordered by CreatedAt
            var pendingEntities = await pendingQuery
                .OrderByDescending(d => d.CreatedAt)
                .Take(100) // Limit to prevent overwhelming the UI
                .ToListAsync(cancellationToken);

            if (!pendingEntities.Any())
            {
                return (new List<DocumentSearchHit>(), 0);
            }

            // Get total count of pending documents for UI badge
            var totalPendingCount = await _db.Documents
                .Where(d => d.Status != DocumentStatus.Indexed && d.Status != DocumentStatus.Deleted)
                .CountAsync(cancellationToken);

            // Get all outbox messages for queue position calculation
            var pendingDocIds = pendingEntities.Select(e => e.Id).ToList();
            var outboxMessages = await _db.OutboxMessages
                .Where(m => pendingDocIds.Contains(m.Id) ||
                           m.Payload.Contains("DocumentId") && m.ProcessedAt == null)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(cancellationToken);

            // Build queue position map from outbox messages
            var queuePositionMap = new Dictionary<Guid, int>();
            int position = 1;
            foreach (var msg in outboxMessages.OrderBy(m => m.CreatedAt))
            {
                // Try to extract document ID from payload
                if (msg.Payload.Contains("DocumentId"))
                {
                    try
                    {
                        var docIdMatch = System.Text.RegularExpressions.Regex.Match(
                            msg.Payload, @"""DocumentId"":?\s*""?([0-9a-fA-F-]+)""?");
                        if (docIdMatch.Success && Guid.TryParse(docIdMatch.Groups[1].Value, out var docId))
                        {
                            if (!queuePositionMap.ContainsKey(docId))
                            {
                                queuePositionMap[docId] = position++;
                            }
                        }
                    }
                    catch { /* ignore parse errors */ }
                }
            }

            var pendingDocuments = new List<DocumentSearchHit>();
            foreach (var entity in pendingEntities)
            {
                // Determine processing stage
                var (stage, isQueued) = DetermineProcessingStage(entity, outboxMessages);

                // Get queue position
                queuePositionMap.TryGetValue(entity.Id, out int queuePosition);

                var hit = new DocumentSearchHit
                {
                    Id = entity.Id,
                    Title = entity.Title,
                    Description = entity.Description,
                    FileName = entity.FileName,
                    ContentType = entity.ContentType,
                    FileSize = entity.FileSize,
                    CreatedAt = entity.CreatedAt,
                    DocumentDate = entity.DocumentDate,
                    ModifiedAt = entity.ModifiedAt,
                    Category = entity.Category,
                    Tags = entity.Tags,
                    Status = entity.Status.ToString(),
                    DocumentStatus = entity.Status.ToString(),
                    IsOcrProcessed = entity.IsOcrProcessed,
                    IsFullyProcessed = false, // Not indexed yet
                    ProcessingStage = stage,
                    QueuePosition = queuePosition > 0 ? queuePosition : null,
                    IsQueued = isQueued,
                    Score = 0, // No relevance score for pending documents
                    OcrStageLabel = entity.Metadata?.GetValueOrDefault("ocr_stage")?.ToString()
                };

                pendingDocuments.Add(hit);
            }

            _logger.LogInformation(
                "Fetched {Count} pending documents (total pending: {Total})",
                pendingDocuments.Count, totalPendingCount);

            return (pendingDocuments, totalPendingCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch pending documents from database");
            return (new List<DocumentSearchHit>(), 0);
        }
    }

    /// <summary>
    /// Determines the processing stage for a pending document.
    /// </summary>
    private (string Stage, bool IsQueued) DetermineProcessingStage(
        DocumentEntity entity,
        List<OutboxMessage> outboxMessages)
    {
        var docOutboxMessages = outboxMessages
            .Where(m => m.Payload.Contains(entity.Id.ToString()))
            .OrderBy(m => m.CreatedAt)
            .ToList();

        var ocrJob = docOutboxMessages.FirstOrDefault(m => m.Type == "OcrJob");
        var indexingJob = docOutboxMessages.FirstOrDefault(m => m.Type == "IndexingJob");

        bool isOcrJobQueued = ocrJob != null;
        bool isOcrJobProcessed = ocrJob?.ProcessedAt != null;
        bool isOcrJobAcknowledged = ocrJob?.AcknowledgedAt != null;

        bool isIndexingJobQueued = indexingJob != null;
        bool isIndexingJobProcessed = indexingJob?.ProcessedAt != null;
        bool isIndexingJobAcknowledged = indexingJob?.AcknowledgedAt != null;

        // Stage 1: Waiting for OCR
        if (entity.Status == DocumentStatus.Pending && !isOcrJobQueued)
        {
            return ("En attente de traitement", false);
        }

        // Stage 2: OCR in queue (waiting for worker)
        if (!isOcrJobProcessed)
        {
            return ("File d'attente OCR", true);
        }

        // Stage 3: OCR processing
        if (!isOcrJobAcknowledged)
        {
            return ("OCR en cours", true);
        }

        // Stage 4: Waiting for indexing
        if (!isIndexingJobQueued)
        {
            return ("Indexation en attente", false);
        }

        // Stage 5: Indexing in queue
        if (!isIndexingJobProcessed)
        {
            return ("File d'attente indexation", true);
        }

        // Stage 6: Indexing in progress
        if (!isIndexingJobAcknowledged)
        {
            return ("Indexation en cours", true);
        }

        // Fallback: generic waiting
        return ($"Statut: {entity.Status}", isOcrJobQueued || isIndexingJobQueued);
    }

    /// <summary>
    /// Runs BM25 (keyword) search with highlighting and aggregations.
    /// </summary>
    private async Task<ISearchResponse<DocumentIndexModel>?> RunBm25SearchAsync(
        CoreSearchRequest request,
        string processedQuery,
        NaturalLanguageQuery? nlQuery,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.SearchAsync<DocumentIndexModel>(s => s
                .Index(_documentIndex)
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

    /// <summary>
    /// Runs kNN semantic search using query embedding.
    /// </summary>
    private async Task<List<(Guid Id, float Score)>> RunKnnSearchAsync(
        CoreSearchRequest request,
        float[] queryEmbedding,
        CancellationToken cancellationToken)
    {
        try
        {
            // Fetch more candidates than requested to allow for filtering
            var k = Math.Min(request.PageSize * 3, 50);

            var response = await _client.SearchAsync<DocumentIndexModel>(s => s
                .Index(_documentIndex)
                .Size(k)
                .Query(q => q
                    .Bool(b =>
                    {
                        // Hard filter: only indexed documents
                        var filters = new List<Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>>
                        {
                            fq => fq.Term(t => t.Field("status").Value("Indexed"))
                        };

                        // ACL filter for non-admin users
                        bool isNonAdminWithValidId = request.UserRole != "Admin" && !string.IsNullOrEmpty(request.UserId);
                        bool isNonAdminWithoutId = request.UserRole != "Admin" && string.IsNullOrEmpty(request.UserId);

                        if (isNonAdminWithoutId)
                        {
                            // Return no results if user has no ID (authentication issue)
                            filters.Add(fq => fq.Term(t => t.Field("id").Value(Guid.Empty.ToString())));
                        }
                        else if (isNonAdminWithValidId)
                        {
                            var userId = request.UserId;
                            var cats   = request.UserAllowedCategories ?? new List<string>();

                            var aclShould = new List<Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>>
                            {
                                // Document is open to all
                                s2 => s2.Term(t => t.Field("accessLevel").Value("open")),
                                // User has explicit ACL grant
                                s2 => s2.Term(t => t.Field("allowedUserIds").Value(userId))
                            };
                            // Category-based access
                            foreach (var cat in cats)
                            {
                                var c = cat;
                                aclShould.Add(s2 => s2.Term(t => t.Field("category.keyword").Value(c)));
                            }

                            filters.Add(fq => fq.Bool(ab => ab
                                .Should(aclShould.ToArray())
                                .MinimumShouldMatch(1)));
                        }

                        b = b.Filter(filters.ToArray());

                        // kNN query for semantic search
                        b = b.Must(m => m.Knn(knn => knn
                            .Field("embedding")
                            .Vector(queryEmbedding)
                            .K(k)));

                        return b;
                    })
                ),
                cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogWarning("kNN search issue: {Error}", response.DebugInformation);
                return new();
            }

            return response.Hits
                .Where(h => (float)(h.Score ?? 0) >= _semanticThreshold)
                .Select(h => (h.Source.Id, (float)(h.Score ?? 0)))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "kNN search failed — BM25-only mode");
            return new();
        }
    }

    /// <summary>
    /// Merges BM25 and kNN results using Reciprocal Rank Fusion (RRF).
    /// RRF provides a simple, robust way to combine rankings without score normalization.
    /// </summary>
    private async Task<List<DocumentSearchHit>> MergeHybridResultsAsync(
        ISearchResponse<DocumentIndexModel>? bm25Response,
        List<(Guid Id, float Score)> semanticHits,
        CancellationToken cancellationToken)
    {
        const int rrfK = 60;  // RRF constant (60 is standard)
        var scores = new Dictionary<Guid, float>();
        var hitMap = new Dictionary<Guid, DocumentSearchHit>();

        // Process BM25 hits
        if (bm25Response?.Hits != null)
        {
            int rank = 1;
            foreach (var hit in bm25Response.Hits)
            {
                var docHit = MapToSearchHit(hit);
                // RRF score contribution: 1 / (k + rank)
                scores[docHit.Id] = scores.GetValueOrDefault(docHit.Id) + 1f / (rrfK + rank++);
                hitMap.TryAdd(docHit.Id, docHit);
            }
        }

        // Process semantic hits and fetch missing documents
        var missingIds = new List<Guid>();
        for (int i = 0; i < semanticHits.Count; i++)
        {
            var (id, _) = semanticHits[i];
            scores[id] = scores.GetValueOrDefault(id) + 1f / (rrfK + i + 1);
            if (!hitMap.ContainsKey(id))
                missingIds.Add(id);
        }

        // Fetch documents missing from BM25 results (found only via kNN)
        if (missingIds.Any())
        {
            var fetchResponse = await _client.MultiGetAsync(mg => mg
                .Index(_documentIndex)
                .GetMany<DocumentIndexModel>(missingIds.Select(id => id.ToString())),
                cancellationToken);

            foreach (var hit in fetchResponse.Hits.OfType<MultiGetHit<DocumentIndexModel>>()
                         .Where(h => h.Found))
            {
                var doc = hit.Source;
                if (doc == null || !Guid.TryParse(hit.Id, out _)) continue;

                var docHit = new DocumentSearchHit
                {
                    Id            = doc.Id,
                    Title         = doc.Title,
                    Description   = doc.Description,
                    FileName      = doc.FileName,
                    ContentType   = doc.ContentType,
                    FileSize      = doc.FileSize,
                    CreatedAt     = doc.CreatedAt,
                    DocumentDate  = doc.DocumentDate,
                    ModifiedAt    = doc.ModifiedAt,
                    Category      = doc.Category,
                    Tags          = doc.Tags,
                    Score         = 0f,
                    Highlights    = null,
                    Metadata      = doc.Metadata,
                    OcrText       = doc.OcrText,
                    ExtractedText = doc.ExtractedText
                };
                hitMap.TryAdd(docHit.Id, docHit);
            }
        }

        // Sort by combined RRF score
        return hitMap.Values
            .OrderByDescending(d => scores.GetValueOrDefault(d.Id))
            .Select(d => { d.Score = scores.GetValueOrDefault(d.Id); return d; })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<bool> IndexDocumentAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexModel = MapToIndexModel(document);

            // Fetch ACL grants and embed in index document for query-time filtering
            var aclUserIds = await _db.DocumentAcls
                .Where(a => a.DocumentId == document.Id &&
                            (a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow))
                .Select(a => a.UserId.ToString())
                .ToListAsync(cancellationToken);

            // If there are ACL grants, document is restricted; otherwise it's open
            indexModel.AllowedUserIds   = aclUserIds;
            indexModel.CreatedByUserId  = document.CreatedBy;
            indexModel.AccessLevel      = aclUserIds.Count > 0 ? "restricted" : "open";

            // Generate embedding for semantic search
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
                .Index(_documentIndex)
                .Id(document.Id.ToString()),
                cancellationToken);

            if (response.IsValid)
            {
                await _client.Indices.RefreshAsync(_documentIndex, r => r, cancellationToken);
                _logger.LogInformation(
                    "✅ Document {Id} indexed (embedding={HasEmb}, aclUsers={AclCount})",
                    document.Id, indexModel.Embedding != null, aclUserIds.Count);
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
    /// Builds text for embedding by combining title, description, category, and content.
    /// Content is truncated to 3000 chars to fit model context.
    /// </summary>
    private static string BuildEmbeddingText(Document document)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(document.Title))       parts.Add(document.Title);
        if (!string.IsNullOrWhiteSpace(document.Description)) parts.Add(document.Description);
        if (!string.IsNullOrWhiteSpace(document.Category))      parts.Add(document.Category);

        var content = !string.IsNullOrWhiteSpace(document.OcrText)
            ? document.OcrText : document.ExtractedText;
        if (!string.IsNullOrWhiteSpace(content))
            parts.Add(content.Length > 3000 ? content[..3000] : content);

        return string.Join(". ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    /// <inheritdoc />
    public async Task IndexChunksAsync(
        Document document,
        List<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (!chunks.Any()) return;

        // Delete existing chunks for this document before re-indexing
        await DeleteChunksForDocumentAsync(document.Id, cancellationToken);

        // Fetch ACL grants for chunk documents
        var aclUserIds = await _db.DocumentAcls
            .Where(a => a.DocumentId == document.Id &&
                        (a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow))
            .Select(a => a.UserId.ToString())
            .ToListAsync(cancellationToken);

        var accessLevel = aclUserIds.Count > 0 ? "restricted" : "open";
        var createdByUserId = document.CreatedBy;

        int indexed = 0;
        foreach (var chunk in chunks)
        {
            try
            {
                // Generate embedding for each chunk
                chunk.Embedding = await _nlpService.GenerateEmbeddingAsync(
                    chunk.Text, cancellationToken);

                var chunkDoc = new
                {
                    document_id    = document.Id,
                    chunk_id       = chunk.ChunkId,
                    chunk_index    = chunk.ChunkIndex,
                    text           = chunk.Text,
                    title          = document.Title,
                    category       = document.Category,
                    document_date  = document.DocumentDate,
                    created_at     = document.CreatedAt,
                    file_name      = document.FileName,
                    content_type   = document.ContentType,
                    tags           = document.Tags,
                    embedding      = chunk.Embedding,
                    // ACL fields denormalized from parent document
                    allowedUserIds = aclUserIds,
                    accessLevel    = accessLevel,
                    createdByUserId = createdByUserId
                };

                var response = await _client.IndexAsync(
                    chunkDoc,
                    i => i.Index("ged-chunks").Id(chunk.ChunkId),
                    cancellationToken);

                if (response.IsValid) indexed++;
                else
                    _logger.LogWarning("Failed to index chunk {ChunkId}: {Error}",
                        chunk.ChunkId, response.ServerError?.Error?.Reason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error indexing chunk {ChunkId} — skipping", chunk.ChunkId);
            }
        }

        _logger.LogInformation("✅ Indexed {Indexed}/{Total} chunks for document {DocId}",
            indexed, chunks.Count, document.Id);
    }

    /// <inheritdoc />
    public async Task<List<ChunkSearchHit>> SearchChunksAsync(
        string query,
        int topK = 5,
        List<string>? categories = null,
        List<Guid>? documentIds = null,
        string? userId = null,
        string? userRole = null,
        List<string>? userAllowedCategories = null,
        CancellationToken cancellationToken = default)
    {
        var filters = BuildChunkFilters(categories, documentIds, userId, userRole, userAllowedCategories);
        var searchSize = topK * 2; // Fetch more for RRF

        // Generate query embedding
        var queryEmbedding = await _nlpService.GenerateEmbeddingAsync(query, cancellationToken);

        // Run BM25 and kNN in parallel
        var bm25Task = SearchChunksWithBm25Async(query, searchSize, filters, cancellationToken);
        var knnTask = (queryEmbedding != null && queryEmbedding.Length > 0)
            ? SearchChunksWithKnnAsync(query, queryEmbedding, searchSize, filters, cancellationToken)
            : Task.FromResult(new List<ChunkSearchHit>());

        await Task.WhenAll(bm25Task, knnTask);

        var bm25Results = await bm25Task;
        var knnResults = await knnTask;

        _logger.LogDebug("Chunk BM25 returned {Bm25Count} results, kNN returned {KnnCount} results",
            bm25Results.Count, knnResults.Count);

        // Combine using RRF if both have results
        List<ChunkSearchHit> results;
        if (bm25Results.Count > 0 && knnResults.Count > 0)
        {
            results = ApplyReciprocalRankFusion(bm25Results, knnResults, searchSize);
            _logger.LogDebug("RRF combined {Count} results", results.Count);
        }
        else if (knnResults.Count > 0)
        {
            results = knnResults;
            _logger.LogDebug("Using kNN results only (BM25 empty)");
        }
        else if (bm25Results.Count > 0)
        {
            // BM25-only fallback when kNN unavailable
            _logger.LogInformation("No kNN results - using BM25 fallback");
            results = bm25Results;
        }
        else
        {
            _logger.LogWarning("Both BM25 and kNN returned empty results for chunk search");
            results = new List<ChunkSearchHit>();
        }

        return results.Take(topK).ToList();
    }

    /// <summary>
    /// Combines BM25 and kNN results using Reciprocal Rank Fusion (RRF).
    /// </summary>
    private List<ChunkSearchHit> ApplyReciprocalRankFusion(
        List<ChunkSearchHit> bm25Results,
        List<ChunkSearchHit> knnResults,
        int size)
    {
        // Create rank dictionaries
        var bm25Ranks = bm25Results
            .Select((hit, index) => new { hit.ChunkId, Rank = index + 1 })
            .ToDictionary(x => x.ChunkId, x => x.Rank);

        var knnRanks = knnResults
            .Select((hit, index) => new { hit.ChunkId, Rank = index + 1 })
            .ToDictionary(x => x.ChunkId, x => x.Rank);

        // Get all unique chunk IDs
        var allChunkIds = bm25Ranks.Keys.Union(knnRanks.Keys);

        // Calculate RRF scores
        var rrfScores = new Dictionary<string, float>();
        foreach (var chunkId in allChunkIds)
        {
            float bm25Score = 0f;
            float knnScore = 0f;

            if (bm25Ranks.TryGetValue(chunkId, out var bm25Rank))
            {
                bm25Score = _chunkBm25Weight * (1f / (_chunkRerankFusionK + bm25Rank));
            }

            if (knnRanks.TryGetValue(chunkId, out var knnRank))
            {
                knnScore = _chunkSemanticWeight * (1f / (_chunkRerankFusionK + knnRank));
            }

            rrfScores[chunkId] = bm25Score + knnScore;
        }

        // Sort by RRF score and rebuild results
        var sortedChunkIds = rrfScores
            .OrderByDescending(x => x.Value)
            .Take(size)
            .Select(x => x.Key)
            .ToList();

        // Build result list with combined scores
        var resultLookup = bm25Results.Concat(knnResults)
            .GroupBy(x => x.ChunkId)
            .ToDictionary(x => x.Key, x => x.First());

        return sortedChunkIds
            .Where(chunkId => resultLookup.TryGetValue(chunkId, out var hit))
            .Select(chunkId =>
            {
                var hit = resultLookup[chunkId];
                hit.Score = rrfScores[chunkId];
                return hit;
            })
            .ToList();
    }

    /// <summary>
    /// Builds filter queries for chunk search (ACL, categories, document IDs).
    /// </summary>
    private List<Func<QueryContainerDescriptor<ChunkIndexModel>, QueryContainer>> BuildChunkFilters(
        List<string>? categories,
        List<Guid>? documentIds,
        string? userId,
        string? userRole,
        List<string>? userAllowedCategories)
    {
        var filters = new List<Func<QueryContainerDescriptor<ChunkIndexModel>, QueryContainer>>();

        // Document ID filter
        if (documentIds?.Any() == true)
        {
            filters.Add(f => f.Terms(t => t
                .Field("document_id")
                .Terms(documentIds.Select(id => id.ToString()))));
        }

        // Category filter
        if (categories?.Any() == true)
        {
            filters.Add(f => f.Terms(t => t
                .Field("category.keyword")
                .Terms(categories)));
        }

        // ACL filter for non-admins
        if (userRole != "Admin" && userId != null)
        {
            var cats = userAllowedCategories ?? new List<string>();

            var aclShould = new List<Func<QueryContainerDescriptor<ChunkIndexModel>, QueryContainer>>
            {
                s => s.Term(t => t.Field("accessLevel").Value("open")),
                s => s.Term(t => t.Field("allowedUserIds").Value(userId))
            };

            foreach (var cat in cats)
            {
                var c = cat;
                aclShould.Add(s => s.Term(t => t.Field("category.keyword").Value(c)));
            }

            filters.Add(fq => fq.Bool(b => b
                .Should(aclShould.ToArray())
                .MinimumShouldMatch(1)));
        }
        else if (userRole != "Admin")
        {
            // No user ID — return no results
            filters.Add(fq => fq.Term(t => t.Field("accessLevel").Value("__impossible__")));
        }

        return filters;
    }

    /// <summary>
    /// Performs kNN semantic search on chunks.
    /// </summary>
    private async Task<List<ChunkSearchHit>> SearchChunksWithKnnAsync(
        string query,
        float[] queryEmbedding,
        int size,
        List<Func<QueryContainerDescriptor<ChunkIndexModel>, QueryContainer>> filters,
        CancellationToken cancellationToken)
    {
        try
        {
            var searchResponse = await _client.SearchAsync<ChunkIndexModel>(s => s
                .Index("ged-chunks")
                .Size(size)
                .Query(q =>
                {
                    var knn = q.Knn(k => k
                        .Field("embedding")
                        .Vector(queryEmbedding)
                        .K(size));

                    if (filters.Any())
                        return q.Bool(b => b.Must(knn).Filter(filters.ToArray()));
                    return knn;
                }),
                cancellationToken);

            if (!searchResponse.IsValid)
            {
                _logger.LogWarning("Chunk kNN search failed: {Error}",
                    searchResponse.ServerError?.Error?.Reason);
                return new List<ChunkSearchHit>();
            }

            return searchResponse.Hits
                .Where(h => (float)(h.Score ?? 0) >= _chunkSemanticThreshold)
                .Select(h => new ChunkSearchHit
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chunk kNN search exception");
            return new List<ChunkSearchHit>();
        }
    }

    /// <summary>
    /// Performs BM25 keyword search on chunks (fallback when kNN unavailable).
    /// </summary>
    private async Task<List<ChunkSearchHit>> SearchChunksWithBm25Async(
        string query,
        int size,
        List<Func<QueryContainerDescriptor<ChunkIndexModel>, QueryContainer>> filters,
        CancellationToken cancellationToken)
    {
        try
        {
            var searchResponse = await _client.SearchAsync<ChunkIndexModel>(s => s
                .Index("ged-chunks")
                .Size(size)
                .Query(q =>
                {
                    var mustQuery = q.MultiMatch(m => m
                        .Query(query)
                        .Fields(f => f
                            .Field(ff => ff.Text, 3.0)
                            .Field(ff => ff.Title, 2.0)
                            .Field(ff => ff.Category, 1.0))
                        .Type(TextQueryType.BestFields)
                        .Operator(Operator.Or));

                    if (filters.Any())
                        return q.Bool(b => b.Must(mustQuery).Filter(filters.ToArray()));
                    return mustQuery;
                }),
                cancellationToken);

            if (!searchResponse.IsValid)
            {
                _logger.LogWarning("Chunk BM25 search failed: {Error}",
                    searchResponse.ServerError?.Error?.Reason);
                return new List<ChunkSearchHit>();
            }

            // Normalize BM25 scores to 0-1 range
            var maxScore = searchResponse.Hits.Max(h => h.Score) ?? 1f;

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
                Score        = maxScore > 0 ? (float)((h.Score ?? 0) / maxScore) : 0
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chunk BM25 search exception");
            return new List<ChunkSearchHit>();
        }
    }

    /// <summary>
    /// Deletes all chunks for a document from the chunks index.
    /// </summary>
    private async Task DeleteChunksForDocumentAsync(Guid documentId, CancellationToken ct)
    {
        try
        {
            await _client.DeleteByQueryAsync<ChunkIndexModel>(d => d
                .Index("ged-chunks")
                .Query(q => q.Term(t => t.Field("document_id").Value(documentId.ToString()))),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete existing chunks for document {DocId}", documentId);
        }
    }

    /// <summary>
    /// Applies NLP-extracted filters to the search request.
    /// </summary>
    private void ApplyNlpFilters(CoreSearchRequest request, NaturalLanguageQuery nlQuery)
    {
        if (nlQuery.ExtractedFilters == null || !nlQuery.ExtractedFilters.Any()) return;

        // Date filters
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
                string.Join(", ", nlQuery.Keywords ?? new List<string>()));
        }

        // File type filter
        var extractedFilters = nlQuery.ExtractedFilters ?? new Dictionary<string, string>();
        _logger.LogInformation("🔍 NLP ExtractedFilters: {Filters}", string.Join(", ", extractedFilters.Select(kv => $"{kv.Key}={kv.Value}")));
        if (extractedFilters.TryGetValue("filetype", out var fileType))
        {
            var keywords = nlQuery.Keywords ?? new List<string>();
            bool hasNonFiletypeKeywords = keywords
                .Any(k => !new[] { "pdf","doc","docx","xls","xlsx","jpg","jpeg","png","txt" }
                    .Contains(k.ToLower()));

            bool hasFiletypeKeyword = keywords
                .Any(k => new[] { "pdf","doc","docx","xls","xlsx","jpg","jpeg","png","txt" }
                    .Contains(k.ToLower()));

            if (!hasNonFiletypeKeywords && hasFiletypeKeyword && keywords.Count == 1)
            {
                // Only keyword is file type — don't filter, search instead
                _logger.LogInformation("⏭️  Skipping filetype filter — only keyword is filetype '{KW}', will search instead", string.Join(", ", keywords));
            }
            else if (!hasNonFiletypeKeywords)
            {
                var contentType = MapFileTypeToContentType(fileType);
                if (!string.IsNullOrEmpty(contentType))
                {
                    request.ContentTypes = new List<string> { contentType };
                    _logger.LogInformation("✅ NLP contentType filter: {Type}", contentType);
                }
            }
        }

        // Category filter from entities
        var categoryEntity = nlQuery.Entities.FirstOrDefault(e => e.StartsWith("CATEGORY:"));
        if (categoryEntity != null && request.Categories == null)
        {
            var category = categoryEntity["CATEGORY:".Length..];
            request.Categories = new List<string> { category };
            _logger.LogInformation("✅ NLP category filter: {Cat}", category);
        }
    }

    /// <summary>
    /// Builds a human-readable NLP summary for display.
    /// </summary>
    private static string? BuildNlpSummary(NaturalLanguageQuery? nlQuery)
    {
        if (nlQuery == null) return null;
        var parts = new List<string>();

        var category = nlQuery.Entities
            .FirstOrDefault(e => e.StartsWith("CATEGORY:"))?["CATEGORY:".Length..];
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

    /// <summary>
    /// Builds the BM25 query with filters, scoring, and ACL enforcement.
    /// </summary>
    private QueryContainer BuildPrecisionQuery(
        QueryContainerDescriptor<DocumentIndexModel> q,
        string query,
        CoreSearchRequest request,
        NaturalLanguageQuery? nlQuery)
    {
        var shouldQueries = new List<Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>>();
        var filterQueries = new List<Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>>();

        // Hard filter: only indexed documents
        filterQueries.Add(fq => fq.Term(t => t.Field("status").Value("Indexed")));

        // ACL filter for non-admin users
        bool isNonAdminWithValidId = request.UserRole != "Admin" && !string.IsNullOrEmpty(request.UserId);
        bool isNonAdminWithoutId = request.UserRole != "Admin" && string.IsNullOrEmpty(request.UserId);

        if (isNonAdminWithoutId)
        {
            // Return no results
            filterQueries.Add(fq => fq.Term(t => t.Field("id").Value(Guid.Empty.ToString())));
        }
        else if (isNonAdminWithValidId)
        {
            var userId = request.UserId;
            var cats   = request.UserAllowedCategories ?? new List<string>();

            var aclShould = new List<Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>>
            {
                s => s.Term(t => t.Field("accessLevel").Value("open")),
                s => s.Term(t => t.Field("allowedUserIds").Value(userId))
            };

            foreach (var cat in cats)
            {
                var c = cat;
                aclShould.Add(s => s.Term(t => t.Field("category.keyword").Value(c)));
            }

            filterQueries.Add(fq => fq.Bool(b => b
                .Should(aclShould.ToArray())
                .MinimumShouldMatch(1)));
        }

        // Optional filters
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

        // Date range filter
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

        // Scoring queries
        if (!string.IsNullOrWhiteSpace(query))
        {
            var cleanQuery = query.Trim();
            var normalized = NormalizeSearchQuery(cleanQuery);
            var variations = GenerateQueryVariations(cleanQuery);

            // Add category aliases
            foreach (var token in cleanQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (CategoryAliases.TryGetValue(token, out var aliasedCategory))
                    variations.Add(aliasedCategory);

            // Exact phrase match on title (highest boost)
            if (cleanQuery.Contains(' '))
                shouldQueries.Add(sq => sq.MatchPhrase(mp => mp
                    .Field(d => d.Title).Query(cleanQuery).Boost(100.0)));

            // Build query variations with boosts
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

            // Additional query types
            shouldQueries.Add(sq => sq.MatchPhrasePrefix(mpp => mpp
                .Field(d => d.Title).Query(cleanQuery).MaxExpansions(20).Boost(35.0)));
            shouldQueries.Add(sq => sq.Match(m => m
                .Field(d => d.ExtractedText).Query(normalized).Operator(Operator.Or).MinimumShouldMatch("30%").Boost(10.0)));
            shouldQueries.Add(sq => sq.Match(m => m
                .Field(d => d.OcrText).Query(normalized).Operator(Operator.Or).MinimumShouldMatch("30%").Boost(5.0)));

            // NLP keyword queries
            if (nlQuery?.Keywords?.Any() == true)
            {
                foreach (var kw in nlQuery.Keywords)
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(kw, @"^\d{4}$"))
                    {
                        // Year keyword — boost all fields
                        shouldQueries.Add(sq => sq.Match(m => m.Field(d => d.ExtractedText).Query(kw).Boost(8.0)));
                        shouldQueries.Add(sq => sq.Match(m => m.Field(d => d.OcrText).Query(kw).Boost(4.0)));
                        shouldQueries.Add(sq => sq.Match(m => m.Field(d => d.Title).Query(kw).Boost(6.0)));
                        continue;
                    }

                    var kwVariations = GenerateQueryVariations(kw);
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

        // Build final query using dis_max for better relevance
        if (shouldQueries.Any())
        {
            var exactMatches = shouldQueries.Take(10).ToList();
            var fuzzyMatches = shouldQueries.Skip(10).ToList();

            return q.Bool(b =>
            {
                var bq = b;
                if (filterQueries.Any()) bq = bq.Filter(filterQueries.ToArray());

                if (exactMatches.Any())
                {
                    bq = bq.Should(m => m.DisMax(dm => dm
                        .Queries(exactMatches.ToArray())
                        .TieBreaker(0.3)));
                }

                if (fuzzyMatches.Any())
                {
                    bq = bq.Should(fuzzyMatches.ToArray())
                        .MinimumShouldMatch(1);
                }
                else if (!exactMatches.Any())
                {
                    bq = bq.Must(m => m.MatchNone());
                }

                return bq;
            });
        }

        // Empty query - return all indexed documents (no must clause, just apply filters)
        return q.Bool(b =>
        {
            var bq = b;
            if (filterQueries.Any()) bq = bq.Filter(filterQueries.ToArray());
            return bq;
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalizes query by handling common plural forms.
    /// </summary>
    private static string NormalizeSearchQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return query;
        var n = query.ToLower().Trim();
        if (n.EndsWith("ies") && n.Length > 4) return n[..^3] + "y";  // factories → factory
        if (n.EndsWith("s") && n.Length > 2 && !n.EndsWith("ss")) return n.TrimEnd('s');
        return n;
    }

    /// <summary>
    /// Generates query variations for fuzzy matching.
    /// </summary>
    private static List<string> GenerateQueryVariations(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();
        var cleaned = query.Trim();
        var lower   = cleaned.ToLower();
        var norm    = NormalizeSearchQuery(lower);
        var cap     = char.ToUpper(lower[0]) + lower[1..];
        return new[] { cleaned, norm, lower, cap, lower + "s" }
            .Distinct()
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
    }

    /// <summary>
    /// Builds sort descriptor for search results.
    /// </summary>
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

    /// <summary>
    /// Builds aggregation descriptors for faceted search.
    /// </summary>
    private static AggregationContainerDescriptor<DocumentIndexModel> BuildAggregations(
        AggregationContainerDescriptor<DocumentIndexModel> agg)
        => agg
            .Terms("categories",    t => t.Field("category.keyword").Size(10))
            .Terms("content_types", t => t.Field(d => d.ContentType).Size(10))
            .Terms("tags",          t => t.Field(d => d.Tags).Size(20))
            .DateHistogram("created_dates", d => d
                .Field(doc => doc.CreatedAt)
                .CalendarInterval(DateInterval.Month));

    /// <summary>
    /// Maps an OpenSearch hit to a <see cref="DocumentSearchHit"/>.
    /// </summary>
    private static DocumentSearchHit MapToSearchHit(IHit<DocumentIndexModel> hit)
    {
        var highlights = hit.Highlight?.Values.SelectMany(v => v).ToList() ?? new();
        var doc        = hit.Source;
        return new DocumentSearchHit
        {
            Id            = doc.Id,
            Title         = doc.Title,
            Description   = doc.Description,
            FileName      = doc.FileName,
            ContentType   = doc.ContentType,
            FileSize      = doc.FileSize,
            CreatedAt     = doc.CreatedAt,
            DocumentDate  = doc.DocumentDate,
            ModifiedAt    = doc.ModifiedAt,
            Category      = doc.Category,
            Tags          = doc.Tags,
            Score         = (float)(hit.Score ?? 0),
            Highlights    = highlights.Any() ? highlights : null,
            Metadata      = doc.Metadata,
            OcrText       = doc.OcrText,
            ExtractedText = doc.ExtractedText,
            Status         = doc.Status,
            IsOcrProcessed = doc.IsOcrProcessed,
            IsFullyProcessed = doc.IsFullyProcessed,
        };
    }

    /// <summary>
    /// Maps a domain <see cref="Document"/> to an index model.
    /// </summary>
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
            Status         = document.Status.ToString(),
            IsOcrProcessed = document.IsOcrProcessed,
            IsFullyProcessed = document.IsFullyProcessed, // Use value from domain model if provided
            ExtractedText = document.ExtractedText,
            OcrText       = document.OcrText,
            Tags          = document.Tags,
            Category      = document.Category,
            Metadata      = SanitizeMetadata(document.Metadata)
            // ACL fields set in IndexDocumentAsync after DB query
        };

    /// <summary>
    /// Sanitizes metadata values for JSON serialization.
    /// </summary>
    private static Dictionary<string, object>? SanitizeMetadata(Dictionary<string, object>? metadata)
    {
        if (metadata == null) return null;
        var result = new Dictionary<string, object>(metadata.Count);
        foreach (var (key, value) in metadata)
            result[key] = FlattenValue(value);
        return result;
    }

    /// <summary>
    /// Flattens complex values to JSON-serializable types.
    /// </summary>
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

    /// <summary>
    /// Maps file type string to MIME content type.
    /// </summary>
    private static string MapFileTypeToContentType(string fileType)
        => fileType.ToLower() switch
        {
            "pdf"         => "application/pdf",
            "doc"         => "application/msword",
            "docx"        => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "xls"         => "application/vnd.ms-excel",
            "xlsx"        => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "jpg" or "jpeg" => "image/jpeg",
            "png"         => "image/png",
            "txt"         => "text/plain",
            _             => string.Empty
        };

    /// <summary>
    /// Extracts facet values from aggregation results.
    /// </summary>
    private Dictionary<string, List<FacetValue>> ExtractFacets(
        IReadOnlyDictionary<string, IAggregate> aggregations)
    {
        var facets = new Dictionary<string, List<FacetValue>>();
        foreach (var agg in aggregations)
            if (agg.Value is BucketAggregate ba)
                facets[agg.Key] = ba.Items.OfType<KeyedBucket<object>>()
                    .Select(b => new FacetValue
                    {
                        Value = b.Key?.ToString() ?? "",
                        Count = (int)(b.DocCount ?? 0)
                    })
                    .ToList();
        return facets;
    }

    // ── Pass-through implementations ─────────────────────────────────────────

    /// <inheritdoc />
    public async Task<NaturalLanguageQuery> ProcessNaturalLanguageQueryAsync(
        string query, CancellationToken cancellationToken = default)
        => await _nlpService.UnderstandQueryAsync(query, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> UpdateDocumentIndexAsync(
        Document document, CancellationToken cancellationToken = default)
    {
        var result = await IndexDocumentAsync(document, cancellationToken);
        
        // Also re-index all chunks to update their ACL fields immediately
        if (result)
        {
            await RefreshChunksAclAsync(document.Id, cancellationToken);
        }
        
        return result;
    }

    /// <summary>
    /// Refreshes ACL fields on all chunks for a document.
    /// Called after ACL grants/revokes to ensure chunks respect the latest access control.
    /// </summary>
    private async Task RefreshChunksAclAsync(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            // Fetch current ACL grants
            var aclUserIds = await _db.DocumentAcls
                .Where(a => a.DocumentId == documentId &&
                            (a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow))
                .Select(a => a.UserId.ToString())
                .ToListAsync(cancellationToken);

            var accessLevel = aclUserIds.Count > 0 ? "restricted" : "open";

            // Search for existing chunks
            var chunksResponse = await _client.SearchAsync<ChunkIndexModel>(s => s
                .Index("ged-chunks")
                .Size(1000)
                .Query(q => q.Term(t => t.Field("document_id").Value(documentId.ToString()))),
                cancellationToken);

            if (!chunksResponse.IsValid || !chunksResponse.Hits.Any())
                return;

            // Update ACL fields on each chunk
            foreach (var hit in chunksResponse.Hits)
            {
                hit.Source.AllowedUserIds = aclUserIds;
                hit.Source.AccessLevel = accessLevel;
            }

            // Bulk update chunks with refreshed ACL
            var bulkResponse = await _client.BulkAsync(b => b
                .Index("ged-chunks")
                .IndexMany(chunksResponse.Hits.Select(h => h.Source)),
                cancellationToken);

            if (bulkResponse.IsValid)
            {
                await _client.Indices.RefreshAsync("ged-chunks", r => r, cancellationToken);
                _logger.LogInformation(
                    "✅ Refreshed ACL on {Count} chunks for document {DocId}",
                    chunksResponse.Hits.Count, documentId);
            }
            else
            {
                _logger.LogWarning("Failed to refresh chunk ACL for document {DocId}", documentId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing chunk ACL for document {DocId}", documentId);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteDocumentIndexAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var updateResponse = await _client.UpdateAsync<DocumentIndexModel, object>(
                documentId.ToString(),
                u => u.Index(_documentIndex).Doc(new { Status = "Deleted" }),
                cancellationToken);

            if (!updateResponse.IsValid)
            {
                _logger.LogWarning("Failed to update document {DocId} status to Deleted in OpenSearch, proceeding with delete", documentId);
            }

            // Delete document from main index
            var response = await _client.DeleteAsync<DocumentIndexModel>(
                documentId.ToString(), d => d.Index(_documentIndex), cancellationToken);

            // Also delete all chunks for this document
            await DeleteChunksForDocumentAsync(documentId, cancellationToken);

            // Refresh the index to ensure immediate visibility of the deletion in search
            await _client.Indices.RefreshAsync(_documentIndex, r => r, cancellationToken);

            return response.IsValid || updateResponse.IsValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document index {Id}", documentId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> BulkIndexDocumentsAsync(
        IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        try
        {
            var docs        = documents.ToList();
            var indexModels = new List<DocumentIndexModel>(docs.Count);
            var semaphore   = new SemaphoreSlim(4, 4);

            // Batch-fetch ACLs for all documents
            var docIds = docs.Select(d => d.Id).ToList();
            
            var allAclRows = await _db.DocumentAcls
                .Where(a => docIds.Contains(a.DocumentId) &&
                            (a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow))
                .Select(a => new { a.DocumentId, a.UserId })
                .ToListAsync(cancellationToken);
                
            var allAcls = allAclRows
                .GroupBy(a => a.DocumentId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(a => a.UserId.ToString()).ToList());

            // Process documents in parallel with semaphore throttling
            var tasks = docs.Select(async doc =>
            {
                var model         = MapToIndexModel(doc);
                var embeddingText = BuildEmbeddingText(doc);

                // Attach ACL snapshot
                model.AllowedUserIds  = allAcls.GetValueOrDefault(doc.Id) ?? new List<string>();
                model.CreatedByUserId = doc.CreatedBy;

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
                .Index(_documentIndex)
                .IndexMany(indexModels),
                cancellationToken);

            if (response.IsValid && !response.ItemsWithErrors.Any())
            {
                await _client.Indices.RefreshAsync(_documentIndex, r => r, cancellationToken);
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

    /// <inheritdoc />
    public async Task<List<DocumentSuggestion>> GetRelatedDocumentsAsync(
        Guid documentId, int count = 5, CancellationToken cancellationToken = default)
    {
        try
        {
            // Try semantic similarity first
            var docResponse = await _client.GetAsync<DocumentIndexModel>(
                documentId.ToString(), g => g.Index(_documentIndex), cancellationToken);

            if (docResponse.Found && docResponse.Source.Embedding != null)
            {
                var embedding   = docResponse.Source.Embedding;
                var knnResponse = await _client.SearchAsync<DocumentIndexModel>(s => s
                    .Index(_documentIndex)
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
                        DocumentId      = h.Source.Id,
                        Title           = h.Source.Title,
                        SimilarityScore = (float)(h.Score ?? 0),
                        Reason          = "Semantic similarity"
                    })
                    .ToList();

                if (semanticSuggestions.Any()) return semanticSuggestions;
            }

            // Fallback to More Like This
            var mltResponse = await _client.SearchAsync<DocumentIndexModel>(s => s
                .Index(_documentIndex)
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
/// OpenSearch index document model for ged-documents index.
/// Embedding field is mapped as knn_vector in Program.cs.
/// </summary>
public class DocumentIndexModel
{
    /// <summary>
    /// Document unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Document title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Document description/summary.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Original filename.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// MIME content type.
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Document creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Extracted document date (e.g., invoice date, contract effective date).
    /// </summary>
    public DateTime? DocumentDate { get; set; }

    /// <summary>
    /// Last modification timestamp.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// Indexing status.
    /// </summary>
    public string Status { get; set; } = "Indexed";

    /// <summary>
    /// Whether OCR has been processed for this document.
    /// </summary>
    public bool IsOcrProcessed { get; set; }

    /// <summary>
    /// Whether the entire OCR pipeline (Tesseract + LLM) is complete.
    /// </summary>
    public bool IsFullyProcessed { get; set; }

    /// <summary>
    /// Extracted text content.
    /// </summary>
    public string? ExtractedText { get; set; }

    /// <summary>
    /// OCR-extracted text.
    /// </summary>
    public string? OcrText { get; set; }

    /// <summary>
    /// Document tags.
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Document category.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// 1024-dimensional bge-m3 vector for kNN search.
    /// </summary>
    public float[]? Embedding { get; set; }

    // ── ACL fields for query-time row-level security ─────────────────────────

    /// <summary>
    /// User IDs with explicit access grants.
    /// </summary>
    public List<string> AllowedUserIds { get; set; } = new();

    /// <summary>
    /// Access level: "open" or "restricted".
    /// </summary>
    public string AccessLevel { get; set; } = "restricted";

    /// <summary>
    /// Username of document creator.
    /// </summary>
    public string? CreatedByUserId { get; set; }
}

/// <summary>
/// OpenSearch index model for ged-chunks index (RAG chunk storage).
/// </summary>
public class ChunkIndexModel
{
    /// <summary>
    /// Parent document ID.
    /// </summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// Unique chunk identifier.
    /// </summary>
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>
    /// Zero-based chunk index within document.
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// Chunk text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Parent document title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Document category.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Document date.
    /// </summary>
    public DateTime? DocumentDate { get; set; }

    /// <summary>
    /// Original filename.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// MIME content type.
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// Document tags.
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Chunk embedding vector.
    /// </summary>
    public float[]? Embedding { get; set; }

    // ACL fields
    /// <summary>
    /// User IDs with access grants.
    /// </summary>
    public List<string> AllowedUserIds { get; set; } = new();

    /// <summary>
    /// Access level: "open" or "restricted".
    /// </summary>
    public string AccessLevel { get; set; } = "restricted";

    /// <summary>
    /// Document creator username.
    /// </summary>
    public string? CreatedByUserId { get; set; }
}
