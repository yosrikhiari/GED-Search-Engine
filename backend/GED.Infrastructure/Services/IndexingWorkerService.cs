using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace GED.Infrastructure.Services;

/// <summary>
/// Background worker that consumes document indexing jobs from RabbitMQ and processes them asynchronously.
///
/// <para>
/// This worker implements a producer-consumer pattern to decouple document indexing from the API.
/// Documents that need indexing are published to a queue, and this worker consumes them in batches.
///
/// <list type="bullet">
///   <item>
///     <term>Producer</term>
///     <description>
///       <see cref="DocumentService"/> or <see cref="OpenSearchService"/> publishes indexing jobs
///       when documents need to be indexed (created, updated, OCR completed).
///     </description>
///   </item>
///   <item>
///     <term>Consumer</term>
///     <description>
///       This worker consumes jobs from the queue and processes them with proper backpressure.
///       Uses configurable concurrency to limit parallel embedding generation.
///     </description>
///   </item>
/// </list>
/// </para>
///
/// <para>
/// Benefits over direct in-process bulk indexing:
/// <list type="number">
///   <item>
///     <term>Horizontal scaling</term>
///     <description>Run multiple worker instances for parallel processing.</description>
///   </item>
///   <item>
///     <term>Survivability</term>
///     <description>Indexing continues even if API restarts.</description>
///   </item>
///   <item>
///     <term>Backpressure</term>
///     <description>Queue absorbs spikes; workers process at sustainable rate.</description>
///   </item>
///   <item>
///     <term>Resource isolation</term>
///     <description>Heavy embedding generation doesn't impact API response times.</description>
///   </item>
/// </list>
/// </para>
/// </summary>
public class IndexingWorkerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IndexingWorkerService> _logger;

    private readonly string _rabbitHost;
    private readonly string _rabbitUser;
    private readonly string _rabbitPass;
    private readonly int _concurrencyLimit;
    private readonly int _batchSize;

    private const string QueueName = "indexing-queue";
    private const int MaxConnectRetries = 5;
    private const int RetryDelayMs = 5000;

    private SemaphoreSlim? _concurrencySemaphore;

    public IndexingWorkerService(
        IServiceProvider serviceProvider,
        ILogger<IndexingWorkerService> logger,
        string rabbitHost = "localhost",
        string rabbitUser = "admin",
        string rabbitPass = "admin123",
        int concurrencyLimit = 4,
        int batchSize = 10)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _rabbitHost = rabbitHost;
        _rabbitUser = rabbitUser;
        _rabbitPass = rabbitPass;
        _concurrencyLimit = concurrencyLimit;
        _batchSize = batchSize;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _concurrencySemaphore = new SemaphoreSlim(_concurrencyLimit, _concurrencyLimit);
        _logger.LogInformation(
            "📥 Indexing Worker starting… (concurrency={Concurrency}, batch={Batch})",
            _concurrencyLimit, _batchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            IConnection? connection = null;

            for (int attempt = 1; attempt <= MaxConnectRetries; attempt++)
            {
                try
                {
                    var factory = new ConnectionFactory
                    {
                        HostName = _rabbitHost,
                        UserName = _rabbitUser,
                        Password = _rabbitPass,
                        AutomaticRecoveryEnabled = false,
                        RequestedHeartbeat = TimeSpan.FromSeconds(60),
                        RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
                    };

                    connection = await factory.CreateConnectionAsync(stoppingToken);
                    _logger.LogInformation("✅ Indexing Worker connected to RabbitMQ at {Host}", _rabbitHost);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("⚠️ RabbitMQ connect attempt {Attempt}/{Max} failed: {Error}",
                        attempt, MaxConnectRetries, ex.Message);

                    if (attempt == MaxConnectRetries)
                    {
                        _logger.LogError("❌ Could not connect to RabbitMQ. Waiting 30s…");
                        await Task.Delay(30_000, stoppingToken);
                        break;
                    }

                    await Task.Delay(RetryDelayMs * attempt, stoppingToken);
                }
            }

            if (connection == null) continue;

            try
            {
                await using (connection)
                {
                    var connectionClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    connection.ConnectionShutdownAsync += (_, args) =>
                    {
                        _logger.LogWarning("🔌 RabbitMQ connection shutdown: {Reason}", args.ReplyText);
                        connectionClosed.TrySetResult();
                        return Task.CompletedTask;
                    };

                    var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                    await using (channel)
                    {
                        // Declare the indexing queue
                        await channel.QueueDeclareAsync(
                            queue: QueueName,
                            durable: true,
                            exclusive: false,
                            autoDelete: false,
                            arguments: new Dictionary<string, object?>
                            {
                                ["x-max-length"] = 10000,
                                ["x-message-ttl"] = 3600000
                            },
                            cancellationToken: stoppingToken);

                        // Allow multiple messages prefetch for batching
                        await channel.BasicQosAsync(
                            prefetchSize: 0,
                            prefetchCount: (ushort)_batchSize,
                            global: false,
                            cancellationToken: stoppingToken);

                        _logger.LogInformation("📥 Indexing Worker listening on queue '{Queue}'", QueueName);

                        var consumer = new AsyncEventingBasicConsumer(channel);
                        consumer.ReceivedAsync += async (_, ea) =>
                        {
                            var deliveryTag = ea.DeliveryTag;

                            try
                            {
                                var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                                var message = JsonSerializer.Deserialize<IndexingJobMessage>(json,
                                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                                if (message == null)
                                {
                                    _logger.LogWarning("Received null indexing message — discarding");
                                    await SafeAckAsync(channel, deliveryTag);
                                    return;
                                }

                                _logger.LogInformation(
                                    "📄 Indexing job received: jobId={JobId}, documentId={DocId}, action={Action}",
                                    message.JobId, message.DocumentId, message.Action);

                                await ProcessIndexingJobAsync(message, stoppingToken);
                                await SafeAckAsync(channel, deliveryTag);

                                _logger.LogInformation("✅ Indexing job {JobId} completed and acked", message.JobId);
                            }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                            {
                                _logger.LogInformation("Indexing job cancelled during shutdown — nacking for requeue");
                                await SafeNackAsync(channel, deliveryTag, requeue: true);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "❌ Indexing job failed");
                                await SafeNackAsync(channel, deliveryTag, requeue: false);

                                try { await connection.CloseAsync(); } catch { }
                            }
                        };

                        await channel.BasicConsumeAsync(
                            queue: QueueName,
                            autoAck: false,
                            consumer: consumer,
                            cancellationToken: stoppingToken);

                        try
                        {
                            await Task.WhenAny(
                                Task.Delay(Timeout.Infinite, stoppingToken),
                                connectionClosed.Task);
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogInformation("🛑 Indexing Worker stopping gracefully");
                            return;
                        }

                        _logger.LogWarning("🔄 Connection lost — reconnecting in 5s…");
                        await Task.Delay(5_000, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Indexing Worker consumer loop crashed — reconnecting in 10s");
                await Task.Delay(10_000, stoppingToken);
            }
        }
    }

    private async Task ProcessIndexingJobAsync(IndexingJobMessage message, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();
        var db = scope.ServiceProvider.GetRequiredService<GedDbContext>();

        switch (message.Action)
        {
            case IndexingAction.Index:
            case IndexingAction.Reindex:
                await HandleIndexOrReindexAsync(message, searchService, db, ct);
                break;

            case IndexingAction.Delete:
                await HandleDeleteAsync(message, searchService, ct);
                break;

            case IndexingAction.UpdateAcl:
                await HandleUpdateAclAsync(message, searchService, db, ct);
                break;

            default:
                _logger.LogWarning("Unknown indexing action: {Action}", message.Action);
                break;
        }
    }

    private async Task HandleIndexOrReindexAsync(
        IndexingJobMessage message,
        ISearchService searchService,
        GedDbContext db,
        CancellationToken ct)
    {
        // Fetch document from database
        var entity = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == message.DocumentId, ct);

        if (entity == null)
        {
            _logger.LogWarning("Document {DocId} not found for indexing", message.DocumentId);
            return;
        }

        // Fetch ACLs
        var aclRows = await db.DocumentAcls
            .Where(a => a.DocumentId == message.DocumentId &&
                        (a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow))
            .Select(a => a.UserId.ToString())
            .ToListAsync(ct);

        // Calculate IsFullyProcessed: true only when OCR is done AND stage is "completed"
        var ocrStage = entity.Metadata?.GetValueOrDefault("ocr_stage")?.ToString();
        var isFullyProcessed = entity.IsOcrProcessed && ocrStage == "completed";
        
        // Map to domain model
        var document = new Document
        {
            Id = entity.Id,
            Title = entity.Title,
            Description = entity.Description,
            FileName = entity.FileName,
            FilePath = entity.FilePath,
            ContentType = entity.ContentType,
            FileSize = entity.FileSize,
            CreatedAt = entity.CreatedAt,
            DocumentDate = entity.DocumentDate,
            ModifiedAt = entity.ModifiedAt,
            Status = entity.Status,
            OcrText = entity.OcrText,
            ExtractedText = entity.ExtractedText,
            Tags = entity.Tags ?? new List<string>(),
            Category = entity.Category,
            Metadata = entity.Metadata ?? new Dictionary<string, object>(),
            IsOcrProcessed = entity.IsOcrProcessed,
            IsFullyProcessed = isFullyProcessed,
            CreatedBy = entity.CreatedBy
        };

        // Index the document (this will generate embeddings with semaphore throttling)
        var success = await searchService.UpdateDocumentIndexAsync(document, ct);

        if (success)
        {
            _logger.LogInformation("✅ Document {DocId} indexed successfully", message.DocumentId);
        }
        else
        {
            _logger.LogWarning("⚠️ Document {DocId} indexing returned false", message.DocumentId);
        }
    }

    private async Task HandleDeleteAsync(
        IndexingJobMessage message,
        ISearchService searchService,
        CancellationToken ct)
    {
        var success = await searchService.DeleteDocumentIndexAsync(message.DocumentId, ct);

        if (success)
        {
            _logger.LogInformation("✅ Document {DocId} removed from index", message.DocumentId);
        }
        else
        {
            _logger.LogWarning("⚠️ Document {DocId} delete from index returned false", message.DocumentId);
        }
    }

    private async Task HandleUpdateAclAsync(
        IndexingJobMessage message,
        ISearchService searchService,
        GedDbContext db,
        CancellationToken ct)
    {
        // Fetch current document and ACLs
        var entity = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == message.DocumentId, ct);

        if (entity == null)
        {
            _logger.LogWarning("Document {DocId} not found for ACL update", message.DocumentId);
            return;
        }

        var aclRows = await db.DocumentAcls
            .Where(a => a.DocumentId == message.DocumentId &&
                        (a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow))
            .Select(a => a.UserId.ToString())
            .ToListAsync(ct);

        var document = new Document
        {
            Id = entity.Id,
            Title = entity.Title,
            Description = entity.Description,
            FileName = entity.FileName,
            FilePath = entity.FilePath,
            ContentType = entity.ContentType,
            FileSize = entity.FileSize,
            CreatedAt = entity.CreatedAt,
            DocumentDate = entity.DocumentDate,
            ModifiedAt = entity.ModifiedAt,
            Status = entity.Status,
            OcrText = entity.OcrText,
            ExtractedText = entity.ExtractedText,
            Tags = entity.Tags ?? new List<string>(),
            Category = entity.Category,
            Metadata = entity.Metadata ?? new Dictionary<string, object>(),
            IsOcrProcessed = entity.IsOcrProcessed,
            CreatedBy = entity.CreatedBy
        };

        var success = await searchService.UpdateDocumentIndexAsync(document, ct);
        _logger.LogInformation(
            "✅ Document {DocId} ACL updated in index (success={Success})",
            message.DocumentId, success);
    }

    private async Task SafeAckAsync(IChannel channel, ulong deliveryTag)
    {
        try
        {
            await channel.BasicAckAsync(deliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BasicAckAsync failed for tag {Tag}", deliveryTag);
        }
    }

    private async Task SafeNackAsync(IChannel channel, ulong deliveryTag, bool requeue)
    {
        try
        {
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: requeue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BasicNackAsync failed for tag {Tag}", deliveryTag);
        }
    }

    public override void Dispose()
    {
        _concurrencySemaphore?.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// Message format for document indexing jobs sent to RabbitMQ.
/// </summary>
public class IndexingJobMessage
{
    public Guid JobId { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public IndexingAction Action { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Actions that can be performed by the indexing worker.
/// </summary>
public enum IndexingAction
{
    Index,
    Reindex,
    Delete,
    UpdateAcl
}

/// <summary>
/// Service for publishing indexing jobs to RabbitMQ.
/// Use this instead of direct indexing for production scalability.
/// </summary>
public class IndexingQueueService : IIndexingQueueService
{
    private readonly RabbitMqService _rabbitMq;
    private readonly ILogger<IndexingQueueService> _logger;

    public IndexingQueueService(RabbitMqService rabbitMq, ILogger<IndexingQueueService> logger)
    {
        _rabbitMq = rabbitMq;
        _logger = logger;
    }

    public async Task PublishIndexJobAsync(Guid documentId, IndexingAction action, CancellationToken ct = default)
    {
        var message = new IndexingJobMessage
        {
            JobId = Guid.NewGuid(),
            DocumentId = documentId,
            Action = action,
            CreatedAt = DateTime.UtcNow
        };

        await _rabbitMq.PublishAsync("indexing-queue", message, ct);
        _logger.LogInformation(
            "📤 Published indexing job: jobId={JobId}, docId={DocId}, action={Action}",
            message.JobId, documentId, action);
    }
}

/// <summary>
/// Interface for the indexing queue service.
/// </summary>
public interface IIndexingQueueService
{
    Task PublishIndexJobAsync(Guid documentId, IndexingAction action, CancellationToken ct = default);
}
