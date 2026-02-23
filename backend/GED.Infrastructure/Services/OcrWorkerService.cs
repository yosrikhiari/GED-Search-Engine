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
///
/// Pipeline per job:
///   1. Tesseract OCR  → raw text
///   2. Ollama LLM     → clean text  (fixes OCR artifacts)
///   3. DocumentDateExtractor → extract document date from clean text
///   4. Persist to PostgreSQL + re-index in OpenSearch
/// </summary>
public class OcrWorkerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OcrWorkerService> _logger;
    private readonly string _rabbitHost;
    private readonly string _rabbitUser;
    private readonly string _rabbitPass;

    private const string QueueName = "ocr-queue";
    private const int MaxRetries   = 3;
    private const int RetryDelayMs = 5000;

    // Lower threshold for OCR content — LLM-cleaned text is still noisier than
    // native PDF text, so we accept dates with >= 30% confidence.
    private const float DateConfidenceThreshold = 0.3f;

    public OcrWorkerService(
        IServiceProvider serviceProvider,
        ILogger<OcrWorkerService> logger,
        string rabbitHost = "localhost",
        string rabbitUser = "admin",
        string rabbitPass = "admin123")
    {
        _serviceProvider = serviceProvider;
        _logger          = logger;
        _rabbitHost      = rabbitHost;
        _rabbitUser      = rabbitUser;
        _rabbitPass      = rabbitPass;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🔄 OCR Worker starting...");

        IConnection? connection = null;
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var factory = new ConnectionFactory
                {
                    HostName               = _rabbitHost,
                    UserName               = _rabbitUser,
                    Password               = _rabbitPass,
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
                    _logger.LogError(
                        "❌ Could not connect to RabbitMQ after {Max} attempts. OCR Worker stopping.",
                        MaxRetries);
                    return;
                }
                await Task.Delay(RetryDelayMs * attempt, stoppingToken);
            }
        }

        if (connection == null) return;

        using (connection)
        using (var channel = connection.CreateModel())
        {
            channel.QueueDeclare(
                queue:      QueueName,
                durable:    true,
                exclusive:  false,
                autoDelete: false
            );
            channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

            _logger.LogInformation("📥 OCR Worker listening on queue '{Queue}'", QueueName);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.Received += async (_, ea) =>
            {
                var messageId     = ea.DeliveryTag;
                OcrJobMessage? jobMessage = null;

                try
                {
                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
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
                    _logger.LogInformation("✅ OCR job {JobId} completed", jobMessage.JobId);
                }
                catch (OperationCanceledException)
                {
                    channel.BasicNack(messageId, multiple: false, requeue: true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "❌ OCR job {JobId} failed for document {DocumentId}",
                        jobMessage?.JobId, jobMessage?.DocumentId);
                    channel.BasicNack(messageId, multiple: false, requeue: false);
                    if (jobMessage != null)
                        await MarkOcrFailedAsync(jobMessage.DocumentId, ex.Message, stoppingToken);
                }
            };

            channel.BasicConsume(queue: QueueName, autoAck: false, consumer: consumer);

            try { await Task.Delay(Timeout.Infinite, stoppingToken); }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("🛑 OCR Worker stopping gracefully");
            }
        }
    }

    private async Task ProcessOcrJobAsync(OcrJobMessage message, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();

        var db            = scope.ServiceProvider.GetRequiredService<GedDbContext>();
        var ocrService    = scope.ServiceProvider.GetRequiredService<IOcrService>();
        var search        = scope.ServiceProvider.GetRequiredService<ISearchService>();
        var textCleaner   = scope.ServiceProvider.GetService<OcrTextCleaningService>();
        var dateExtractor = scope.ServiceProvider.GetService<DocumentDateExtractor>();

        if (textCleaner == null)
            _logger.LogWarning("⚠️ OcrTextCleaningService not registered — OCR text will not be LLM-cleaned");
        if (dateExtractor == null)
            _logger.LogWarning("⚠️ DocumentDateExtractor not registered — date extraction disabled");

        // 1. Load document
        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == message.DocumentId, cancellationToken);

        if (document == null)
        {
            _logger.LogWarning("Document {DocumentId} not found, skipping", message.DocumentId);
            return;
        }
        if (!File.Exists(document.FilePath))
        {
            _logger.LogWarning("File not found at {FilePath}, skipping", document.FilePath);
            return;
        }

        _logger.LogInformation(
            "🖼️  OCR pipeline starting for {DocumentId} (type={ContentType}, category={Category})",
            document.Id, document.ContentType, document.Category);

        // 2. Tesseract OCR
        using var fileStream = File.OpenRead(document.FilePath);
        var ocrResult = await ocrService.ProcessDocumentAsync(
            message.DocumentId, fileStream, message.Language ?? "eng", cancellationToken);

        _logger.LogInformation(
            "📝 Tesseract done for {DocumentId}: success={Success}, chars={Chars}, confidence={Conf:F2}",
            message.DocumentId, ocrResult.Success,
            ocrResult.ExtractedText?.Length ?? 0, ocrResult.AverageConfidence);

        if (!ocrResult.Success || string.IsNullOrWhiteSpace(ocrResult.ExtractedText))
        {
            _logger.LogWarning("OCR returned no text for {DocumentId}: {Error}",
                message.DocumentId, ocrResult.ErrorMessage ?? "unknown");

            document.IsOcrProcessed = true;
            document.ModifiedAt     = DateTime.UtcNow;
            document.Metadata      ??= new Dictionary<string, object>();
            document.Metadata["ocr_empty"]        = true;
            document.Metadata["ocr_processed_at"] = DateTime.UtcNow.ToString("o");
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        // 3. LLM cleaning via Ollama
        string cleanedText = ocrResult.ExtractedText;
        if (textCleaner != null)
        {
            _logger.LogInformation("🧹 Sending {Chars} chars to Ollama for OCR cleaning...",
                ocrResult.ExtractedText.Length);

            cleanedText = await textCleaner.CleanOcrTextAsync(
                ocrResult.ExtractedText, cancellationToken);

            _logger.LogInformation("✅ Ollama cleaning done: {Before} → {After} chars",
                ocrResult.ExtractedText.Length, cleanedText.Length);
        }

        // 4. Persist raw OCR + cleaned text
        document.OcrText        = ocrResult.ExtractedText;   // raw — kept for audit
        document.IsOcrProcessed = true;
        document.ModifiedAt     = DateTime.UtcNow;

        // Cleaned text becomes the searchable extracted text
        if (string.IsNullOrWhiteSpace(document.ExtractedText))
            document.ExtractedText = cleanedText;
        else
            document.ExtractedText += "\n\n[OCR - LLM cleaned]\n" + cleanedText;

        document.Metadata ??= new Dictionary<string, object>();
        document.Metadata["ocr_raw_length"]     = ocrResult.ExtractedText.Length;
        document.Metadata["ocr_cleaned_length"] = cleanedText.Length;
        document.Metadata["ocr_confidence"]     = ocrResult.AverageConfidence;
        document.Metadata["ocr_processed_at"]   = DateTime.UtcNow.ToString("o");

        // 5. Date extraction from cleaned text
        if (dateExtractor != null)
        {
            bool isImage          = document.ContentType?.StartsWith("image/") == true;
            bool shouldExtractDate = document.DocumentDate == null || isImage;

            if (shouldExtractDate)
            {
                try
                {
                    _logger.LogInformation(
                        "🗓️  Extracting date from cleaned OCR text for {DocumentId} ({Chars} chars)...",
                        document.Id, cleanedText.Length);

                    var dateInfo = await dateExtractor.ExtractDocumentDateAsync(
                        cleanedText, document.FileName,
                        document.Category ?? "Other", cancellationToken);

                    _logger.LogInformation(
                        "🗓️  Date result for {DocumentId}: Date={Date}, Confidence={Conf:F2}, Type={Type}",
                        document.Id,
                        dateInfo?.DocumentDate?.ToString("yyyy-MM-dd") ?? "null",
                        dateInfo?.Confidence ?? 0f,
                        dateInfo?.DateType ?? "none");

                    if (dateInfo?.DocumentDate != null && dateInfo.Confidence >= DateConfidenceThreshold)
                    {
                        document.DocumentDate = DateTime.SpecifyKind(
                            dateInfo.DocumentDate.Value, DateTimeKind.Utc);

                        document.Metadata["extracted_date"]  = document.DocumentDate.Value.ToString("yyyy-MM-dd");
                        document.Metadata["date_confidence"] = dateInfo.Confidence;
                        document.Metadata["date_type"]       = dateInfo.DateType;
                        document.Metadata["date_source"]     = "ocr_llm_cleaned";

                        _logger.LogInformation(
                            "✅ DocumentDate set for {DocumentId}: {Date} (conf={Conf:F2})",
                            document.Id,
                            document.DocumentDate.Value.ToString("yyyy-MM-dd"),
                            dateInfo.Confidence);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "📭 No date found (conf={Conf:F2} < threshold={Threshold}) for {DocumentId}",
                            dateInfo?.Confidence ?? 0f, DateConfidenceThreshold, document.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Date extraction failed for {DocumentId}", document.Id);
                }
            }
        }

        // 6. Save to PostgreSQL
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "💾 Saved OCR pipeline results for {DocumentId}: " +
            "pages={Pages}, conf={Conf:F2}, documentDate={Date}",
            message.DocumentId, ocrResult.PageCount, ocrResult.AverageConfidence,
            document.DocumentDate?.ToString("yyyy-MM-dd") ?? "none");

        // 7. Re-index in OpenSearch with updated text + date
        try
        {
            var domainDoc = new GED.Core.Models.Document
            {
                Id             = document.Id,
                Title          = document.Title,
                Description    = document.Description,
                FileName       = document.FileName,
                FilePath       = document.FilePath,
                ContentType    = document.ContentType,
                FileSize       = document.FileSize,
                CreatedAt      = document.CreatedAt,
                DocumentDate   = document.DocumentDate,
                ModifiedAt     = document.ModifiedAt,
                Status         = document.Status,
                OcrText        = document.OcrText,
                ExtractedText  = document.ExtractedText,
                Tags           = document.Tags,
                Category       = document.Category,
                Metadata       = document.Metadata,
                IsOcrProcessed = true
            };

            await search.UpdateDocumentIndexAsync(domainDoc, cancellationToken);

            _logger.LogInformation(
                "🔍 Re-indexed {DocumentId} after OCR pipeline (documentDate={Date})",
                message.DocumentId,
                document.DocumentDate?.ToString("yyyy-MM-dd") ?? "none");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-index {DocumentId} after OCR", message.DocumentId);
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
                document.Metadata      ??= new Dictionary<string, object>();
                document.Metadata["ocr_error"]     = error;
                document.Metadata["ocr_failed_at"] = DateTime.UtcNow.ToString("o");
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark OCR failed for {DocumentId}", documentId);
        }
    }
}

public class OcrJobMessage
{
    public Guid   JobId      { get; set; }
    public Guid   DocumentId { get; set; }
    public string Language   { get; set; } = "eng";
}