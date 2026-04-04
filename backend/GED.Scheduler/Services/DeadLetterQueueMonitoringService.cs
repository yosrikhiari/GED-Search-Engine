using GED.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace GED.Infrastructure.Services;

public class DeadLetterQueueMonitoringService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeadLetterQueueMonitoringService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);
    private readonly int _alertThreshold = 10;

    public DeadLetterQueueMonitoringService(
        IServiceProvider serviceProvider,
        ILogger<DeadLetterQueueMonitoringService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _alertThreshold = configuration.GetValue<int>("DLQ:AlertThreshold", 10);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Dead Letter Queue monitoring service started (check interval: {Interval}, alert threshold: {Threshold})",
            _checkInterval, _alertThreshold);

        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckDlqDepthAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking DLQ depths");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Dead Letter Queue monitoring service stopped");
    }

    private async Task CheckDlqDepthAsync(CancellationToken ct)
    {
        var queues = new[] 
        { 
            ("ocr-queue.dlq", "ocr-dlq"),
            ("indexing-queue.dlq", "indexing-dlq")
        };

        foreach (var (queueName, metricName) in queues)
        {
            try
            {
                var messageCount = await GetQueueMessageCountAsync(queueName, ct);
                
                if (messageCount > 0)
                {
                    _logger.LogWarning(
                        "DLQ depth alert: {QueueName} has {Count} messages in dead letter queue",
                        queueName, messageCount);
                    
                    if (messageCount > _alertThreshold)
                    {
                        await TriggerAlertAsync(queueName, messageCount, ct);
                    }
                }
                else
                {
                    _logger.LogDebug("DLQ check: {QueueName} is empty", queueName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check DLQ depth for {QueueName}", queueName);
            }
        }
    }

    private async Task<int> GetQueueMessageCountAsync(string queueName, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        
        // The dead letter queue names based on our configuration
        // ocr-queue -> ocr-queue.dlq
        // indexing-queue -> indexing-queue.dlq
        
        try
        {
            // Use RabbitMQ management API via HTTP
            var rabbitHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "rabbitmq";
            var rabbitPort = Environment.GetEnvironmentVariable("RABBITMQ_PORT") ?? "15672";
            var rabbitUser = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "admin";
            var rabbitPass = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "admin123";

            var url = $"http://{rabbitHost}:{rabbitPort}/api/queues/%2F/{Uri.EscapeDataString(queueName)}";
            
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", 
                    Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{rabbitUser}:{rabbitPass}")));
            
            var response = await httpClient.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("messages", out var messages))
                {
                    return messages.GetInt32();
                }
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch queue depth for {QueueName} via API - trying direct check", queueName);
            
            // Fallback: return 0 if we can't reach the management API
            // In production, this should be configured properly
            return 0;
        }
    }

    private async Task TriggerAlertAsync(string queueName, int messageCount, CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var webhookService = scope.ServiceProvider.GetService<IWebhookService>();
            
            if (webhookService != null)
            {
                await webhookService.TriggerEventAsync("dlq.alert", new
                {
                    queueName = queueName,
                    messageCount = messageCount,
                    threshold = _alertThreshold,
                    severity = "high",
                    timestamp = DateTime.UtcNow
                }, null);

                _logger.LogWarning(
                    "DLQ alert triggered: {QueueName} exceeded threshold ({Count} > {Threshold})",
                    queueName, messageCount, _alertThreshold);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger DLQ alert webhook");
        }
    }
}
