using GED.Core.Interfaces;
using GED.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace GED.Infrastructure.Services;

/// <summary>
/// Outbox pattern relay that ensures reliable message delivery to RabbitMQ.
/// 
/// <para>
/// This is the second half of the Outbox Pattern. It polls the outbox_messages
/// table every 1 second and publishes any unprocessed messages to RabbitMQ.
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
///     <term>Resource Saver Mode</term>
///     <description>
///       When ResourceSaver:Enabled = true, enforces mutual exclusion between
///       OCR jobs and indexing jobs to prevent Ollama resource exhaustion.
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
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Polling interval between relay cycles (configurable via OUTBOX_POLL_INTERVAL_SECONDS).
    /// </summary>
    private readonly TimeSpan _pollInterval;

    /// <summary>
    /// Maximum retry attempts before giving up on a message.
    /// </summary>
    private const int MaxRetries = 5;

    /// <summary>
    /// Maximum messages to process per poll cycle (configurable via OUTBOX_BATCH_SIZE).
    /// </summary>
    private readonly int _batchSize;

    /// <summary>
    /// Whether ResourceSaver mode is enabled.
    /// </summary>
    private readonly bool _resourceSaverEnabled;

    /// <summary>
    /// Initializes a new instance of <see cref="OutboxRelayService"/>.
    /// </summary>
    /// <param name="sp">Service provider for creating scoped dependencies.</param>
    /// <param name="logger">Logger for relay events.</param>
    /// <param name="configuration">Application configuration.</param>
    public OutboxRelayService(IServiceProvider sp, ILogger<OutboxRelayService> logger, IConfiguration configuration)
    {
        _sp = sp;
        _logger = logger;
        _configuration = configuration;

        var pollIntervalSeconds = configuration.GetValue<int>("Outbox:PollIntervalSeconds", 1);
        _pollInterval = TimeSpan.FromSeconds(Math.Max(1, pollIntervalSeconds));

        _batchSize = configuration.GetValue<int>("Outbox:BatchSize", 20);

        _resourceSaverEnabled = configuration.GetValue<bool>("ResourceSaver:Enabled", false);

        _logger.LogInformation(
            "📬 Outbox relay configured: PollInterval={Interval}s, BatchSize={Batch}, ResourceSaver={ResourceSaver}",
            _pollInterval.TotalSeconds, _batchSize, _resourceSaverEnabled);

        if (_resourceSaverEnabled)
        {
            _logger.LogInformation("ResourceSaver mode enabled - mutual exclusion between OCR and indexing jobs");
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("📬 Outbox relay started (polling every {Interval}s, ResourceSaver={Enabled})",
            _pollInterval.TotalSeconds, _resourceSaverEnabled);

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

        // Fetch unprocessed messages with row-level locking to prevent duplicate delivery
        // across concurrent instances. Uses SQL Server's WITH (UPDLOCK, ROWLOCK) hint.
        // Order by priority (lower = higher priority) then by created_at
        var pending = await db.OutboxMessages
            .FromSqlRaw(
                @"SELECT * FROM outbox_messages WITH (UPDLOCK, ROWLOCK)
                  WHERE processed_at IS NULL AND retry_count < {0}
                  ORDER BY CASE WHEN priority IS NULL THEN 1 ELSE 0 END, priority, created_at
                  OFFSET 0 ROWS FETCH NEXT {1} ROWS ONLY",
                MaxRetries, _batchSize)
            .ToListAsync(ct);

        if (!pending.Any()) return;

        // Check for in-flight jobs if ResourceSaver is enabled
        // Include stale job detection - jobs in-flight for > 5 minutes are considered stuck
        var staleThreshold = DateTime.UtcNow.AddMinutes(-5);
        var ocrInFlight = 0;
        var indexingInFlight = 0;
        
        if (_resourceSaverEnabled)
        {
            ocrInFlight = await db.OutboxMessages
                .Where(m => m.Type == "OcrJob" && m.ProcessedAt != null && m.AcknowledgedAt == null)
                .Where(m => m.ProcessedAt > staleThreshold) // Only count recent in-flight jobs
                .CountAsync(ct);

            indexingInFlight = await db.OutboxMessages
                .Where(m => m.Type == "IndexingJob" && m.ProcessedAt != null && m.AcknowledgedAt == null)
                .Where(m => m.ProcessedAt > staleThreshold) // Only count recent in-flight jobs
                .CountAsync(ct);

            if (ocrInFlight > 0)
            {
                _logger.LogInformation("⏸️ Holding back OCR jobs - {Count} indexing jobs in-flight", indexingInFlight);
            }
            if (indexingInFlight > 0)
            {
                _logger.LogInformation("⏸️ Holding back indexing jobs - {Count} OCR jobs in-flight", ocrInFlight);
            }
        }

        _logger.LogInformation("📬 Outbox relay: processing {Count} messages (OCR:{OcrInFlight}, Indexing:{IdxInFlight})", 
            pending.Count, ocrInFlight, indexingInFlight);

        foreach (var msg in pending)
        {
            // ResourceSaver mutual exclusion check
            // Note: OcrJob should NEVER be skipped - it must complete before IndexingJob can run
            // IndexingJob depends on OCR-enriched text, so OcrJob has priority
            if (_resourceSaverEnabled && msg.Type == "IndexingJob" && ocrInFlight > 0)
            {
                _logger.LogInformation("⏸️ Skipping IndexingJob {Id} - {Count} OcrJob(s) in-flight (must wait for OCR to complete)", msg.Id, ocrInFlight);
                continue;
            }

            try
            {
                _logger.LogDebug("📤 About to publish outbox message {Id}, type={Type}", msg.Id, msg.Type);
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
                        "❌ Outbox message {Id} ({Type}) failed after {MaxRetries} attempts — giving up",
                        msg.Id, msg.Type, MaxRetries);
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
    private async Task PublishMessageAsync(
        RabbitMqService rabbitMq, OutboxMessage msg, CancellationToken ct)
    {
        // Route to appropriate queue based on message type
        var queueName = msg.Type switch
        {
            "OcrJob" => "ocr-queue",
            "IndexingJob" => "indexing-queue",
            _        => throw new InvalidOperationException($"Unknown outbox message type: {msg.Type}")
        };

        if (msg.Type == "OcrJob")
        {
            var ocrJob = JsonSerializer.Deserialize<OcrJobMessage>(msg.Payload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Failed to deserialize OcrJob payload");
            await rabbitMq.PublishAsync(queueName, ocrJob, ct);
        }
        else if (msg.Type == "IndexingJob")
        {
            var indexingJob = JsonSerializer.Deserialize<IndexingJobMessage>(msg.Payload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Failed to deserialize IndexingJob payload");
            await rabbitMq.PublishAsync(queueName, indexingJob, ct);
        }
    }
}
