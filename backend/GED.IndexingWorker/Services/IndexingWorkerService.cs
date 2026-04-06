using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GED.Infrastructure.Services;

public class IndexingWorkerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IndexingWorkerService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IPipelineEventService? _pipelineEventService;

    private readonly string _rabbitHost;
    private readonly string _rabbitUser;
    private readonly string _rabbitPass;
    private readonly int _concurrencyLimit;
    private readonly int _batchSize;

    private const string QueueName = "indexing-queue";
    private const string DlxName = "indexing-dlx";
    private const string DeadLetterQueueName = "indexing-dead-letter";
    private const int MaxConnectRetries = 5;
    private const int RetryDelayMs = 5000;

    private SemaphoreSlim? _concurrencySemaphore;

    public IndexingWorkerService(
        IServiceProvider serviceProvider,
        ILogger<IndexingWorkerService> logger,
        IConfiguration configuration,
        IPipelineEventService? pipelineEventService = null,
        string rabbitHost = "localhost",
        string rabbitUser = "admin",
        string rabbitPass = "admin123",
        int concurrencyLimit = 4,
        int batchSize = 10)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _pipelineEventService = pipelineEventService;
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
            "Indexing Worker starting (concurrency={Concurrency}, batch={Batch})",
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
                    _logger.LogInformation("Indexing Worker connected to RabbitMQ at {Host}", _rabbitHost);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("RabbitMQ connect attempt {Attempt}/{Max} failed: {Error}",
                        attempt, MaxConnectRetries, ex.Message);

                    if (attempt == MaxConnectRetries)
                    {
                        _logger.LogError("Could not connect to RabbitMQ. Waiting 30s");
                        await Task.Delay(30_000, stoppingToken);
                        break;
                    }

                    var delay = (int)Math.Min(RetryDelayMs * Math.Pow(2, attempt - 1), 60_000);
                    await Task.Delay(delay, stoppingToken);
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
                        _logger.LogWarning("RabbitMQ connection shutdown: {Reason}", args.ReplyText);
                        connectionClosed.TrySetResult();
                        return Task.CompletedTask;
                    };

                    var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                    await using (channel)
                    {
                        await channel.ExchangeDeclareAsync(
                            exchange: DlxName,
                            type: ExchangeType.Direct,
                            durable: true,
                            autoDelete: false,
                            cancellationToken: stoppingToken);

                        await channel.QueueDeclareAsync(
                            queue: DeadLetterQueueName,
                            durable: true,
                            exclusive: false,
                            autoDelete: false,
                            arguments: null,
                            cancellationToken: stoppingToken);

                        await channel.QueueBindAsync(
                            queue: DeadLetterQueueName,
                            exchange: DlxName,
                            routingKey: QueueName,
                            cancellationToken: stoppingToken);

                        await channel.QueueDeclareAsync(
                            queue: QueueName,
                            durable: true,
                            exclusive: false,
                            autoDelete: false,
                            arguments: new Dictionary<string, object?>
                            {
                                ["x-dead-letter-exchange"]    = DlxName,
                                ["x-dead-letter-routing-key"] = QueueName,
                                ["x-max-length"] = 1000,
                                ["x-message-ttl"] = 3600000
                            },
                            cancellationToken: stoppingToken);

                        await channel.BasicQosAsync(
                            prefetchSize: 0,
                            prefetchCount: (ushort)_batchSize,
                            global: false,
                            cancellationToken: stoppingToken);

                        _logger.LogInformation("Indexing Worker listening on queue '{Queue}'", QueueName);

                        var consumer = new AsyncEventingBasicConsumer(channel);
                        consumer.ReceivedAsync += async (_, ea) =>
                        {
                            var deliveryTag = ea.DeliveryTag;
                            IndexingJobMessage? message = null;
                            Guid documentId = Guid.Empty;

                            try
                            {
                                var json = Encoding.UTF8.GetString(ea.Body.ToArray());

                                var simpleMsg = JsonSerializer.Deserialize<IndexingJobSimpleMessage>(json,
                                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                                if (simpleMsg != null && simpleMsg.DocumentId != Guid.Empty)
                                {
                                    documentId = simpleMsg.DocumentId;
                                    message = new IndexingJobMessage
                                    {
                                        JobId = Guid.NewGuid(),
                                        DocumentId = documentId,
                                        Action = IndexingAction.Index,
                                        CreatedAt = DateTime.UtcNow,
                                        CorrelationId = simpleMsg.CorrelationId
                                    };
                                    _logger.LogInformation(
                                        "Indexing job received (simple format): documentId={DocId}, correlationId={CorrId}",
                                        documentId, simpleMsg.CorrelationId);
                                }
                                else
                                {
                                    message = JsonSerializer.Deserialize<IndexingJobMessage>(json,
                                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                                    if (message == null)
                                    {
                                        _logger.LogWarning("Received null indexing message — discarding");
                                        await SafeAckAsync(channel, deliveryTag);
                                        return;
                                    }

                                    documentId = message.DocumentId;
                                    _logger.LogInformation(
                                        "Indexing job received: jobId={JobId}, documentId={DocId}, action={Action}",
                                        message.JobId, message.DocumentId, message.Action);
                                }

                                var sw = Stopwatch.StartNew();
                                var correlationId = message.CorrelationId ?? string.Empty;
                                var startEvt = new PipelineEvent
                                {
                                    Timestamp = DateTime.UtcNow,
                                    PipelineStage = PipelineStages.IndexingWorker,
                                    Status = PipelineStatuses.Started,
                                    CorrelationId = correlationId,
                                    DocumentId = documentId.ToString()
                                };
                                if (_pipelineEventService != null)
                                    _ = _pipelineEventService.EmitPipelineEventAsync(startEvt);

                                await ProcessIndexingJobAsync(message, stoppingToken);

                                sw.Stop();
                                var completedEvt = new PipelineEvent
                                {
                                    Timestamp = DateTime.UtcNow,
                                    PipelineStage = PipelineStages.IndexingWorker,
                                    Status = PipelineStatuses.Completed,
                                    CorrelationId = correlationId,
                                    DocumentId = documentId.ToString(),
                                    DurationMs = sw.ElapsedMilliseconds,
                                    EmbeddingModel = "bge-m3",
                                    EmbeddingDimension = 1024
                                };
                                if (_pipelineEventService != null)
                                    _ = _pipelineEventService.EmitPipelineEventAsync(completedEvt);

                                await UpdateOutboxAcknowledgedAsync(documentId, stoppingToken);

                                await SafeAckAsync(channel, deliveryTag);

                                _logger.LogInformation("Indexing job completed and acked for document {DocId}", documentId);
                            }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                            {
                                _logger.LogInformation("Indexing job cancelled during shutdown — nacking for requeue");
                                await SafeNackAsync(channel, deliveryTag, requeue: true);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Indexing job failed for document {DocId}", documentId);

                                var correlationId = message?.CorrelationId ?? Guid.NewGuid().ToString("N")[..12];
                                var failedEvt = new PipelineEvent
                                {
                                    Timestamp = DateTime.UtcNow,
                                    PipelineStage = PipelineStages.IndexingWorker,
                                    Status = PipelineStatuses.Failed,
                                    CorrelationId = correlationId,
                                    DocumentId = documentId.ToString(),
                                    ErrorMessage = ex.Message,
                                    ErrorType = ex.GetType().Name
                                };
                                if (_pipelineEventService != null)
                                    _ = _pipelineEventService.EmitPipelineEventAsync(failedEvt);

                                await SafeNackAsync(channel, deliveryTag, requeue: false);

                                try { await connection.CloseAsync(); }
                                catch (Exception closeEx)
                                {
                                    _logger.LogWarning(closeEx, "Failed to close RabbitMQ connection");
                                }
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
                            _logger.LogInformation("Indexing Worker stopping gracefully");
                            return;
                        }

                        _logger.LogWarning("Connection lost — reconnecting in 5s");
                        await Task.Delay(5_000, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Indexing Worker consumer loop crashed — reconnecting in 10s");
                await Task.Delay(10_000, stoppingToken);
            }
        }
    }

    private async Task ProcessIndexingJobAsync(IndexingJobMessage message, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();
        var chunkingService = scope.ServiceProvider.GetRequiredService<DocumentChunkingService>();
        var db = scope.ServiceProvider.GetRequiredService<GedDbContext>();

        var correlationId = message.CorrelationId ?? Guid.NewGuid().ToString("N")[..12];
        
        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            switch (message.Action)
            {
                case IndexingAction.Index:
                case IndexingAction.Reindex:
                    await HandleIndexOrReindexAsync(message, searchService, chunkingService, db, ct);
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
    }

    private async Task HandleIndexOrReindexAsync(
        IndexingJobMessage message,
        ISearchService searchService,
        DocumentChunkingService chunkingService,
        GedDbContext db,
        CancellationToken ct)
    {
        var entity = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == message.DocumentId, ct);

        if (entity == null)
        {
            _logger.LogWarning("Document {DocId} not found for indexing", message.DocumentId);
            return;
        }

        var ocrStage = entity.Metadata?.GetValueOrDefault("ocr_stage")?.ToString();
        var isFullyProcessed = entity.IsOcrProcessed && ocrStage == "completed";
        
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

        var success = await searchService.UpdateDocumentIndexAsync(document, ct);

        if (success)
        {
            _logger.LogInformation("Document {DocId} indexed successfully", message.DocumentId);
        }
        else
        {
            _logger.LogWarning("Document {DocId} indexing returned false", message.DocumentId);
        }

        var textToChunk = !string.IsNullOrWhiteSpace(entity.ExtractedText) ? entity.ExtractedText : entity.OcrText;
        if (!string.IsNullOrWhiteSpace(textToChunk))
        {
            var chunks = chunkingService.ChunkText(document.Id, textToChunk);
            if (chunks.Any())
            {
                try
                {
                    await searchService.IndexChunksAsync(document, chunks, ct);
                    _logger.LogInformation("Document {DocId} chunked into {Count} chunks for RAG", 
                        message.DocumentId, chunks.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to index chunks for document {DocId}", message.DocumentId);
                }
            }
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
            _logger.LogInformation("Document {DocId} removed from index", message.DocumentId);
        }
        else
        {
            _logger.LogWarning("Document {DocId} delete from index returned false", message.DocumentId);
        }
    }

    private async Task HandleUpdateAclAsync(
        IndexingJobMessage message,
        ISearchService searchService,
        GedDbContext db,
        CancellationToken ct)
    {
        var entity = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == message.DocumentId, ct);

        if (entity == null)
        {
            _logger.LogWarning("Document {DocId} not found for ACL update", message.DocumentId);
            return;
        }

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
            "Document {DocId} ACL updated in index (success={Success})",
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

    private async Task UpdateOutboxAcknowledgedAsync(Guid documentId, CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GedDbContext>();

            var outboxMessage = await db.OutboxMessages
                .Where(m => m.Type == "IndexingJob" && m.ProcessedAt != null && m.AcknowledgedAt == null)
                .Where(m => m.Payload.Contains(documentId.ToString()))
                .OrderBy(m => m.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (outboxMessage != null)
            {
                outboxMessage.AcknowledgedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                _logger.LogDebug("Updated AcknowledgedAt for outbox message {Id} (document {DocId})",
                    outboxMessage.Id, documentId);
            }
            else
            {
                _logger.LogDebug("No pending outbox message found for document {DocId}", documentId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update AcknowledgedAt for document {DocId}", documentId);
        }
    }

    public override void Dispose()
    {
        _concurrencySemaphore?.Dispose();
        base.Dispose();
    }
}
