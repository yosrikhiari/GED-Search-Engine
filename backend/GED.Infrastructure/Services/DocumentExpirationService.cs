using GED.Core.Interfaces;
using GED.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

public class DocumentExpirationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentExpirationService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(30);

    public DocumentExpirationService(
        IServiceProvider serviceProvider,
        ILogger<DocumentExpirationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Document expiration service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckExpiredDocumentsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for expired documents");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task CheckExpiredDocumentsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GedDbContext>();
        var webhookService = scope.ServiceProvider.GetService<IWebhookService>();
        var searchService = scope.ServiceProvider.GetService<ISearchService>();
        var storageService = scope.ServiceProvider.GetService<IStorageService>();

        var now = DateTime.UtcNow;

        // Find documents past their expiration date
        var expiredDocs = await db.Documents
            .Where(d => d.Metadata != null)
            .ToListAsync(cancellationToken);

        var toExpire = expiredDocs
            .Where(d => TryGetExpirationDateFromDict(d.Metadata, out var expDate) && expDate.HasValue && expDate.Value <= now)
            .ToList();

        if (toExpire.Count == 0)
        {
            _logger.LogDebug("No expired documents found");
            return;
        }

        _logger.LogInformation("Found {Count} expired document(s) to process", toExpire.Count);

        foreach (var doc in toExpire)
        {
            try
            {
                // Trigger webhook before deletion
                await webhookService?.TriggerEventAsync("document.expired", new
                {
                    documentId = doc.Id,
                    title = doc.Title,
                    category = doc.Category,
                    expiredAt = now
                }, null)!;

                // Mark as expired (soft delete)
                doc.Status = GED.Core.Models.DocumentStatus.Expired;
                doc.ModifiedAt = now;

                // Remove from search index
                if (searchService != null)
                {
                    await searchService.DeleteDocumentIndexAsync(doc.Id, cancellationToken);
                }

                _logger.LogInformation(
                    "Document {Id} ({Title}) marked as expired and removed from search index",
                    doc.Id, doc.Title);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to expire document {Id}", doc.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Document expiration check completed: {Count} documents processed", toExpire.Count);
    }

    private static bool TryGetExpirationDate(string? metadata, out DateTime? expirationDate) => TryGetExpirationDateFromJson(metadata, out expirationDate);

    private static bool TryGetExpirationDateFromJson(string? metadata, out DateTime? expirationDate)
    {
        expirationDate = null;
        if (string.IsNullOrWhiteSpace(metadata)) return false;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(metadata);
            if (doc.RootElement.TryGetProperty("expirationDate", out var elem) ||
                doc.RootElement.TryGetProperty("expiration_date", out elem))
            {
                if (elem.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    if (DateTime.TryParse(elem.GetString(), out var date))
                    {
                        expirationDate = date;
                        return true;
                    }
                }
            }
        }
        catch { /* ignore malformed JSON */ }

        return false;
    }

    private static bool TryGetExpirationDateFromDict(Dictionary<string, object>? metadata, out DateTime? expirationDate)
    {
        expirationDate = null;
        if (metadata == null) return false;

        if (metadata.TryGetValue("expirationDate", out var expVal) ||
            metadata.TryGetValue("expiration_date", out expVal))
        {
            if (expVal is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                if (DateTime.TryParse(je.GetString(), out var date))
                {
                    expirationDate = date;
                    return true;
                }
            }
            else if (expVal is string s && DateTime.TryParse(s, out var date2))
            {
                expirationDate = date2;
                return true;
            }
        }

        return false;
    }
}
