using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GED.Infrastructure.Services;

/// <summary>
/// Decorator around <see cref="ISearchService"/> that caches search results in Redis.
/// 
/// Cache key = SHA256 hash of the serialized <see cref="SearchRequest"/> plus the current
/// cache generation, so different callers with identical parameters share the same cached entry.
/// 
/// Only <see cref="SearchAsync"/> results are cached — index mutations (Index/Update/Delete)
/// also invalidate the cache via generation increment, ensuring stale results are never served after writes.
/// 
/// Cache invalidation strategy uses a "flush tag" approach: instead of enumerating all cached
/// search keys (which is O(N) in Redis), we increment a generation counter. Since
/// <see cref="BuildCacheKeyAsync"/> includes the generation in the hash, all previous
/// cache entries become logically invalid without needing deletion.
/// </summary>
public class CachedSearchService : ISearchService
{
    private readonly ISearchService          _inner;
    private readonly IDistributedCache       _cache;
    private readonly ILogger<CachedSearchService> _logger;

    /// <summary>
    /// Time-to-live for cached search results.
    /// Includes ±20s jitter to prevent thundering herd on cache expiration.
    /// </summary>
    private readonly TimeSpan _ttl;

    /// <summary>
    /// Whether Redis caching is enabled. When false, all requests pass through to the inner service.
    /// </summary>
    private readonly bool     _enabled;

    /// <summary>
    /// JSON serialization options for cache key generation and result serialization.
    /// Uses camelCase naming for consistency with JSON standards.
    /// </summary>
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        WriteIndented               = false
    };

    /// <summary>
    /// Initializes a new instance of <see cref="CachedSearchService"/>.
    /// </summary>
    /// <param name="inner">The wrapped search service to cache results from.</param>
    /// <param name="cache">The distributed cache implementation (Redis).</param>
    /// <param name="logger">Logger for cache events.</param>
    /// <param name="configuration">Application configuration with Redis:* settings.</param>
    public CachedSearchService(
        ISearchService          inner,
        IDistributedCache       cache,
        ILogger<CachedSearchService> logger,
        IConfiguration          configuration)
    {
        _inner   = inner;
        _cache   = cache;
        _logger  = logger;
        _enabled = configuration.GetValue<bool>("Redis:Enabled", true);
        _ttl = TimeSpan.FromSeconds(
            configuration.GetValue<int>("Redis:SearchCacheTtlSeconds", 120)
            + Random.Shared.Next(-20, 20)  // ±20s jitter to prevent thundering herd
        );
    }

    /// <inheritdoc />
    public async Task<SearchResult> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        // Bypass cache entirely when disabled
        if (!_enabled)
            return await _inner.SearchAsync(request, cancellationToken);

        var key = await BuildCacheKeyAsync(request, cancellationToken);

        // Try cache first
        try
        {
            var cached = await _cache.GetStringAsync(key, cancellationToken);
            if (cached != null)
            {
                var result = JsonSerializer.Deserialize<SearchResult>(cached, _json);
                if (result != null)
                {
                    _logger.LogDebug("🔴 Cache HIT  key={Key}", key);
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            // Redis is down — degrade gracefully, don't fail the search
            _logger.LogWarning(ex, "Redis read failed, falling through to OpenSearch");
        }

        _logger.LogDebug("🔴 Cache MISS key={Key}", key);

        // Call real service
        var searchResult = await _inner.SearchAsync(request, cancellationToken);

        // Store in cache (best-effort)
        try
        {
            var json = JsonSerializer.Serialize(searchResult, _json);
            await _cache.SetStringAsync(key, json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl },
                cancellationToken);
            _logger.LogDebug("🔴 Cache SET  key={Key} ttl={Ttl}s", key, _ttl.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis write failed — result not cached");
        }

        return searchResult;
    }

    /// <inheritdoc />
    public async Task<bool> IndexDocumentAsync(
        Document document, CancellationToken cancellationToken = default)
    {
        var ok = await _inner.IndexDocumentAsync(document, cancellationToken);
        await InvalidateCacheAsync(cancellationToken);
        return ok;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateDocumentIndexAsync(
        Document document, CancellationToken cancellationToken = default)
    {
        var ok = await _inner.UpdateDocumentIndexAsync(document, cancellationToken);
        await InvalidateCacheAsync(cancellationToken);
        return ok;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteDocumentIndexAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        var ok = await _inner.DeleteDocumentIndexAsync(documentId, cancellationToken);
        await InvalidateCacheAsync(cancellationToken);
        return ok;
    }

    /// <inheritdoc />
    public async Task<bool> BulkIndexDocumentsAsync(
        IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        var ok = await _inner.BulkIndexDocumentsAsync(documents, cancellationToken);
        await InvalidateCacheAsync(cancellationToken);
        return ok;
    }

    /// <inheritdoc />
    public Task<List<DocumentSuggestion>> GetRelatedDocumentsAsync(
        Guid documentId, int count = 5, CancellationToken cancellationToken = default)
        => _inner.GetRelatedDocumentsAsync(documentId, count, cancellationToken);

    /// <inheritdoc />
    public Task<NaturalLanguageQuery> ProcessNaturalLanguageQueryAsync(
        string query, CancellationToken cancellationToken = default)
        => _inner.ProcessNaturalLanguageQueryAsync(query, cancellationToken);

    /// <inheritdoc />
    public Task IndexChunksAsync(Document document, List<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        // Forward to inner service (chunks are not cached separately)
        return _inner.IndexChunksAsync(document, chunks, cancellationToken);
    }

    /// <summary>
    /// Builds a deterministic cache key from the search request.
    /// 
    /// The key is a SHA256 hash of "generation:serialized_request", prefixed with "ged:search:".
    /// The generation is read from Redis and included in the key — when the generation
    /// changes, all previous keys become logically invalid without needing enumeration.
    /// </summary>
    /// <param name="request">The search request to generate a key for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A cache key string prefixed with "ged:search:".</returns>
    private async Task<string> BuildCacheKeyAsync(
        SearchRequest request, CancellationToken ct)
    {
        // Read current generation — this is what actually makes invalidation work
        string generation = "0";
        try
        {
            var gen = await _cache.GetStringAsync("ged:cache:generation", ct);
            generation = gen ?? "0";
        }
        catch { /* Redis down — use generation 0, degrade gracefully */ }

        var json    = JsonSerializer.Serialize(request, _json);
        var payload = generation + ":" + json;
        var bytes   = System.Text.Encoding.UTF8.GetBytes(payload);
        var hash    = System.Security.Cryptography.SHA256.HashData(bytes);
        return "ged:search:" + Convert.ToHexString(hash).ToLower();
    }

    /// <summary>
    /// Invalidates all cached search results by incrementing the generation counter.
    /// 
    /// This uses a generation-based invalidation strategy: instead of enumerating all
    /// cached search keys (which is O(N) and dangerous in production Redis),
    /// we increment a single "ged:cache:generation" key. Since BuildCacheKey
    /// includes the current generation in the hash, all previously cached
    /// searches become logically invalid with O(1) cost.
    /// </summary>
    private async Task InvalidateCacheAsync(CancellationToken cancellationToken)
    {
        if (!_enabled) return;
        try
        {
            // Set generation to current Unix timestamp (milliseconds)
            await _cache.SetStringAsync(
                "ged:cache:generation",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
                new DistributedCacheEntryOptions
                {
                    // Keep generation slightly longer than TTL to ensure no overlap
                    AbsoluteExpirationRelativeToNow = _ttl + TimeSpan.FromMinutes(5)
                },
                cancellationToken);

            _logger.LogInformation("🔴 Cache invalidated (document write detected)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache invalidation failed — Redis may be unavailable");
        }
    }
}
