using GED.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace GED.Infrastructure.Services;

/// <summary>
/// Outbox pattern relay that ensures reliable message delivery to RabbitMQ.
/// 
/// <para>
/// This is the second half of the Outbox Pattern. It polls the outbox_messages
/// table every 5 seconds and publishes any unprocessed messages to RabbitMQ.
/// </para>
/// 
/// <para>
/// Key properties:
/// <list type="bullet">
///   <item>
///     <term>At-least-once delivery</term>
///     <description>
///       Messages are published until acknowledged. Idempotent consumers should handle duplicates.
///     </description>
///   </item>
///   <item>
///     <term>Retry limit</term>
///     <description>
///       Max 5 retry attempts per message to prevent infinite retry loops.
///     </description>
///   </item>
///   <item>
///     <term>Backpressure</term>
///     <description>
///       Processes up to 20 messages per poll cycle.
///     </description>
///   </item>
///   <item>
///     <term>Thread safety</term>
///     <description>
///       Uses scoped DI to get fresh DbContext per cycle.
///     </description>
///   </item>
/// </list>
/// </para>
/// 
/// <para>
/// This approach ensures OCR jobs are delivered to RabbitMQ even if the broker
/// was temporarily unavailable during document upload. The upload transaction
/// commits both the document and the outbox message atomically.
/// </para>
/// </summary>
public class OutboxRelayService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<OutboxRelayService> _logger;

    /// <summary>
    /// Polling interval between relay cycles.
    /// </summary>
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum retry attempts before giving up on a message.
    /// </summary>
    private const int MaxRetries = 5;

    /// <summary>
    /// Maximum messages to process per poll cycle.
    /// </summary>
    private const int BatchSize = 20;

    /// <summary>
    /// Initializes a new instance of <see cref="OutboxRelayService"/>.
    /// </summary>
    /// <param name="sp">Service provider for creating scoped dependencies.</param>
    /// <param name="logger">Logger for relay events.</param>
    public OutboxRelayService(IServiceProvider sp, ILogger<OutboxRelayService> logger)
    {
        _sp     = sp;
        _logger = logger;
    }

    /// <inheritdoc />
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

    /// <summary>
    /// Processes pending outbox messages by publishing them to RabbitMQ.
    /// </summary>
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

        // Persist all state changes (ProcessedAt + RetryCount + Error) in one round-trip
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Publishes an outbox message to the appropriate RabbitMQ queue.
    /// </summary>
    /// <param name="rabbitMq">RabbitMQ service for publishing.</param>
    /// <param name="msg">Outbox message to publish.</param>
    /// <param name="ct">Cancellation token.</param>
    private static async Task PublishMessageAsync(
        RabbitMqService rabbitMq, OutboxMessage msg, CancellationToken ct)
    {
        // Route to appropriate queue based on message type
        var queueName = msg.Type switch
        {
            "OcrJob" => "ocr-queue",
            _        => throw new InvalidOperationException($"Unknown outbox message type: {msg.Type}")
        };

        // Deserialize payload to concrete type before publishing
        // This prevents double-serialization when PublishAsync<T> serializes the object
        var ocrJob = JsonSerializer.Deserialize<OcrJobMessage>(msg.Payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialize OcrJob payload");

        await rabbitMq.PublishAsync(queueName, ocrJob, ct);
    }
}
