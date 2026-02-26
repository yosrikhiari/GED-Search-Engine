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
/// FIXES vs original:
///   1. ConnectionFactory.DispatchConsumersAsync = true is required for
///      AsyncEventingBasicConsumer — without it RabbitMQ.Client v6 throws or
///      silently drops messages.
///   2. For PDFs that already have native text (extracted by Tika/iText at
///      upload time), Tesseract OCR is skipped but IsOcrProcessed is still
///      set to true so the frontend polling loop can complete.
///   3. Reconnect loop on consumer-side disconnect.
/// </summary>
public class OcrWorkerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OcrWorkerService> _logger;
    private readonly string _rabbitHost;
    private readonly string _rabbitUser;
    private readonly string _rabbitPass;

    private const string QueueName = "ocr-queue";
    private const int MaxRetries   = 5;
    private const int RetryDelayMs = 5000;

    // Lower threshold — LLM-cleaned OCR text is noisier than native PDF text
    private const float DateConfidenceThreshold = 0.3f;

    // If a PDF already has this many characters of native text, skip Tesseract
    private const int NativeTextMinChars = 50;

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
        _logger.LogInformation("🔄 OCR Worker starting…");

        // Outer reconnect loop — if the connection drops, restart the consumer
        while (!stoppingToken.IsCancellationRequested)
        {
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
                        // ── FIX: this MUST be true when using AsyncEventingBasicConsumer ──
                        // Without it RabbitMQ.Client v6 throws InvalidOperationException or
                        // silently drops messages onto the thread pool in a way that causes
                        // the async handler to never complete properly.
                        DispatchConsumersAsync        = true,
                        AutomaticRecoveryEnabled      = false, // we handle reconnect ourselves
                        RequestedHeartbeat            = TimeSpan.FromSeconds(60),
                        RequestedConnectionTimeout    = TimeSpan.FromSeconds(10),
                    };

                    connection = factory.CreateConnection();
                    _logger.LogInformation(
                        "✅ OCR Worker connected to RabbitMQ at {Host}", _rabbitHost);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "⚠️ RabbitMQ connect attempt {Attempt}/{Max} failed: {Error}",
                        attempt, MaxRetries, ex.Message);

                    if (attempt == MaxRetries)
                    {
                        _logger.LogError(
                            "❌ Could not connect to RabbitMQ after {Max} attempts. " +
                            "Waiting 30s before retrying…", MaxRetries);
                        await Task.Delay(30_000, stoppingToken);
                        break; // restart outer loop
                    }

                    await Task.Delay(RetryDelayMs * attempt, stoppingToken);
                }
            }

            if (connection == null) continue;

            try
            {
                using (connection)
                using (var channel = connection.CreateModel())
                {
                    channel.QueueDeclare(
                        queue:      QueueName,
                        durable:    true,
                        exclusive:  false,
                        autoDelete: false);

                    // Only one job at a time per worker instance
                    channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

                    _logger.LogInformation(
                        "📥 OCR Worker listening on queue '{Queue}'", QueueName);

                    var consumer = new AsyncEventingBasicConsumer(channel);

                    consumer.Received += async (_, ea) =>
                    {
                        var deliveryTag    = ea.DeliveryTag;
                        OcrJobMessage? msg = null;

                        try
                        {
                            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                            msg = JsonSerializer.Deserialize<OcrJobMessage>(json,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (msg == null)
                            {
                                _logger.LogWarning("Received null OCR message — discarding");
                                channel.BasicAck(deliveryTag, multiple: false);
                                return;
                            }

                            _logger.LogInformation(
                                "📄 OCR job received: jobId={JobId}, documentId={DocId}",
                                msg.JobId, msg.DocumentId);

                            await ProcessOcrJobAsync(msg, stoppingToken);
                            channel.BasicAck(deliveryTag, multiple: false);

                            _logger.LogInformation("✅ OCR job {JobId} acked", msg.JobId);
                        }
                        catch (OperationCanceledException)
                        {
                            channel.BasicNack(deliveryTag, multiple: false, requeue: true);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "❌ OCR job {JobId} failed for document {DocId}",
                                msg?.JobId, msg?.DocumentId);

                            channel.BasicNack(deliveryTag, multiple: false, requeue: false);

                            if (msg != null)
                                await MarkOcrFailedAsync(msg.DocumentId, ex.Message, stoppingToken);
                        }
                    };

                    channel.BasicConsume(
                        queue:    QueueName,
                        autoAck:  false,
                        consumer: consumer);

                    // Wait until cancelled or connection closes
                    try
                    {
                        await Task.Delay(Timeout.Infinite, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("🛑 OCR Worker stopping gracefully");
                        return; // exit the outer loop too
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "❌ OCR Worker consumer loop crashed — will reconnect in 10s");
                await Task.Delay(10_000, stoppingToken);
            }
        }
    }

    // ── Job processing ────────────────────────────────────────────────────────

    private async Task ProcessOcrJobAsync(OcrJobMessage message, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();

        var db            = scope.ServiceProvider.GetRequiredService<GedDbContext>();
        var ocrService    = scope.ServiceProvider.GetRequiredService<IOcrService>();
        var search        = scope.ServiceProvider.GetRequiredService<ISearchService>();
        var textCleaner   = scope.ServiceProvider.GetService<OcrTextCleaningService>();
        var dateExtractor = scope.ServiceProvider.GetService<DocumentDateExtractor>();

        // 1. Load document
        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == message.DocumentId, ct);

        if (document == null)
        {
            _logger.LogWarning("Document {DocumentId} not found — skipping", message.DocumentId);
            return;
        }

        _logger.LogInformation(
            "🖼️  OCR pipeline: docId={DocId}, type={Type}, category={Cat}, " +
            "hasNativeText={HasText} ({NativeChars} chars)",
            document.Id, document.ContentType, document.Category,
            !string.IsNullOrWhiteSpace(document.ExtractedText),
            document.ExtractedText?.Length ?? 0);

        // ── FIX: If the document already has sufficient native text (e.g. a
        // text-layer PDF processed by Tika/iText at upload time), skip
        // Tesseract and just mark OCR as processed so the polling loop ends. ──
        bool hasNativeText = !string.IsNullOrWhiteSpace(document.ExtractedText)
                             && document.ExtractedText.Trim().Length >= NativeTextMinChars;

        // Also skip if the file is gone (shouldn't happen, but defensive)
        bool fileExists = File.Exists(document.FilePath);

        if (hasNativeText && document.ContentType == "application/pdf")
        {
            _logger.LogInformation(
                "📄 PDF {DocId} has {Chars} chars of native text — skipping Tesseract, " +
                "marking OCR complete",
                document.Id, document.ExtractedText!.Length);

            document.IsOcrProcessed = true;
            document.ModifiedAt     = DateTime.UtcNow;
            document.Metadata     ??= new Dictionary<string, object>();
            document.Metadata["ocr_skipped"]      = "native_text_available";
            document.Metadata["ocr_processed_at"] = DateTime.UtcNow.ToString("o");

            await db.SaveChangesAsync(ct);
            await ReIndexDocumentAsync(document, search, ct);
            return;
        }

        if (!fileExists)
        {
            _logger.LogWarning("File missing at {Path} — marking OCR failed", document.FilePath);
            await MarkOcrFailedAsync(document.Id, "File not found on disk", ct);
            return;
        }

        // 2. Tesseract OCR
        using var fileStream = File.OpenRead(document.FilePath);
        var ocrResult = await ocrService.ProcessDocumentAsync(
            message.DocumentId, fileStream,
            message.Language ?? "eng", ct);

        _logger.LogInformation(
            "📝 Tesseract result: success={Ok}, chars={Chars}, confidence={Conf:F2}",
            ocrResult.Success, ocrResult.ExtractedText?.Length ?? 0, ocrResult.AverageConfidence);

        if (!ocrResult.Success || string.IsNullOrWhiteSpace(ocrResult.ExtractedText))
        {
            _logger.LogWarning("OCR returned no text for {DocId}: {Error}",
                message.DocumentId, ocrResult.ErrorMessage ?? "unknown");

            document.IsOcrProcessed = true;
            document.ModifiedAt     = DateTime.UtcNow;
            document.Metadata     ??= new Dictionary<string, object>();
            document.Metadata["ocr_empty"]        = true;
            document.Metadata["ocr_processed_at"] = DateTime.UtcNow.ToString("o");
            await db.SaveChangesAsync(ct);
            return;
        }

        // 3. LLM cleaning (optional)
        string cleanedText = ocrResult.ExtractedText;
        if (textCleaner != null)
        {
            _logger.LogInformation(
                "🧹 Sending {Chars} chars to Ollama for cleaning…",
                ocrResult.ExtractedText.Length);

            cleanedText = await textCleaner.CleanOcrTextAsync(ocrResult.ExtractedText, ct);

            _logger.LogInformation(
                "✅ Ollama cleaning: {Before} → {After} chars",
                ocrResult.ExtractedText.Length, cleanedText.Length);
        }

        // 4. Persist
        document.OcrText        = ocrResult.ExtractedText;
        document.IsOcrProcessed = true;
        document.ModifiedAt     = DateTime.UtcNow;

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
        if (dateExtractor != null && document.DocumentDate == null)
        {
            try
            {
                var dateInfo = await dateExtractor.ExtractDocumentDateAsync(
                    cleanedText, document.FileName,
                    document.Category ?? "Other", ct);

                if (dateInfo?.DocumentDate != null &&
                    dateInfo.Confidence >= DateConfidenceThreshold)
                {
                    document.DocumentDate = DateTime.SpecifyKind(
                        dateInfo.DocumentDate.Value, DateTimeKind.Utc);

                    document.Metadata["extracted_date"]  = document.DocumentDate.Value.ToString("yyyy-MM-dd");
                    document.Metadata["date_confidence"] = dateInfo.Confidence;
                    document.Metadata["date_type"]       = dateInfo.DateType;
                    document.Metadata["date_source"]     = "ocr_llm_cleaned";

                    _logger.LogInformation(
                        "✅ DocumentDate set: {Date} (conf={Conf:F2})",
                        document.DocumentDate.Value.ToString("yyyy-MM-dd"),
                        dateInfo.Confidence);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Date extraction failed for {DocId}", document.Id);
            }
        }

        // 6. Save & re-index
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "💾 OCR results saved for {DocId}: pages={Pages}, conf={Conf:F2}, date={Date}",
            message.DocumentId, ocrResult.PageCount, ocrResult.AverageConfidence,
            document.DocumentDate?.ToString("yyyy-MM-dd") ?? "none");

        await ReIndexDocumentAsync(document, search, ct);
    }

    private async Task ReIndexDocumentAsync(
        DocumentEntity document,
        ISearchService search,
        CancellationToken ct)
    {
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
                IsOcrProcessed = document.IsOcrProcessed
            };

            await search.UpdateDocumentIndexAsync(domainDoc, ct);
            _logger.LogInformation("🔍 Re-indexed {DocId} after OCR pipeline", document.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-index {DocId} after OCR", document.Id);
        }
    }

    private async Task MarkOcrFailedAsync(
        Guid documentId, string error, CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GedDbContext>();
            var doc = await db.Documents.FindAsync(new object[] { documentId }, ct);
            if (doc != null)
            {
                doc.IsOcrProcessed = false;
                doc.ModifiedAt     = DateTime.UtcNow;
                doc.Metadata     ??= new Dictionary<string, object>();
                doc.Metadata["ocr_error"]     = error;
                doc.Metadata["ocr_failed_at"] = DateTime.UtcNow.ToString("o");
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark OCR failed for {DocId}", documentId);
        }
    }
}

public class OcrJobMessage
{
    public Guid   JobId      { get; set; }
    public Guid   DocumentId { get; set; }
    public string Language   { get; set; } = "eng";
}