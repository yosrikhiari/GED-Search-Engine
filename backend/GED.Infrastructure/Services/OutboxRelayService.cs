using GED.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace GED.Infrastructure.Services;

/// <summary>
/// Outbox Relay: polls the outbox_messages table every 5 seconds and publishes
/// any unprocessed messages to RabbitMQ.
///
/// This is the second half of the Outbox Pattern. It ensures OCR jobs are
/// delivered to RabbitMQ even if the broker was temporarily unavailable during upload.
///
/// Key properties:
/// - At-least-once delivery (idempotent consumers should handle duplicates)
/// - Max 5 retry attempts per message (prevents infinite retry loops)
/// - Processes up to 20 messages per poll cycle (backpressure)
/// - Uses scoped DI to get fresh DbContext per cycle (thread-safe)
/// </summary>
public class OutboxRelayService : BackgroundService
{
    private readonly IServiceProvider            _sp;
    private readonly ILogger<OutboxRelayService> _logger;
    private readonly TimeSpan                    _pollInterval = TimeSpan.FromSeconds(5);
    private const    int                         MaxRetries    = 5;
    private const    int                         BatchSize     = 20;

    public OutboxRelayService(IServiceProvider sp, ILogger<OutboxRelayService> logger)
    {
        _sp     = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("📬 Outbox relay started (polling every {Interval}s)", _pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Log but keep running — transient errors shouldn't kill the relay
                _logger.LogError(ex, "Outbox relay cycle failed — will retry in {Interval}s", _pollInterval.TotalSeconds);
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }

        _logger.LogInformation("📪 Outbox relay stopped");
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken ct)
    {
        using var scope  = _sp.CreateScope();
        var db           = scope.ServiceProvider.GetRequiredService<GedDbContext>();
        var rabbitMq     = scope.ServiceProvider.GetRequiredService<RabbitMqService>();

        // Fetch unprocessed messages (not yet published, under retry limit)
        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.RetryCount < MaxRetries)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (!pending.Any()) return;

        _logger.LogInformation("📬 Outbox relay: processing {Count} messages", pending.Count);

        foreach (var msg in pending)
        {
            try
            {
                await PublishMessageAsync(rabbitMq, msg, ct);
                msg.ProcessedAt = DateTime.UtcNow;
                _logger.LogInformation("✅ Outbox message {Id} ({Type}) published", msg.Id, msg.Type);
            }
            catch (Exception ex)
            {
                msg.RetryCount++;
                msg.Error = ex.Message;

                if (msg.RetryCount >= MaxRetries)
                {
                    _logger.LogError(
                        ex,
                        "❌ Outbox message {Id} ({Type}) failed after {MaxRetries} attempts — giving up. Payload: {Payload}",
                        msg.Id, msg.Type, MaxRetries, msg.Payload);
                }
                else
                {
                    _logger.LogWarning(
                        ex,
                        "⚠️ Outbox message {Id} ({Type}) failed (attempt {Attempt}/{Max}) — will retry",
                        msg.Id, msg.Type, msg.RetryCount, MaxRetries);
                }
            }
        }

        // Persist all state changes (ProcessedAt + RetryCount) in one round-trip
        await db.SaveChangesAsync(ct);
    }

    private static async Task PublishMessageAsync(
    RabbitMqService rabbitMq, OutboxMessage msg, CancellationToken ct)
{
    var queueName = msg.Type switch
    {
        "OcrJob" => "ocr-queue",
        _        => throw new InvalidOperationException($"Unknown outbox message type: {msg.Type}")
    };

    // ✅ FIX: msg.Payload is already a JSON string.
    // Passing it directly to PublishAsync<string> causes double-serialization
    // (the string gets JSON-encoded again, wrapping it in quotes).
    // Deserialize to the concrete type first so PublishAsync<T> serializes an object.
    var ocrJob = JsonSerializer.Deserialize<OcrJobMessage>(msg.Payload,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("Failed to deserialize OcrJob payload");

    await rabbitMq.PublishAsync(queueName, ocrJob, ct);
}
}