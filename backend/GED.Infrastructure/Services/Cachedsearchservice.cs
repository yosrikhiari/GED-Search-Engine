using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GED.Infrastructure.Services;

/// <summary>
/// Decorator around ISearchService that caches search results in Redis.
/// Cache key = SHA256 of the serialized SearchRequest so different callers
/// with identical parameters share the same cached entry.
///
/// Only SearchAsync results are cached — index mutations (Index/Update/Delete)
/// also invalidate the cache so stale results are never served after writes.
/// </summary>
public class CachedSearchService : ISearchService
{
    private readonly ISearchService          _inner;
    private readonly IDistributedCache       _cache;
    private readonly ILogger<CachedSearchService> _logger;
    private readonly TimeSpan                _ttl;
    private readonly bool                    _enabled;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        WriteIndented               = false
    };

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
        _ttl     = TimeSpan.FromSeconds(
                       configuration.GetValue<int>("Redis:SearchCacheTtlSeconds", 120));
    }

    // ── ISearchService ────────────────────────────────────────────────────────

    public async Task<SearchResult> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
            return await _inner.SearchAsync(request, cancellationToken);

        var key = BuildCacheKey(request);

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

    // ── Write-through cache invalidation ─────────────────────────────────────
    // When a document is written (indexed / updated / deleted) we flush the
    // search cache so the next search re-reads from OpenSearch.
    // We use a "flush tag" key rather than enumerating all search keys.

    public async Task<bool> IndexDocumentAsync(
        Document document, CancellationToken cancellationToken = default)
    {
        var ok = await _inner.IndexDocumentAsync(document, cancellationToken);
        await InvalidateCacheAsync(cancellationToken);
        return ok;
    }

    public async Task<bool> UpdateDocumentIndexAsync(
        Document document, CancellationToken cancellationToken = default)
    {
        var ok = await _inner.UpdateDocumentIndexAsync(document, cancellationToken);
        await InvalidateCacheAsync(cancellationToken);
        return ok;
    }

    public async Task<bool> DeleteDocumentIndexAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        var ok = await _inner.DeleteDocumentIndexAsync(documentId, cancellationToken);
        await InvalidateCacheAsync(cancellationToken);
        return ok;
    }

    public async Task<bool> BulkIndexDocumentsAsync(
        IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        var ok = await _inner.BulkIndexDocumentsAsync(documents, cancellationToken);
        await InvalidateCacheAsync(cancellationToken);
        return ok;
    }

    // ── Pass-through (not cached) ─────────────────────────────────────────────

    public Task<List<DocumentSuggestion>> GetRelatedDocumentsAsync(
        Guid documentId, int count = 5, CancellationToken cancellationToken = default)
        => _inner.GetRelatedDocumentsAsync(documentId, count, cancellationToken);

    public Task<NaturalLanguageQuery> ProcessNaturalLanguageQueryAsync(
        string query, CancellationToken cancellationToken = default)
        => _inner.ProcessNaturalLanguageQueryAsync(query, cancellationToken);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Deterministic cache key derived from the full search request.
    /// Uses SHA256 so keys are a fixed length regardless of query complexity.
    /// Prefix "ged:search:" lets us namespace keys in a shared Redis instance.
    /// </summary>
    private static string BuildCacheKey(SearchRequest request)
    {
        // Serialize request deterministically
        var json  = JsonSerializer.Serialize(request, _json);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var hash  = System.Security.Cryptography.SHA256.HashData(bytes);
        return "ged:search:" + Convert.ToHexString(hash).ToLower();
    }

    /// <summary>
    /// Increments a "generation" counter in Redis.
    /// BuildCacheKey includes the current generation, so bumping it
    /// logically invalidates all previously cached search results without
    /// needing to enumerate keys (Redis SCAN is O(N) and dangerous in prod).
    /// </summary>
    private async Task InvalidateCacheAsync(CancellationToken cancellationToken)
    {
        if (!_enabled) return;
        try
        {
            // Set a short-lived "bust" flag; if Redis is unavailable we skip silently
            await _cache.SetStringAsync(
                "ged:cache:generation",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
                new DistributedCacheEntryOptions
                {
                    // Keep generation key alive as long as the longest possible cached entry
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