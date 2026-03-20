using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

/// <summary>
/// Background service that automatically maintains search index consistency.
/// 
/// This service runs three maintenance tasks at configurable intervals:
/// <list type="number">
///   <item>
///     <term>Stale Document Re-indexing</term>
///     <description>Documents that have been modified since their last indexing are re-indexed.</description>
///   </item>
///   <item>
///     <term>Missing Document Detection</term>
///     <description>Documents marked as Indexed but missing from OpenSearch are re-indexed.</description>
///   </item>
///   <item>
///     <term>Failed Document Retry</term>
///     <description>Documents with Failed status are retried for indexing.</description>
///   </item>
/// </list>
/// 
/// All intervals and batch sizes are configurable via appsettings.json.
/// </summary>
public class AutoReindexService : BackgroundService
{
    private readonly IServiceProvider         _serviceProvider;
    private readonly ILogger<AutoReindexService> _logger;

    /// <summary>
    /// Interval between consecutive checks for stale documents.
    /// </summary>
    private readonly TimeSpan _staleInterval;

    /// <summary>
    /// Interval between consecutive checks for missing documents in OpenSearch.
    /// </summary>
    private readonly TimeSpan _missingInterval;

    /// <summary>
    /// Interval between consecutive retry attempts for failed documents.
    /// </summary>
    private readonly TimeSpan _failedInterval;

    /// <summary>
    /// Maximum number of documents to process per batch.
    /// </summary>
    private readonly int      _batchSize;

    /// <summary>
    /// Whether the service is enabled. When false, the service returns immediately.
    /// </summary>
    private readonly bool     _enabled;

    /// <summary>
    /// Timestamp of the last stale document check (UTC).
    /// </summary>
    private DateTime _lastStaleCheck   = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of the last missing document check (UTC).
    /// </summary>
    private DateTime _lastMissingCheck = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of the last failed document retry check (UTC).
    /// </summary>
    private DateTime _lastFailedCheck  = DateTime.UtcNow;

    /// <summary>
    /// Initializes a new instance of <see cref="AutoReindexService"/>.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scoped dependencies.</param>
    /// <param name="logger">Logger for service events.</param>
    /// <param name="configuration">Application configuration with Reindex:* settings.</param>
    public AutoReindexService(
        IServiceProvider          serviceProvider,
        ILogger<AutoReindexService> logger,
        IConfiguration            configuration)
    {
        _serviceProvider = serviceProvider;
        _logger          = logger;
        _enabled         = configuration.GetValue<bool>("Reindex:Enabled", true);
        _batchSize       = configuration.GetValue<int>("Reindex:BatchSize", 50);
        _staleInterval   = TimeSpan.FromMinutes(
                               configuration.GetValue<double>("Reindex:StaleCheckIntervalMinutes", 5));
        _missingInterval = TimeSpan.FromMinutes(
                               configuration.GetValue<double>("Reindex:MissingCheckIntervalMinutes", 15));
        _failedInterval  = TimeSpan.FromMinutes(
                               configuration.GetValue<double>("Reindex:FailedRetryIntervalMinutes", 30));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("⏸️  AutoReindexService is disabled via config");
            return;
        }

        _logger.LogInformation(
            "🔄 AutoReindexService started — stale={Stale}m, missing={Missing}m, failed={Failed}m",
            _staleInterval.TotalMinutes,
            _missingInterval.TotalMinutes,
            _failedInterval.TotalMinutes);

        // Wait for system to stabilize before starting checks
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            try
            {
                // Check and process stale documents
                if (now - _lastStaleCheck >= _staleInterval)
                {
                    await ReindexStaleDocumentsAsync(stoppingToken);
                    _lastStaleCheck = DateTime.UtcNow;
                }

                // Check for documents missing from OpenSearch
                if (now - _lastMissingCheck >= _missingInterval)
                {
                    await ReindexMissingDocumentsAsync(stoppingToken);
                    _lastMissingCheck = DateTime.UtcNow;
                }

                // Retry failed document indexing
                if (now - _lastFailedCheck >= _failedInterval)
                {
                    await RetryFailedDocumentsAsync(stoppingToken);
                    _lastFailedCheck = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in AutoReindexService loop");
            }

            // Wait 1 minute before next iteration
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }

        _logger.LogInformation("🛑 AutoReindexService stopped");
    }

    /// <summary>
    /// Re-indexes documents that have been modified since their last indexing.
    /// Compares <c>ModifiedAt</c> against <c>last_indexed_at</c> metadata.
    /// </summary>
    private async Task ReindexStaleDocumentsAsync(CancellationToken ct)
    {
        using var scope        = _serviceProvider.CreateScope();
        var db                 = scope.ServiceProvider.GetRequiredService<GedDbContext>();
        var searchService      = scope.ServiceProvider.GetRequiredService<ISearchService>();

        try
        {
            // Fetch candidate documents (modified after indexing)
            var candidates = await db.Documents
                .AsNoTracking()
                .Where(d => d.Status == DocumentStatus.Indexed &&
                            d.ModifiedAt.HasValue)
                .OrderByDescending(d => d.ModifiedAt)
                .Take(_batchSize * 2)
                .ToListAsync(ct);

            // Filter to only truly stale documents
            var stale = candidates.Where(d =>
            {
                if (d.Metadata == null) return true;
                if (!d.Metadata.TryGetValue("last_indexed_at", out var val)) return true;
                if (!DateTime.TryParse(val?.ToString(), out var lastIndexed)) return true;
                return d.ModifiedAt!.Value > lastIndexed;
            }).Take(_batchSize).ToList();

            if (!stale.Any())
            {
                _logger.LogDebug("✅ No stale documents to re-index");
                return;
            }

            _logger.LogInformation("🔄 Re-indexing {Count} stale documents", stale.Count);

            // Convert and bulk index
            var domainDocs = stale.Select(MapToDomain).ToList();
            await searchService.BulkIndexDocumentsAsync(domainDocs, ct);

            // Update last_indexed_at metadata for each re-indexed document
            using var writeScope = _serviceProvider.CreateScope();
            var writeDb          = writeScope.ServiceProvider.GetRequiredService<GedDbContext>();

            foreach (var entity in stale)
            {
                var tracked = await writeDb.Documents.FindAsync(
                    new object[] { entity.Id }, ct);
                if (tracked == null) continue;
                tracked.Metadata ??= new Dictionary<string, object>();
                tracked.Metadata["last_indexed_at"] = DateTime.UtcNow.ToString("o");
            }

            await writeDb.SaveChangesAsync(ct);
            _logger.LogInformation("✅ Re-indexed {Count} stale documents", stale.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during stale document re-index check");
        }
    }

    /// <summary>
    /// Detects and re-indexes documents that exist in the database but are missing from OpenSearch.
    /// Uses multi-get API to check existence without full re-indexing.
    /// </summary>
    private async Task ReindexMissingDocumentsAsync(CancellationToken ct)
    {
        using var scope   = _serviceProvider.CreateScope();
        var db            = scope.ServiceProvider.GetRequiredService<GedDbContext>();
        var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();

        try
        {
            // Get indexed documents from database
            var candidates = await db.Documents
                .AsNoTracking()
                .Where(d => d.Status == DocumentStatus.Indexed)
                .OrderByDescending(d => d.CreatedAt)
                .Take(_batchSize)
                .ToListAsync(ct);

            if (!candidates.Any()) return;

            // Check which ones are missing from OpenSearch
            var missing = await FindMissingFromOpenSearchAsync(
                candidates.Select(d => d.Id).ToList(),
                scope.ServiceProvider,
                ct);

            if (!missing.Any())
            {
                _logger.LogDebug("✅ No missing documents in OpenSearch");
                return;
            }

            _logger.LogWarning("⚠️  {Count} documents missing from OpenSearch — re-indexing", missing.Count);

            // Re-index only the missing documents
            var toIndex = candidates
                .Where(d => missing.Contains(d.Id))
                .Select(MapToDomain)
                .ToList();

            await searchService.BulkIndexDocumentsAsync(toIndex, ct);
            _logger.LogInformation("✅ Re-indexed {Count} previously missing documents", toIndex.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during missing document check");
        }
    }

    /// <summary>
    /// Retries indexing for documents that previously failed.
    /// Updates status to Indexed on success.
    /// </summary>
    private async Task RetryFailedDocumentsAsync(CancellationToken ct)
    {
        using var scope   = _serviceProvider.CreateScope();
        var db            = scope.ServiceProvider.GetRequiredService<GedDbContext>();
        var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();

        try
        {
            // Get failed documents ordered by modification time (oldest first)
            var failed = await db.Documents
                .AsNoTracking()
                .Where(d => d.Status == DocumentStatus.Failed)
                .OrderBy(d => d.ModifiedAt)
                .Take(_batchSize)
                .ToListAsync(ct);

            if (!failed.Any())
            {
                _logger.LogDebug("✅ No failed documents to retry");
                return;
            }

            _logger.LogInformation("🔄 Retrying indexing for {Count} failed documents", failed.Count);

            int successCount = 0;

            foreach (var entity in failed)
            {
                try
                {
                    var domain = MapToDomain(entity);
                    var ok     = await searchService.IndexDocumentAsync(domain, ct);

                    if (ok)
                    {
                        // Update document status on successful indexing
                        using var retryScope = _serviceProvider.CreateScope();
                        var retryDb = retryScope.ServiceProvider
                            .GetRequiredService<GedDbContext>();

                        var tracked = await retryDb.Documents
                            .FindAsync(new object[] { entity.Id }, ct);

                        if (tracked != null)
                        {
                            tracked.Status     = DocumentStatus.Indexed;
                            tracked.ModifiedAt = DateTime.UtcNow;  // explicitly UTC
                            await retryDb.SaveChangesAsync(ct);
                        }

                        successCount++;
                        _logger.LogInformation("✅ Retry successful for document {DocumentId}", entity.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Retry failed for document {DocumentId}", entity.Id);
                }

                // Delay between retries to avoid overwhelming the search service
                await Task.Delay(200, ct);
            }

            _logger.LogInformation(
                "✅ Retry completed: {Success}/{Total} succeeded",
                successCount, failed.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during failed document retry");
        }
    }

    /// <summary>
    /// Checks OpenSearch for document existence using multi-get API.
    /// </summary>
    /// <param name="ids">Document IDs to check.</param>
    /// <param name="scopedProvider">Scoped service provider for OpenSearch client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Set of document IDs not found in OpenSearch.</returns>
    private async Task<HashSet<Guid>> FindMissingFromOpenSearchAsync(
        List<Guid> ids,
        IServiceProvider scopedProvider,
        CancellationToken ct)
    {
        try
        {
            var client = scopedProvider
                .GetRequiredService<OpenSearch.Client.IOpenSearchClient>();

            // Multi-get request to check all IDs at once
            var mgetResponse = await client.MultiGetAsync(mg => mg
                .Index("ged-documents")
                .GetMany<DocumentIndexModel>(ids.Select(id => id.ToString())),
                ct);

            // Collect found document IDs
            var found = mgetResponse.Hits
                .Where(h => h.Found)
                .Select(h => Guid.Parse(h.Id))
                .ToHashSet();

            // Return IDs that were not found
            return ids.Where(id => !found.Contains(id)).ToHashSet();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check OpenSearch for missing documents — skipping");
            return new HashSet<Guid>();
        }
    }

    /// <summary>
    /// Triggers a full reindex of all indexed documents.
    /// Can be called manually via the API.
    /// </summary>
    public async Task<ReindexResult> TriggerFullReindexAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("🔄 Full reindex triggered manually");

        using var scope        = _serviceProvider.CreateScope();
        var db                = scope.ServiceProvider.GetRequiredService<GedDbContext>();
        var searchService     = scope.ServiceProvider.GetRequiredService<ISearchService>();

        try
        {
            var totalDocs = await db.Documents
                .CountAsync(d => d.Status == DocumentStatus.Indexed, ct);

            if (totalDocs == 0)
            {
                _logger.LogInformation("No documents to reindex");
                return new ReindexResult { Total = 0, Succeeded = 0, Failed = 0 };
            }

            _logger.LogInformation("🔄 Full reindex: {Total} documents to process", totalDocs);

            var processed = 0;
            var failed    = 0;

            var batches = Enumerable.Range(0, (int)Math.Ceiling(totalDocs / (double)_batchSize));

            foreach (var batchNum in batches)
            {
                var batch = await db.Documents
                    .AsNoTracking()
                    .Where(d => d.Status == DocumentStatus.Indexed)
                    .OrderBy(d => d.CreatedAt)
                    .Skip(batchNum * _batchSize)
                    .Take(_batchSize)
                    .ToListAsync(ct);

                if (!batch.Any()) break;

                try
                {
                    var domainDocs = batch.Select(MapToDomain).ToList();
                    await searchService.BulkIndexDocumentsAsync(domainDocs, ct);

                    // Update last_indexed_at
                    var ids = batch.Select(d => d.Id).ToList();
                    var tracked = await db.Documents
                        .Where(d => ids.Contains(d.Id))
                        .ToListAsync(ct);

                    foreach (var entity in tracked)
                    {
                        entity.Metadata ??= new Dictionary<string, object>();
                        entity.Metadata["last_indexed_at"] = DateTime.UtcNow.ToString("o");
                    }
                    await db.SaveChangesAsync(ct);

                    processed += batch.Count;
                    _logger.LogInformation("✅ Reindex batch {Batch}: {Count}/{Total} docs indexed",
                        batchNum + 1, processed, totalDocs);
                }
                catch (Exception ex)
                {
                    failed += batch.Count;
                    _logger.LogError(ex, "❌ Reindex batch {Batch} failed", batchNum + 1);
                }
            }

            _logger.LogInformation(
                "🏁 Full reindex complete: {Processed} succeeded, {Failed} failed",
                processed, failed);

            return new ReindexResult
            {
                Total = totalDocs,
                Succeeded = processed,
                Failed = failed,
                Message = $"Reindexed {processed}/{totalDocs} documents ({failed} failed)"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Full reindex failed");
            return new ReindexResult
            {
                Total = 0,
                Succeeded = 0,
                Failed = 0,
                Message = $"Reindex failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Maps a database entity to a domain <see cref="Document"/> model.
    /// </summary>
    private static Document MapToDomain(DocumentEntity e) => new()
    {
        Id            = e.Id,
        Title         = e.Title,
        Description   = e.Description,
        FileName      = e.FileName,
        FilePath      = e.FilePath,
        ContentType   = e.ContentType,
        FileSize      = e.FileSize,
        CreatedAt     = e.CreatedAt,
        DocumentDate  = e.DocumentDate,
        ModifiedAt    = e.ModifiedAt,
        Status        = e.Status,
        OcrText       = e.OcrText,
        ExtractedText = e.ExtractedText,
        Tags          = e.Tags,
        Category      = e.Category,
        Metadata      = e.Metadata,
        IsOcrProcessed = e.IsOcrProcessed
    };
}

public class ReindexResult
{
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}
