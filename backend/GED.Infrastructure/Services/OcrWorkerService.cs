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
/// Background worker that consumes OCR jobs from RabbitMQ and processes them.
/// Previously, OCR jobs were queued but NEVER consumed — this fixes that.
/// </summary>
public class OcrWorkerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OcrWorkerService> _logger;
    private readonly string _rabbitHost;
    private readonly string _rabbitUser;
    private readonly string _rabbitPass;

    private const string QueueName = "ocr-queue";
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 5000;

    public OcrWorkerService(
        IServiceProvider serviceProvider,
        ILogger<OcrWorkerService> logger,
        string rabbitHost = "localhost",
        string rabbitUser = "admin",
        string rabbitPass = "admin123")
    {
        _serviceProvider  = serviceProvider;
        _logger           = logger;
        _rabbitHost       = rabbitHost;
        _rabbitUser       = rabbitUser;
        _rabbitPass       = rabbitPass;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🔄 OCR Worker starting...");

        // Retry connection loop — RabbitMQ might not be ready immediately
        IConnection? connection = null;
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = _rabbitHost,
                    UserName = _rabbitUser,
                    Password = _rabbitPass,
                    DispatchConsumersAsync = true
                };
                connection = factory.CreateConnection();
                _logger.LogInformation("✅ OCR Worker connected to RabbitMQ at {Host}", _rabbitHost);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "⚠️ RabbitMQ connection attempt {Attempt}/{Max} failed: {Error}",
                    attempt, MaxRetries, ex.Message);

                if (attempt == MaxRetries)
                {
                    _logger.LogError("❌ Could not connect to RabbitMQ after {Max} attempts. OCR Worker stopping.", MaxRetries);
                    return;
                }

                await Task.Delay(RetryDelayMs * attempt, stoppingToken);
            }
        }

        if (connection == null) return;

        using (connection)
        using (var channel = connection.CreateModel())
        {
            // Declare durable queue — survives broker restart
            channel.QueueDeclare(
                queue:      QueueName,
                durable:    true,
                exclusive:  false,
                autoDelete: false
            );

            // Process one message at a time to avoid overwhelming the server
            channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

            _logger.LogInformation("📥 OCR Worker listening on queue '{Queue}'", QueueName);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.Received += async (_, ea) =>
            {
                var messageId = ea.DeliveryTag;
                OcrJobMessage? jobMessage = null;

                try
                {
                    var body = ea.Body.ToArray();
                    var json = Encoding.UTF8.GetString(body);
                    jobMessage = JsonSerializer.Deserialize<OcrJobMessage>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (jobMessage == null)
                    {
                        _logger.LogWarning("Received null OCR job message, discarding");
                        channel.BasicAck(messageId, multiple: false);
                        return;
                    }

                    _logger.LogInformation(
                        "📄 Processing OCR job {JobId} for document {DocumentId}",
                        jobMessage.JobId, jobMessage.DocumentId);

                    await ProcessOcrJobAsync(jobMessage, stoppingToken);

                    channel.BasicAck(messageId, multiple: false);
                    _logger.LogInformation("✅ OCR job {JobId} completed successfully", jobMessage.JobId);
                }
                catch (OperationCanceledException)
                {
                    // Service is stopping — requeue for another instance
                    channel.BasicNack(messageId, multiple: false, requeue: true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "❌ OCR job {JobId} failed for document {DocumentId}",
                        jobMessage?.JobId, jobMessage?.DocumentId);

                    // Don't requeue permanently failing messages
                    channel.BasicNack(messageId, multiple: false, requeue: false);

                    // Update document status to Failed in DB
                    if (jobMessage != null)
                    {
                        await MarkOcrFailedAsync(jobMessage.DocumentId, ex.Message, stoppingToken);
                    }
                }
            };

            channel.BasicConsume(queue: QueueName, autoAck: false, consumer: consumer);

            // Keep the worker alive until cancellation
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("🛑 OCR Worker stopping gracefully");
            }
        }
    }

    /// <summary>
    /// Core OCR processing logic — runs inside a fresh DI scope per job
    /// so EF Core DbContext is properly scoped.
    /// </summary>
    private async Task ProcessOcrJobAsync(OcrJobMessage message, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db         = scope.ServiceProvider.GetRequiredService<GedDbContext>();
        var ocrService = scope.ServiceProvider.GetRequiredService<IOcrService>();
        var storage    = scope.ServiceProvider.GetRequiredService<IStorageService>();
        var search     = scope.ServiceProvider.GetRequiredService<ISearchService>();

        // Load document from DB
        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == message.DocumentId, cancellationToken);

        if (document == null)
        {
            _logger.LogWarning("Document {DocumentId} not found in DB, skipping OCR", message.DocumentId);
            return;
        }

        if (!File.Exists(document.FilePath))
        {
            _logger.LogWarning("File not found at {FilePath}, skipping OCR", document.FilePath);
            return;
        }

        // Open file stream and run OCR
        using var fileStream = File.OpenRead(document.FilePath);
        var result = await ocrService.ProcessDocumentAsync(
            message.DocumentId,
            fileStream,
            message.Language ?? "eng",
            cancellationToken
        );

        // Update document with OCR results
        if (result.Success && !string.IsNullOrWhiteSpace(result.ExtractedText))
        {
            document.OcrText       = result.ExtractedText;
            document.IsOcrProcessed = true;
            document.ModifiedAt    = DateTime.UtcNow;

            // Enrich extracted text (append OCR to any native text)
            if (string.IsNullOrWhiteSpace(document.ExtractedText))
                document.ExtractedText = result.ExtractedText;
            else
                document.ExtractedText += "\n\n[OCR]\n" + result.ExtractedText;

            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "📝 OCR results saved for document {DocumentId}: {Pages} pages, confidence {Confidence:F2}",
                message.DocumentId, result.PageCount, result.AverageConfidence);

            // Re-index in OpenSearch with the new OCR text
            try
            {
                var domainDoc = new GED.Core.Models.Document
                {
                    Id            = document.Id,
                    Title         = document.Title,
                    Description   = document.Description,
                    FileName      = document.FileName,
                    FilePath      = document.FilePath,
                    ContentType   = document.ContentType,
                    FileSize      = document.FileSize,
                    CreatedAt     = document.CreatedAt,
                    DocumentDate  = document.DocumentDate,
                    ModifiedAt    = document.ModifiedAt,
                    Status        = document.Status,
                    OcrText       = document.OcrText,
                    ExtractedText = document.ExtractedText,
                    Tags          = document.Tags,
                    Category      = document.Category,
                    Metadata      = document.Metadata,
                    IsOcrProcessed = true
                };

                await search.UpdateDocumentIndexAsync(domainDoc, cancellationToken);
                _logger.LogInformation("🔍 Document {DocumentId} re-indexed after OCR", message.DocumentId);
            }
            catch (Exception ex)
            {
                // Non-fatal: OCR text is saved in DB, search index will be stale but not broken
                _logger.LogWarning(ex, "Failed to re-index document {DocumentId} after OCR", message.DocumentId);
            }
        }
        else
        {
            _logger.LogWarning(
                "OCR completed but returned no text for document {DocumentId}: {Error}",
                message.DocumentId, result.ErrorMessage);
        }
    }

    private async Task MarkOcrFailedAsync(Guid documentId, string error, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GedDbContext>();

            var document = await db.Documents.FindAsync(new object[] { documentId }, cancellationToken);
            if (document != null)
            {
                document.IsOcrProcessed = false;
                document.ModifiedAt     = DateTime.UtcNow;
                // Store error in metadata
                document.Metadata ??= new Dictionary<string, object>();
                document.Metadata["ocr_error"] = error;
                document.Metadata["ocr_failed_at"] = DateTime.UtcNow.ToString("o");

                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark OCR as failed for document {DocumentId}", documentId);
        }
    }
}

/// <summary>
/// Message schema for OCR queue — must match what TesseractOcrService publishes.
/// </summary>
public class OcrJobMessage
{
    public Guid JobId      { get; set; }
    public Guid DocumentId { get; set; }
    public string Language { get; set; } = "eng";
}