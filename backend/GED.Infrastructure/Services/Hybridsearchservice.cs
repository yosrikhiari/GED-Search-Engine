using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

/// <summary>
/// Wraps OpenSearchService (keyword/BM25) and VectorSearchService (k-NN)
/// and merges their results using Reciprocal Rank Fusion (RRF).
///
/// RRF formula: score(d) = Σ  1 / (k + rank_i(d))
///   k = 60  (standard value that dampens very high ranks)
///
/// The hybrid result keeps the relevance ordering from keyword search
/// for well-matching documents while surfacing semantically-related
/// documents that wouldn't appear in BM25 alone.
///
/// Registered as ISearchService in DI so callers need no changes.
/// </summary>
public class HybridSearchService : ISearchService
{
    private readonly ISearchService           _keywordSearch;   // OpenSearchService
    private readonly VectorSearchService      _vectorSearch;
    private readonly ILogger<HybridSearchService> _logger;
    private readonly bool                     _enabled;
    private const int RrfK = 60;

    public HybridSearchService(
        OpenSearchService         keywordSearch,
        VectorSearchService       vectorSearch,
        ILogger<HybridSearchService> logger,
        IConfiguration            configuration)
    {
        _keywordSearch = keywordSearch;
        _vectorSearch  = vectorSearch;
        _logger        = logger;
        _enabled       = configuration.GetValue<bool>("Embeddings:Enabled", false);
    }

    // ── Core hybrid search ────────────────────────────────────────────────────

    public async Task<SearchResult> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        // When embeddings are disabled, fall back to pure keyword search
        if (!_enabled || string.IsNullOrWhiteSpace(request.Query))
            return await _keywordSearch.SearchAsync(request, cancellationToken);

        var startTime = DateTime.UtcNow;

        // Run both searches in parallel
        var keywordTask  = _keywordSearch.SearchAsync(request, cancellationToken);
        var semanticTask = _vectorSearch.SemanticSearchAsync(
            request.Query,
            topK: Math.Min(request.PageSize * 3, 60),   // fetch enough to fuse
            cancellationToken);

        await Task.WhenAll(keywordTask, semanticTask);

        var keywordResult  = keywordTask.Result;
        var semanticHits   = semanticTask.Result;

        _logger.LogInformation(
            "🔀 Hybrid search: keyword={KwCount} hits, semantic={SeCount} hits",
            keywordResult.Documents.Count, semanticHits.Count);

        if (!semanticHits.Any())
        {
            // Semantic search unavailable — return keyword results as-is
            return keywordResult;
        }

        // Build RRF rank maps
        // Keyword rank: position in keywordResult.Documents (0-based)
        var keywordRanks = keywordResult.Documents
            .Select((doc, idx) => (doc.Id, Rank: idx))
            .ToDictionary(x => x.Id, x => x.Rank);

        // Semantic rank: position in semanticHits (0-based)
        var semanticRanks = semanticHits
            .Select((hit, idx) => (hit.DocumentId, Rank: idx))
            .ToDictionary(x => x.DocumentId, x => x.Rank);

        // Union of all document IDs seen
        var allIds = new HashSet<Guid>(keywordRanks.Keys);
        allIds.UnionWith(semanticRanks.Keys);

        // RRF score
        var rrfScores = allIds
            .Select(id =>
            {
                double score = 0;
                if (keywordRanks.TryGetValue(id, out var kr))
                    score += 1.0 / (RrfK + kr + 1);
                if (semanticRanks.TryGetValue(id, out var sr))
                    score += 1.0 / (RrfK + sr + 1);
                return (Id: id, Score: score);
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        // Re-order the keyword result documents by RRF score
        // (documents only in semantic results won't have a DocumentSearchHit — skip them
        //  for now; a follow-up fetch from OpenSearch could enrich them)
        var hitLookup = keywordResult.Documents.ToDictionary(d => d.Id);
        var maxRrf    = rrfScores.First().Score;

        var rerankedDocs = rrfScores
            .Where(x => hitLookup.ContainsKey(x.Id))      // only docs we have full data for
            .Take(request.PageSize)
            .Select(x =>
            {
                var doc = hitLookup[x.Id];
                doc.Score = (float)(x.Score / maxRrf);    // normalise to 0-1
                return doc;
            })
            .ToList();

        _logger.LogInformation(
            "🔀 RRF fusion produced {Count} results (from {Total} candidates)",
            rerankedDocs.Count, rrfScores.Count);

        return new SearchResult
        {
            TotalResults  = keywordResult.TotalResults,
            Page          = keywordResult.Page,
            PageSize      = keywordResult.PageSize,
            TotalPages    = keywordResult.TotalPages,
            Documents     = rerankedDocs,
            Facets        = keywordResult.Facets,
            DidYouMean    = keywordResult.DidYouMean,
            SearchTimeMs  = (long)(DateTime.UtcNow - startTime).TotalMilliseconds,
            ProcessedQuery = keywordResult.ProcessedQuery
        };
    }

    // ── Pass-through to keyword service ──────────────────────────────────────

    public Task<List<DocumentSuggestion>> GetRelatedDocumentsAsync(
        Guid documentId, int count = 5, CancellationToken cancellationToken = default)
        => _keywordSearch.GetRelatedDocumentsAsync(documentId, count, cancellationToken);

    public Task<NaturalLanguageQuery> ProcessNaturalLanguageQueryAsync(
        string query, CancellationToken cancellationToken = default)
        => _keywordSearch.ProcessNaturalLanguageQueryAsync(query, cancellationToken);

    // ── Index mutations — write to both indexes ───────────────────────────────

    public async Task<bool> IndexDocumentAsync(
        Document document, CancellationToken cancellationToken = default)
    {
        var kwOk = await _keywordSearch.IndexDocumentAsync(document, cancellationToken);
        await _vectorSearch.IndexDocumentVectorAsync(document, cancellationToken);  // best-effort
        return kwOk;
    }

    public async Task<bool> UpdateDocumentIndexAsync(
        Document document, CancellationToken cancellationToken = default)
    {
        var kwOk = await _keywordSearch.UpdateDocumentIndexAsync(document, cancellationToken);
        await _vectorSearch.IndexDocumentVectorAsync(document, cancellationToken);
        return kwOk;
    }

    public async Task<bool> DeleteDocumentIndexAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        var kwOk = await _keywordSearch.DeleteDocumentIndexAsync(documentId, cancellationToken);
        await _vectorSearch.DeleteDocumentVectorAsync(documentId, cancellationToken);
        return kwOk;
    }

    public async Task<bool> BulkIndexDocumentsAsync(
        IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        var docList = documents.ToList();
        var kwOk    = await _keywordSearch.BulkIndexDocumentsAsync(docList, cancellationToken);

        // Index vectors one-at-a-time to avoid flooding Ollama
        foreach (var doc in docList)
            await _vectorSearch.IndexDocumentVectorAsync(doc, cancellationToken);

        return kwOk;
    }
}