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
/// Background service that periodically reconciles PostgreSQL documents
/// with the OpenSearch index.
///
/// Three reconciliation jobs run on independent schedules:
///
///   1. STALE CHECK  (every 5 min, configurable)
///      Finds documents whose DB ModifiedAt > their OpenSearch indexed time.
///      Happens naturally after OCR completes or a document is edited via the API.
///
///   2. MISSING CHECK  (every 15 min, configurable)
///      Finds documents with Status=Indexed in DB but absent from OpenSearch.
///      Covers restarts where OpenSearch lost data (e.g., index was recreated).
///
///   3. FAILED RETRY  (every 30 min, configurable)
///      Finds documents with Status=Failed and retries indexing them.
///      Gives transient failures (network blip, OpenSearch restarting) a chance
///      to heal without manual intervention.
///
/// All jobs process in configurable batch sizes to avoid flooding OpenSearch.
/// </summary>
public class AutoReindexService : BackgroundService
{
    private readonly IServiceProvider         _serviceProvider;
    private readonly ILogger<AutoReindexService> _logger;

    // Schedules (configurable via appsettings)
    private readonly TimeSpan _staleInterval;
    private readonly TimeSpan _missingInterval;
    private readonly TimeSpan _failedInterval;
    private readonly int      _batchSize;
    private readonly bool     _enabled;

    // Track when we last ran each job so independent timers aren't needed
    private DateTime _lastStaleCheck   = DateTime.MinValue;
    private DateTime _lastMissingCheck = DateTime.MinValue;
    private DateTime _lastFailedCheck  = DateTime.MinValue;

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

        // Small startup delay — let OpenSearch and Postgres finish initialising
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            try
            {
                if (now - _lastStaleCheck >= _staleInterval)
                {
                    await ReindexStaleDocumentsAsync(stoppingToken);
                    _lastStaleCheck = DateTime.UtcNow;
                }

                if (now - _lastMissingCheck >= _missingInterval)
                {
                    await ReindexMissingDocumentsAsync(stoppingToken);
                    _lastMissingCheck = DateTime.UtcNow;
                }

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

            // Poll every minute — the per-job logic decides whether to actually run
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }

        _logger.LogInformation("🛑 AutoReindexService stopped");
    }

    // ── Job 1: Re-index documents modified after their last index time ────────

    private async Task ReindexStaleDocumentsAsync(CancellationToken ct)
    {
        using var scope        = _serviceProvider.CreateScope();
        var db                 = scope.ServiceProvider.GetRequiredService<GedDbContext>();
        var searchService      = scope.ServiceProvider.GetRequiredService<ISearchService>();

        try
        {
            // We track last-indexed time in the document's Metadata dict
            // (key: "last_indexed_at"). Any document where ModifiedAt >
            // last_indexed_at is considered stale.
            //
            // EF JSONB query: filter in memory after fetching candidates to
            // avoid complex JSONB predicates — batch is small so this is fine.
            var candidates = await db.Documents
                .AsNoTracking()
                .Where(d => d.Status == DocumentStatus.Indexed &&
                            d.ModifiedAt.HasValue)
                .OrderByDescending(d => d.ModifiedAt)
                .Take(_batchSize * 2)         // over-fetch, filter in memory
                .ToListAsync(ct);

            var stale = candidates.Where(d =>
            {
                if (d.Metadata == null) return true; // never indexed with metadata
                if (!d.Metadata.TryGetValue("last_indexed_at", out var val)) return true;
                if (!DateTime.TryParse(val?.ToString(), out var lastIndexed)) return true;
                return d.ModifiedAt!.Value > lastIndexed;
            }).Take(_batchSize).ToList();

            if (!stale.Any())
            {
                _logger.LogDebug("✅ No stale documents to re-index");
                return;
            }

            _logger.LogInformation(
                "🔄 Re-indexing {Count} stale documents", stale.Count);

            var domainDocs = stale.Select(MapToDomain).ToList();
            await searchService.BulkIndexDocumentsAsync(domainDocs, ct);

            // Stamp last_indexed_at on each document
            foreach (var entity in stale)
            {
                entity.Metadata ??= new Dictionary<string, object>();
                entity.Metadata["last_indexed_at"] = DateTime.UtcNow.ToString("o");
            }

            // Update in DB — need a tracked context for this
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

    // ── Job 2: Index documents that are missing from OpenSearch ──────────────

    private async Task ReindexMissingDocumentsAsync(CancellationToken ct)
    {
        using var scope   = _serviceProvider.CreateScope();
        var db            = scope.ServiceProvider.GetRequiredService<GedDbContext>();
        var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();

        try
        {
            // Pull DB documents that should be indexed
            var candidates = await db.Documents
                .AsNoTracking()
                .Where(d => d.Status == DocumentStatus.Indexed)
                .OrderByDescending(d => d.CreatedAt)
                .Take(_batchSize)
                .ToListAsync(ct);

            if (!candidates.Any()) return;

            // Check which ones are actually present in OpenSearch by attempting a
            // multi-get. Documents missing from OpenSearch get re-indexed.
            var missing = await FindMissingFromOpenSearchAsync(
                candidates.Select(d => d.Id).ToList(),
                scope.ServiceProvider,
                ct);

            if (!missing.Any())
            {
                _logger.LogDebug("✅ No missing documents in OpenSearch");
                return;
            }

            _logger.LogWarning(
                "⚠️  {Count} documents missing from OpenSearch — re-indexing",
                missing.Count);

            var toIndex = candidates
                .Where(d => missing.Contains(d.Id))
                .Select(MapToDomain)
                .ToList();

            await searchService.BulkIndexDocumentsAsync(toIndex, ct);
            _logger.LogInformation(
                "✅ Re-indexed {Count} previously missing documents", toIndex.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during missing document check");
        }
    }

    // ── Job 3: Retry documents that previously failed indexing ───────────────

    private async Task RetryFailedDocumentsAsync(CancellationToken ct)
    {
        using var scope   = _serviceProvider.CreateScope();
        var db            = scope.ServiceProvider.GetRequiredService<GedDbContext>();
        var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();
        var writeDb       = scope.ServiceProvider.GetRequiredService<GedDbContext>();

        try
        {
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

            _logger.LogInformation(
                "🔄 Retrying indexing for {Count} failed documents", failed.Count);

            int successCount = 0;

            foreach (var entity in failed)
            {
                try
                {
                    var domain = MapToDomain(entity);
                    var ok     = await searchService.IndexDocumentAsync(domain, ct);

                    if (ok)
                    {
                        // Update status back to Indexed in DB
                        using var retryScope = _serviceProvider.CreateScope();
                        var retryDb = retryScope.ServiceProvider
                            .GetRequiredService<GedDbContext>();

                        var tracked = await retryDb.Documents
                            .FindAsync(new object[] { entity.Id }, ct);

                        if (tracked != null)
                        {
                            tracked.Status     = DocumentStatus.Indexed;
                            tracked.ModifiedAt = DateTime.UtcNow;
                            await retryDb.SaveChangesAsync(ct);
                        }

                        successCount++;
                        _logger.LogInformation(
                            "✅ Retry successful for document {DocumentId}", entity.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Retry failed for document {DocumentId}", entity.Id);
                }

                // Small delay between retries to avoid hammering OpenSearch
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<HashSet<Guid>> FindMissingFromOpenSearchAsync(
        List<Guid> ids,
        IServiceProvider scopedProvider,
        CancellationToken ct)
    {
        try
        {
            var client = scopedProvider
                .GetRequiredService<OpenSearch.Client.IOpenSearchClient>();

            var mgetResponse = await client.MultiGetAsync(mg => mg
                .Index("ged-documents")
                .GetMany<DocumentIndexModel>(ids.Select(id => id.ToString())),
                ct);

            var found = mgetResponse.Hits
                .Where(h => h.Found)
                .Select(h => Guid.Parse(h.Id))
                .ToHashSet();

            return ids.Where(id => !found.Contains(id)).ToHashSet();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not check OpenSearch for missing documents — skipping");
            return new HashSet<Guid>();
        }
    }

    /// <summary>
    /// Lightweight domain model mapping (same fields needed for indexing).
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