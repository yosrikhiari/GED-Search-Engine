using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

public class AutoReindexService : BackgroundService
{
    private readonly IServiceProvider         _serviceProvider;
    private readonly ILogger<AutoReindexService> _logger;

    private readonly TimeSpan _staleInterval;
    private readonly TimeSpan _missingInterval;
    private readonly TimeSpan _failedInterval;
    private readonly int      _batchSize;
    private readonly bool     _enabled;

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

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }

        _logger.LogInformation("🛑 AutoReindexService stopped");
    }

    private async Task ReindexStaleDocumentsAsync(CancellationToken ct)
    {
        using var scope        = _serviceProvider.CreateScope();
        var db                 = scope.ServiceProvider.GetRequiredService<GedDbContext>();
        var searchService      = scope.ServiceProvider.GetRequiredService<ISearchService>();

        try
        {
            var candidates = await db.Documents
                .AsNoTracking()
                .Where(d => d.Status == DocumentStatus.Indexed &&
                            d.ModifiedAt.HasValue)
                .OrderByDescending(d => d.ModifiedAt)
                .Take(_batchSize * 2)
                .ToListAsync(ct);

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

            var domainDocs = stale.Select(MapToDomain).ToList();
            await searchService.BulkIndexDocumentsAsync(domainDocs, ct);

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

    private async Task ReindexMissingDocumentsAsync(CancellationToken ct)
    {
        using var scope   = _serviceProvider.CreateScope();
        var db            = scope.ServiceProvider.GetRequiredService<GedDbContext>();
        var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();

        try
        {
            var candidates = await db.Documents
                .AsNoTracking()
                .Where(d => d.Status == DocumentStatus.Indexed)
                .OrderByDescending(d => d.CreatedAt)
                .Take(_batchSize)
                .ToListAsync(ct);

            if (!candidates.Any()) return;

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

    private async Task RetryFailedDocumentsAsync(CancellationToken ct)
    {
        using var scope   = _serviceProvider.CreateScope();
        var db            = scope.ServiceProvider.GetRequiredService<GedDbContext>();
        var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();

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
            _logger.LogWarning(ex, "Could not check OpenSearch for missing documents — skipping");
            return new HashSet<Guid>();
        }
    }

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