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
using System.Text.RegularExpressions;

namespace GED.Infrastructure.Services;

/// <summary>
/// Background worker that consumes OCR jobs from RabbitMQ and processes them.
///
/// Pipeline stages (each stage is persisted to DB so the frontend can track):
///
///   1. Tesseract OCR          → sets IsOcrProcessed = true, ocr_stage = "text_extracted"
///                               Frontend polling resolves here — no more 60-second waits.
///   2. Ollama LLM cleaning    → ocr_stage = "llm_cleaning"
///   3. Date extraction        → still ocr_stage = "llm_cleaning"
///   4. AI tag + description   → still ocr_stage = "llm_cleaning"  ← NEW
///   5. Final save + re-index  → ocr_stage = "completed"
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

    private const float DateConfidenceThreshold = 0.3f;
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

        while (!stoppingToken.IsCancellationRequested)
        {
            IConnection? connection = null;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var factory = new ConnectionFactory
                    {
                        HostName                   = _rabbitHost,
                        UserName                   = _rabbitUser,
                        Password                   = _rabbitPass,
                        DispatchConsumersAsync     = true,
                        AutomaticRecoveryEnabled   = false,
                        RequestedHeartbeat         = TimeSpan.FromSeconds(60),
                        RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
                    };

                    connection = factory.CreateConnection();
                    _logger.LogInformation("✅ OCR Worker connected to RabbitMQ at {Host}", _rabbitHost);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("⚠️ RabbitMQ connect attempt {Attempt}/{Max} failed: {Error}",
                        attempt, MaxRetries, ex.Message);

                    if (attempt == MaxRetries)
                    {
                        _logger.LogError("❌ Could not connect to RabbitMQ after {Max} attempts. Waiting 30s…", MaxRetries);
                        await Task.Delay(30_000, stoppingToken);
                        break;
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
                    channel.QueueDeclare(queue: QueueName, durable: true, exclusive: false, autoDelete: false);
                    channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

                    _logger.LogInformation("📥 OCR Worker listening on queue '{Queue}'", QueueName);

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

                            _logger.LogInformation("📄 OCR job received: jobId={JobId}, documentId={DocId}",
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
                            _logger.LogError(ex, "❌ OCR job {JobId} failed for document {DocId}",
                                msg?.JobId, msg?.DocumentId);

                            channel.BasicNack(deliveryTag, multiple: false, requeue: false);

                            if (msg != null)
                                await MarkOcrFailedAsync(msg.DocumentId, ex.Message, stoppingToken);
                        }
                    };

                    channel.BasicConsume(queue: QueueName, autoAck: false, consumer: consumer);

                    try { await Task.Delay(Timeout.Infinite, stoppingToken); }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("🛑 OCR Worker stopping gracefully");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ OCR Worker consumer loop crashed — will reconnect in 10s");
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
        var enricher      = scope.ServiceProvider.GetService<OcrMetadataEnrichmentService>(); // ← NEW

        // ── 1. Load document and pipleline ─────────────────────────────────────────────────
        var document = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == message.DocumentId, ct);

        if (document == null)
        {
            _logger.LogWarning("Document {DocumentId} not found — skipping", message.DocumentId);
            return;
        }

        _logger.LogInformation(
            "🖼️  OCR pipeline: docId={DocId}, type={Type}, category={Cat}, hasNativeText={HasText} ({NativeChars} chars)",
            document.Id, document.ContentType, document.Category,
            !string.IsNullOrWhiteSpace(document.ExtractedText),
            document.ExtractedText?.Length ?? 0);

        var pipelineId = $"ocr-{message.DocumentId.ToString()[..8]}";






        // Mark as "processing" so frontend shows correct label immediately
        await SetStageAsync(db, document, "processing", ct);

        // ── Shortcut: PDF with sufficient native text ────────────────────────
        bool hasNativeText = !string.IsNullOrWhiteSpace(document.ExtractedText)
                             && document.ExtractedText.Trim().Length >= NativeTextMinChars;
        bool fileExists = File.Exists(document.FilePath);

        if (hasNativeText && document.ContentType == "application/pdf")
        {
            _logger.LogInformation(
                "📄 PDF {DocId} has {Chars} chars of native text — skipping Tesseract",
                document.Id, document.ExtractedText!.Length);

            document.IsOcrProcessed = true;
            document.ModifiedAt     = DateTime.UtcNow;
            document.Metadata     ??= new Dictionary<string, object>();
            document.Metadata["ocr_skipped"]      = "native_text_available";
            document.Metadata["ocr_stage"]        = "llm_cleaning";
            document.Metadata["ocr_processed_at"] = DateTime.UtcNow.ToString("o");

            await db.SaveChangesAsync(ct);

            // ── AI metadata enrichment for native-text PDFs ──────────────────
            await EnrichAndSaveAsync(
                db, document, search, enricher, dateExtractor,
                document.ExtractedText!, "native_text_llm", ct);

            return;
        }

        if (!fileExists)
        {
            _logger.LogWarning("File missing at {Path} — marking OCR failed", document.FilePath);
            await MarkOcrFailedAsync(document.Id, "File not found on disk", ct);
            return;
        }

        // ── 2. Tesseract OCR ─────────────────────────────────────────────────
        _logger.LogInformation("🔍 Starting Tesseract OCR for {DocId}…", document.Id);

        using var fileStream = File.OpenRead(document.FilePath);
        var ocrResult = await ocrService.ProcessDocumentAsync(
            message.DocumentId, fileStream, message.Language ?? "eng", ct);

        _logger.LogInformation(
            "[{PipelineId}] OCR pipeline started for {DocId}", 
            pipelineId, message.DocumentId);

        // Pass it as a structured log property throughout:
        _logger.LogInformation(
            "[{PipelineId}] Stage text_extracted: {Chars} chars", 
            pipelineId, ocrResult.ExtractedText?.Length);

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
            document.Metadata["ocr_stage"]        = "completed";
            document.Metadata["ocr_processed_at"] = DateTime.UtcNow.ToString("o");
            await db.SaveChangesAsync(ct);
            return;
        }

        // ── 3. STAGE COMMIT: Tesseract done → IsOcrProcessed = true ─────────
        //
        // Persist IsOcrProcessed = true NOW, before Ollama runs.
        // The frontend polling loop resolves here instead of waiting for LLM.
        // ────────────────────────────────────────────────────────────────────
        document.OcrText        = ocrResult.ExtractedText;
        document.ExtractedText  = ocrResult.ExtractedText;
        document.IsOcrProcessed = true;
        document.ModifiedAt     = DateTime.UtcNow;
        document.Metadata     ??= new Dictionary<string, object>();
        document.Metadata["ocr_stage"]          = "text_extracted";
        document.Metadata["ocr_raw_length"]     = ocrResult.ExtractedText.Length;
        document.Metadata["ocr_processed_at"]   = DateTime.UtcNow.ToString("o");

        if (ocrResult.AverageConfidence > 0)
            document.Metadata["ocr_confidence"] = ocrResult.AverageConfidence;

        await db.SaveChangesAsync(ct);
        await ReIndexDocumentAsync(document, search, ct);

        _logger.LogInformation(
            "✅ Stage 'text_extracted' committed for {DocId} — frontend polls will resolve. " +
            "Continuing with LLM cleaning + enrichment in background…",
            document.Id);

        // ── 4. LLM cleaning ──────────────────────────────────────────────────
        await SetStageAsync(db, document, "llm_cleaning", ct);

        string cleanedText = ocrResult.ExtractedText;

        if (textCleaner != null)
        {
            _logger.LogInformation("🧹 Sending {Chars} chars to Ollama for cleaning…",
                ocrResult.ExtractedText.Length);

            try
            {
                cleanedText = await textCleaner.CleanOcrTextAsync(ocrResult.ExtractedText, ct);

                _logger.LogInformation("✅ Ollama cleaning: {Before} → {After} chars",
                    ocrResult.ExtractedText.Length, cleanedText.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM cleaning failed for {DocId} — keeping raw Tesseract text", document.Id);
                cleanedText = ocrResult.ExtractedText;
            }
        }

        // ── 5. Date extraction + AI enrichment ───────────────────────────────
        await EnrichAndSaveAsync(
            db, document, search, enricher, dateExtractor,
            cleanedText, "ocr_llm", ct);
    }

    // ── Core enrichment + final save ──────────────────────────────────────────

    /// <summary>
    /// Runs date extraction and AI metadata enrichment, then saves and re-indexes.
    /// Shared between the native-text shortcut and the full OCR pipeline.
    /// </summary>
    private async Task EnrichAndSaveAsync(
        GedDbContext db,
        DocumentEntity document,
        ISearchService search,
        OcrMetadataEnrichmentService? enricher,
        DocumentDateExtractor? dateExtractor,
        string textToAnalyze,
        string enrichmentSource,
        CancellationToken ct)
    {
        document.Metadata ??= new Dictionary<string, object>();

        // ── 5a. Date extraction ───────────────────────────────────────────────
        if (dateExtractor != null && document.DocumentDate == null)
        {
            try
            {
                var dateInfo = await dateExtractor.ExtractDocumentDateAsync(
                    textToAnalyze, document.FileName, document.Category ?? "Other", ct);

                if (dateInfo?.DocumentDate != null && dateInfo.Confidence >= DateConfidenceThreshold)
                {
                    document.DocumentDate = DateTime.SpecifyKind(dateInfo.DocumentDate.Value, DateTimeKind.Utc);

                    document.Metadata["extracted_date"]  = document.DocumentDate.Value.ToString("yyyy-MM-dd");
                    document.Metadata["date_confidence"] = dateInfo.Confidence;
                    document.Metadata["date_type"]       = dateInfo.DateType;
                    document.Metadata["date_source"]     = enrichmentSource;

                    _logger.LogInformation("✅ DocumentDate set: {Date} (conf={Conf:F2})",
                        document.DocumentDate.Value.ToString("yyyy-MM-dd"), dateInfo.Confidence);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Date extraction failed for {DocId}", document.Id);
            }
        }

        // ── 5b. AI tag + description enrichment ───────────────────────────────
        if (enricher != null)
        {
            try
            {
                var enrichResult = await enricher.EnrichAsync(
                    textToAnalyze,
                    document.FileName,
                    document.Category ?? "Other",
                    ct);

                if (enrichResult != null)
                {
                    // Merge AI tags with existing keyword tags (keep best of both)
                    var mergedTags = new HashSet<string>(
                        document.Tags ?? new List<string>(),
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var tag in enrichResult.Tags)
                        mergedTags.Add(tag);

                    document.Tags = mergedTags
                        .Where(t => t.Length > 1)
                        .OrderBy(t => t)
                        .Take(15)
                        .ToList();

                    // Overwrite description only if AI produced something meaningful
                    if (!string.IsNullOrWhiteSpace(enrichResult.Description))
                        document.Description = enrichResult.Description;

                    document.Metadata["enrichment_source"] = enrichmentSource;

                    _logger.LogInformation(
                        "✅ AI enrichment applied: {TagCount} tags, desc={DescLen} chars",
                        document.Tags.Count, document.Description?.Length ?? 0);
                }
                else
                {
                    _logger.LogInformation(
                        "ℹ️  AI enrichment returned null for {DocId} — keeping keyword tags",
                        document.Id);

                    // Fallback: regenerate tags from OCR text using keyword matching
                    ApplyKeywordTagFallback(document, textToAnalyze);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI enrichment threw for {DocId} — applying keyword fallback", document.Id);
                ApplyKeywordTagFallback(document, textToAnalyze);
            }
        }
        else
        {
            // No enricher registered — still do keyword tags from OCR text
            ApplyKeywordTagFallback(document, textToAnalyze);
        }

        // ── 5c. Description fallback from extracted text ───────────────────────
        // If description is still generic/empty after enrichment, extract from text
        if (IsGenericDescription(document.Description, document.FileName) &&
            !string.IsNullOrWhiteSpace(textToAnalyze))
        {
            var lines = textToAnalyze
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => l.Length > 20)
                .Take(3)
                .ToList();

            if (lines.Any())
            {
                var desc = string.Join(" ", lines);
                document.Description = desc.Length > 200 ? desc[..197] + "..." : desc;
                _logger.LogInformation("📝 Description set from extracted text ({Len} chars)", document.Description.Length);
            }
        }

        // ── 6. Final save ─────────────────────────────────────────────────────
        document.ExtractedText                  = textToAnalyze;
        document.ModifiedAt                     = DateTime.UtcNow;
        document.Metadata["ocr_cleaned_length"] = textToAnalyze.Length;
        document.Metadata["ocr_stage"]          = "completed";

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "💾 Enrichment complete for {DocId}: tags=[{Tags}], date={Date}",
            document.Id,
            string.Join(", ", document.Tags?.Take(5) ?? Array.Empty<string>()),
            document.DocumentDate?.ToString("yyyy-MM-dd") ?? "none");

        await ReIndexDocumentAsync(document, search, ct);
    }

    // ── Keyword tag fallback ──────────────────────────────────────────────────

    private static void ApplyKeywordTagFallback(DocumentEntity document, string text)
    {
        var tags = new HashSet<string>(
            document.Tags ?? new List<string>(),
            StringComparer.OrdinalIgnoreCase);

        var keywords = new[]
        {
            "invoice", "contract", "agreement", "report", "proposal",
            "confidential", "draft", "final", "signed", "approved",
            "budget", "payment", "license", "legal", "nda", "schedule",
            "publishing", "royalty", "author", "copyright", "manuscript",
            "real estate", "property", "sale", "mortgage", "lease",
            "medical", "patient", "diagnosis", "prescription",
            "employment", "salary", "benefits", "termination",
            "project", "timeline", "milestone", "deliverable"
        };

        var lower = text.ToLower();
        foreach (var kw in keywords)
            if (lower.Contains(kw)) tags.Add(kw.Replace(" ", "-"));

        var yearMatch = Regex.Match(text, @"\b(20\d{2})\b");
        if (yearMatch.Success) tags.Add(yearMatch.Value);

        document.Tags = tags.Where(t => t.Length > 1).OrderBy(t => t).Take(15).ToList();
    }

    private static bool IsGenericDescription(string? description, string fileName)
    {
        if (string.IsNullOrWhiteSpace(description)) return true;
        var d = description.Trim().ToLower();
        if (d.StartsWith("document:")) return true;
        var baseName = Path.GetFileNameWithoutExtension(fileName).ToLower();
        if (!string.IsNullOrEmpty(baseName) && d.Contains(baseName) && d.Length < 80) return true;
        return false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SetStageAsync(
        GedDbContext db,
        DocumentEntity document,
        string stage,
        CancellationToken ct)
    {
        try
        {
            document.Metadata ??= new Dictionary<string, object>();
            document.Metadata["ocr_stage"] = stage;
            document.ModifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            _logger.LogDebug("📍 OCR stage → '{Stage}' for {DocId}", stage, document.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist ocr_stage='{Stage}' for {DocId}", stage, document.Id);
        }
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
            _logger.LogInformation("🔍 Re-indexed {DocId} (stage={Stage})",
                document.Id, document.Metadata?.GetValueOrDefault("ocr_stage") ?? "?");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-index {DocId}", document.Id);
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
                doc.Metadata["ocr_stage"]     = "failed";
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