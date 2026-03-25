using OpenSearch.Client;
using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace GED.Infrastructure.Services.Search;

/// <summary>
/// Handles chunk-level search operations in OpenSearch.
/// </summary>
public class ChunkSearchService
{
    private readonly IOpenSearchClient _client;
    private readonly INlpService _nlpService;
    private readonly AclFilterBuilder _aclFilterBuilder;
    private readonly ILogger<ChunkSearchService> _logger;
    
    private readonly float _chunkSemanticThreshold;
    private readonly float _chunkBm25Weight;
    private readonly float _chunkSemanticWeight;
    private readonly int _chunkRerankFusionK;

    public ChunkSearchService(
        IOpenSearchClient client,
        INlpService nlpService,
        AclFilterBuilder aclFilterBuilder,
        ILogger<ChunkSearchService> logger,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _client = client;
        _nlpService = nlpService;
        _aclFilterBuilder = aclFilterBuilder;
        _logger = logger;
        
        _chunkSemanticThreshold = configuration.GetValue<float>("Search:ChunkSemanticThreshold", 0.20f);
        _chunkBm25Weight = configuration.GetValue<float>("Search:ChunkHybridBm25Weight", 0.4f);
        _chunkSemanticWeight = configuration.GetValue<float>("Search:ChunkHybridSemanticWeight", 0.6f);
        _chunkRerankFusionK = configuration.GetValue<int>("Search:ChunkRerankFusionK", 60);
    }

    /// <summary>
    /// Searches for relevant document chunks using hybrid BM25 + kNN search.
    /// </summary>
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
        var filters = _aclFilterBuilder.BuildChunkAclFilters(userId, userRole, userAllowedCategories);
        
        // Add category filter
        if (categories?.Any() == true)
        {
            filters.Add(f => f.Terms(t => t
                .Field("category.keyword")
                .Terms(categories)));
        }

        // Add document ID filter
        if (documentIds?.Any() == true)
        {
            filters.Add(f => f.Terms(t => t
                .Field("document_id")
                .Terms(documentIds.Select(id => id.ToString()))));
        }

        var searchSize = topK * 2;

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

        // Combine using RRF
        List<ChunkSearchHit> results;
        if (bm25Results.Count > 0 && knnResults.Count > 0)
        {
            results = ApplyReciprocalRankFusion(bm25Results, knnResults, searchSize);
        }
        else if (knnResults.Count > 0)
        {
            results = knnResults;
        }
        else if (bm25Results.Count > 0)
        {
            results = bm25Results;
        }
        else
        {
            results = new List<ChunkSearchHit>();
        }

        return results.Take(topK).ToList();
    }

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
                    ChunkId = h.Source.ChunkId,
                    DocumentId = h.Source.DocumentId,
                    ChunkIndex = h.Source.ChunkIndex,
                    Text = h.Source.Text,
                    Title = h.Source.Title,
                    Category = h.Source.Category,
                    DocumentDate = h.Source.DocumentDate,
                    FileName = h.Source.FileName,
                    ContentType = h.Source.ContentType,
                    Tags = h.Source.Tags,
                    Score = (float)(h.Score ?? 0)
                }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chunk kNN search exception");
            return new List<ChunkSearchHit>();
        }
    }

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

            var maxScore = searchResponse.Hits.Max(h => h.Score) ?? 1f;

            return searchResponse.Hits.Select(h => new ChunkSearchHit
            {
                ChunkId = h.Source.ChunkId,
                DocumentId = h.Source.DocumentId,
                ChunkIndex = h.Source.ChunkIndex,
                Text = h.Source.Text,
                Title = h.Source.Title,
                Category = h.Source.Category,
                DocumentDate = h.Source.DocumentDate,
                FileName = h.Source.FileName,
                ContentType = h.Source.ContentType,
                Tags = h.Source.Tags,
                Score = maxScore > 0 ? (float)((h.Score ?? 0) / maxScore) : 0
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chunk BM25 search exception");
            return new List<ChunkSearchHit>();
        }
    }

    private List<ChunkSearchHit> ApplyReciprocalRankFusion(
        List<ChunkSearchHit> bm25Results,
        List<ChunkSearchHit> knnResults,
        int size)
    {
        var bm25Ranks = bm25Results
            .Select((hit, index) => new { hit.ChunkId, Rank = index + 1 })
            .ToDictionary(x => x.ChunkId, x => x.Rank);

        var knnRanks = knnResults
            .Select((hit, index) => new { hit.ChunkId, Rank = index + 1 })
            .ToDictionary(x => x.ChunkId, x => x.Rank);

        var allChunkIds = bm25Ranks.Keys.Union(knnRanks.Keys);

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

        var sortedChunkIds = rrfScores
            .OrderByDescending(x => x.Value)
            .Take(size)
            .Select(x => x.Key)
            .ToList();

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
}
