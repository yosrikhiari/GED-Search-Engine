using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

public interface IWebhookService
{
    Task<List<WebhookConfig>> GetAllWebhooksAsync();
    Task<WebhookConfig?> GetWebhookByIdAsync(Guid id);
    Task<WebhookConfig> CreateWebhookAsync(WebhookConfig config);
    Task<WebhookConfig?> UpdateWebhookAsync(Guid id, WebhookConfig config);
    Task<bool> DeleteWebhookAsync(Guid id);
    Task TriggerEventAsync(string eventName, object data, string? correlationId = null);
}

public class WebhookService : IWebhookService
{
    private readonly ILogger<WebhookService> _logger;
    private readonly IDistributedCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _webhooksFilePath;

    private readonly List<WebhookConfig> _webhooks = new();
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WebhookService(
        ILogger<WebhookService> logger,
        IDistributedCache cache,
        IHttpClientFactory httpClientFactory,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _logger = logger;
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _webhooksFilePath = configuration["Webhooks:ConfigPath"] ?? "/var/lib/ged/webhooks.json";
        LoadWebhooks();
    }

    public Task<List<WebhookConfig>> GetAllWebhooksAsync()
    {
        lock (_lock) return Task.FromResult(_webhooks.ToList());
    }

    public Task<WebhookConfig?> GetWebhookByIdAsync(Guid id)
    {
        lock (_lock) return Task.FromResult(_webhooks.FirstOrDefault(w => w.Id == id));
    }

    public Task<WebhookConfig> CreateWebhookAsync(WebhookConfig config)
    {
        config.Id = Guid.NewGuid();
        config.CreatedAt = DateTime.UtcNow;
        config.IsActive = true;

        lock (_lock) _webhooks.Add(config);
        SaveWebhooks();

        _logger.LogInformation(
            "✅ Webhook created: {Name} -> {Url} (events: [{Events}])",
            config.Name, config.Url, string.Join(", ", config.Events));

        return Task.FromResult(config);
    }

    public Task<WebhookConfig?> UpdateWebhookAsync(Guid id, WebhookConfig config)
    {
        lock (_lock)
        {
            var existing = _webhooks.FirstOrDefault(w => w.Id == id);
            if (existing == null) return Task.FromResult<WebhookConfig?>(null);

            existing.Name = config.Name;
            existing.Url = config.Url;
            existing.Secret = config.Secret;
            existing.Events = config.Events;
            existing.IsActive = config.IsActive;
            existing.TimeoutSeconds = config.TimeoutSeconds;
            existing.MaxRetries = config.MaxRetries;
        }
        SaveWebhooks();
        return Task.FromResult(_webhooks.FirstOrDefault(w => w.Id == id));
    }

    public Task<bool> DeleteWebhookAsync(Guid id)
    {
        lock (_lock)
        {
            var removed = _webhooks.RemoveAll(w => w.Id == id) > 0;
            if (removed) SaveWebhooks();
            return Task.FromResult(removed);
        }
    }

    public async Task TriggerEventAsync(string eventName, object data, string? correlationId = null)
    {
        List<WebhookConfig> targets;
        lock (_lock)
        {
            targets = _webhooks
                .Where(w => w.IsActive && w.Events.Contains(eventName))
                .ToList();
        }

        if (targets.Count == 0) return;

        var payload = new WebhookPayload
        {
            Event = eventName,
            Timestamp = DateTime.UtcNow,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("N")[..12],
            Metadata = data
        };

        // Extract document data if present
        if (data is Document doc)
        {
            payload.Document = new WebhookDocumentData
            {
                Id = doc.Id,
                Title = doc.Title,
                Category = doc.Category,
                FileName = doc.FileName,
                ContentType = doc.ContentType,
                FileSize = doc.FileSize,
                CreatedBy = doc.CreatedBy,
                CreatedAt = doc.CreatedAt
            };
        }

        _logger.LogInformation(
            "🔔 Triggering webhook event '{Event}' to {Count} webhook(s) (correlationId={CorrId})",
            eventName, targets.Count, payload.CorrelationId);

        var tasks = targets.Select(w => DeliverWebhookWithRetryAsync(w, payload));
        await Task.WhenAll(tasks);
    }

    private async Task DeliverWebhookWithRetryAsync(WebhookConfig webhook, WebhookPayload payload)
    {
        var client = _httpClientFactory.CreateClient("webhook");
        client.Timeout = TimeSpan.FromSeconds(webhook.TimeoutSeconds);

        for (int attempt = 1; attempt <= webhook.MaxRetries; attempt++)
        {
            try
            {
                var json = JsonSerializer.Serialize(payload, _jsonOptions);
                using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                // Add HMAC signature header if secret is configured
                if (!string.IsNullOrWhiteSpace(webhook.Secret))
                {
                    var signature = ComputeHmacSha256(json, webhook.Secret);
                    request.Headers.TryAddWithoutValidation("X-Webhook-Signature", $"sha256={signature}");
                }

                request.Headers.TryAddWithoutValidation("X-Webhook-Event", payload.Event);
                request.Headers.TryAddWithoutValidation("X-Webhook-Correlation-Id", payload.CorrelationId ?? "");

                var response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    lock (_lock)
                    {
                        webhook.SuccessCount++;
                        webhook.LastTriggeredAt = DateTime.UtcNow;
                    }
                    SaveWebhooks();

                    _logger.LogInformation(
                        "✅ Webhook delivered: {Name} -> {Url} (event={Event}, attempt={Attempt})",
                        webhook.Name, webhook.Url, payload.Event, attempt);
                    return;
                }

                _logger.LogWarning(
                    "⚠️  Webhook delivery failed: {Name} -> {Url} (status={Status}, attempt={Attempt}/{Max})",
                    webhook.Name, webhook.Url, (int)response.StatusCode, attempt, webhook.MaxRetries);
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning(
                    "⏱️  Webhook timeout: {Name} -> {Url} (attempt {Attempt}/{Max})",
                    webhook.Name, webhook.Url, attempt, webhook.MaxRetries);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "❌ Webhook delivery error: {Name} -> {Url} (attempt {Attempt}/{Max})",
                    webhook.Name, webhook.Url, attempt, webhook.MaxRetries);
            }

            if (attempt < webhook.MaxRetries)
            {
                // Exponential backoff: 1s, 2s, 4s...
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)));
            }
        }

        lock (_lock)
        {
            webhook.FailureCount++;
        }
        SaveWebhooks();
    }

    private static string ComputeHmacSha256(string data, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void LoadWebhooks()
    {
        try
        {
            if (!File.Exists(_webhooksFilePath)) return;
            var json = File.ReadAllText(_webhooksFilePath);
            var loaded = JsonSerializer.Deserialize<List<WebhookConfig>>(json, _jsonOptions);
            if (loaded != null)
            {
                lock (_lock) _webhooks.AddRange(loaded);
            }
            _logger.LogInformation("Loaded {Count} webhooks from config", _webhooks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load webhooks from {Path}", _webhooksFilePath);
        }
    }

    private void SaveWebhooks()
    {
        try
        {
            var dir = Path.GetDirectoryName(_webhooksFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_webhooks, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            File.WriteAllText(_webhooksFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save webhooks to {Path}", _webhooksFilePath);
        }
    }
}
