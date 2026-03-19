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
///   4. AI tag + description   → still ocr_stage = "llm_cleaning"
///   5. Final save + re-index  → ocr_stage = "completed"
///
/// RabbitMQ.Client v7 notes:
///   - ReceivedAsync runs on the library's internal dispatch loop.
///   - Any unhandled exception permanently silences the consumer without
///     closing the channel or connection, so the reconnect loop never fires.
///   - Fix: use a TaskCompletionSource tied to ConnectionShutdownAsync so
///     we have a reliable "connection is dead" signal, and force-close the
///     connection on any consumer error to trigger it.
/// </summary>
public class OcrWorkerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OcrWorkerService> _logger;
    private readonly string _rabbitHost;
    private readonly string _rabbitUser;
    private readonly string _rabbitPass;

    private const string QueueName    = "ocr-queue";
    private const int    MaxRetries   = 5;
    private const int    RetryDelayMs = 5000;

    private const float DateConfidenceThreshold = 0.3f;
    private const int   NativeTextMinChars      = 300;

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

            // ── Connect with retries ──────────────────────────────────────────
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var factory = new ConnectionFactory
                    {
                        HostName                   = _rabbitHost,
                        UserName                   = _rabbitUser,
                        Password                   = _rabbitPass,
                        AutomaticRecoveryEnabled   = false, // we handle reconnect ourselves
                        RequestedHeartbeat         = TimeSpan.FromSeconds(60),
                        RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
                    };

                    connection = await factory.CreateConnectionAsync(stoppingToken);
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
                await using (connection)
                {
                    // ── TCS that fires when the connection dies for ANY reason ──────
                    // This is the correct v7 pattern. CloseAsync() is not a blocking
                    // wait — it closes immediately and returns. We need an event-based
                    // signal instead.
                    var connectionClosed = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);

                    connection.ConnectionShutdownAsync += (_, args) =>
                    {
                        _logger.LogWarning(
                            "🔌 RabbitMQ connection shutdown: {Reason}", args.ReplyText);
                        connectionClosed.TrySetResult();
                        return Task.CompletedTask;
                    };

                    var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                    await using (channel)
                    {
                        // ── Dead-Letter Queue Setup ───────────────────────────────
                        const string DlxName = "ocr-dlx";
                        const string DlqName = "ocr-dead-letter";

                        await channel.ExchangeDeclareAsync(
                            exchange:          DlxName,
                            type:              ExchangeType.Direct,
                            durable:           true,
                            autoDelete:        false,
                            cancellationToken: stoppingToken);

                        await channel.QueueDeclareAsync(
                            queue:             DlqName,
                            durable:           true,
                            exclusive:         false,
                            autoDelete:        false,
                            cancellationToken: stoppingToken);

                        await channel.QueueBindAsync(
                            DlqName, DlxName,
                            routingKey:        QueueName,
                            cancellationToken: stoppingToken);

                        await channel.QueueDeclareAsync(
                            queue:      QueueName,
                            durable:    true,
                            exclusive:  false,
                            autoDelete: false,
                            arguments: new Dictionary<string, object?>
                            {
                                ["x-dead-letter-exchange"]    = DlxName,
                                ["x-dead-letter-routing-key"] = QueueName,
                                ["x-message-ttl"]             = 3_600_000,
                                ["x-max-length"]              = 1000
                            },
                            cancellationToken: stoppingToken);

                        await channel.BasicQosAsync(
                            prefetchSize:  0,
                            prefetchCount: 1,
                            global:        false,
                            cancellationToken: stoppingToken);

                        _logger.LogInformation("📥 OCR Worker listening on queue '{Queue}'", QueueName);

                        var consumer = new AsyncEventingBasicConsumer(channel);

                        consumer.ReceivedAsync += async (_, ea) =>
                        {
                            var deliveryTag = ea.DeliveryTag;
                            OcrJobMessage? msg = null;
                            bool ackSent = false;

                            try
                            {
                                var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                                msg = JsonSerializer.Deserialize<OcrJobMessage>(json,
                                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                                if (msg == null)
                                {
                                    _logger.LogWarning("Received null OCR message — discarding");
                                    ackSent = await SafeAckAsync(channel, deliveryTag, ackSent);
                                    return;
                                }

                                _logger.LogInformation("📄 OCR job received: jobId={JobId}, documentId={DocId}",
                                    msg.JobId, msg.DocumentId);

                                await ProcessOcrJobAsync(msg, stoppingToken);
                                ackSent = await SafeAckAsync(channel, deliveryTag, ackSent);

                                _logger.LogInformation("✅ OCR job {JobId} acked", msg.JobId);
                            }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                            {
                                _logger.LogInformation("OCR job cancelled during shutdown — nacking for requeue");
                                ackSent = await SafeNackAsync(channel, deliveryTag, requeue: true, ackSent);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "❌ OCR job failed for document {DocId}", msg?.DocumentId);

                                // Nack first — if the DB call below also fails we still want
                                // the message dead-lettered rather than stuck unacked.
                                ackSent = await SafeNackAsync(channel, deliveryTag, requeue: false, ackSent);

                                if (msg != null)
                                    await MarkOcrFailedAsync(msg.DocumentId, ex.Message, stoppingToken);

                                // ── v7 FIX ────────────────────────────────────────────────
                                // In v7, any unhandled exception in ReceivedAsync permanently
                                // silences the consumer without closing the channel or connection.
                                // connectionClosed.Task keeps blocking indefinitely.
                                //
                                // Fix: close the connection → fires ConnectionShutdownAsync
                                //      → TCS resolves → WhenAny unblocks → reconnect loop runs.
                                // ─────────────────────────────────────────────────────────
                                _logger.LogWarning("⚠️ Forcing connection close to restart consumer (v7 safety)");
                                try { await connection.CloseAsync(); } catch { /* best effort */ }
                            }
                        };

                        // Also catch the case where v7 silently unregisters the consumer
                        // without an exception (e.g. channel-level protocol error).
                        consumer.UnregisteredAsync += async (_, ea) =>
                        {
                            _logger.LogWarning(
                                "⚠️ OCR consumer unregistered (tag={Tag}) — forcing reconnect",
                                ea.ConsumerTags.FirstOrDefault() ?? "?");
                            try { await connection.CloseAsync(); } catch { /* best effort */ }
                        };

                        await channel.BasicConsumeAsync(
                            queue:             QueueName,
                            autoAck:           false,
                            consumer:          consumer,
                            cancellationToken: stoppingToken);

                        // ── Wait until the connection dies OR the host stops ───────
                        // IMPORTANT: Do NOT use connection.CloseAsync() here as a wait
                        // mechanism — it closes immediately and returns, causing the
                        // worker to loop-reconnect without ever processing messages.
                        //
                        // Task.WhenAny returns as soon as either:
                        //   (a) stoppingToken is cancelled  → graceful shutdown
                        //   (b) connectionClosed.Task fires  → connection dropped, reconnect
                        try
                        {
                            await Task.WhenAny(
                                Task.Delay(Timeout.Infinite, stoppingToken),
                                connectionClosed.Task);
                        }
                        catch (OperationCanceledException)
                        {
                            // stoppingToken fired — fall through to the check below
                        }

                        if (stoppingToken.IsCancellationRequested)
                        {
                            _logger.LogInformation("🛑 OCR Worker stopping gracefully");
                            return;
                        }

                        _logger.LogWarning("🔄 Connection lost — reconnecting in 5s…");
                        await Task.Delay(5_000, stoppingToken);
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

    // ── Safe Ack/Nack Helpers ─────────────────────────────────────────────────
    // Never let ack/nack throw out of the consumer handler — that would trigger
    // the v7 "silent consumer death" bug again.
    //
    // Returns true if the ack/nack was sent (or already sent before this call).
    // C# async methods cannot have ref parameters, so we return the new sent state.

    private async Task<bool> SafeAckAsync(IChannel channel, ulong deliveryTag, bool alreadySent)
    {
        if (alreadySent) return true;
        try
        {
            await channel.BasicAckAsync(deliveryTag, multiple: false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BasicAckAsync failed for tag {Tag} — channel likely closed", deliveryTag);
            return true; // treat as sent to prevent double-ack attempts
        }
    }

    private async Task<bool> SafeNackAsync(IChannel channel, ulong deliveryTag, bool requeue, bool alreadySent)
    {
        if (alreadySent) return true;
        try
        {
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: requeue);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BasicNackAsync failed for tag {Tag} — channel likely closed", deliveryTag);
            return true;
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
        var enricher      = scope.ServiceProvider.GetService<OcrMetadataEnrichmentService>();

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

        await SetStageAsync(db, document, "processing", ct);

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

            // ── Phase 1: index immediately with raw native text ───────────────
            // The document is now searchable (BM25) while LLM enrichment runs.
            // EnrichAndSaveAsync will do a partial _update once tags/date/summary are ready.
            await ReIndexDocumentAsync(document, search, ct);
            _logger.LogInformation(
                "🔍 Phase 1 index done for {DocId} (native text) — document is now searchable", document.Id);

            await EnrichAndSaveAsync(
                db, document, search, enricher, dateExtractor,
                document.ExtractedText!, "native_text_llm", scope, ct);

            return;
        }

        if (!fileExists)
        {
            _logger.LogWarning("File missing at {Path} — marking OCR failed", document.FilePath);
            await MarkOcrFailedAsync(document.Id, "File not found on disk", ct);
            return;
        }

        _logger.LogInformation("🔍 Starting Tesseract OCR for {DocId}…", document.Id);

        bool isImageUpload = TesseractDirectOcrService.SupportsContentType(document.ContentType);


        OcrResult ocrResult;
        try
        {
            using var fileStream = File.OpenRead(document.FilePath);
            if (isImageUpload)
            {
                // Direct Tesseract — no PDF intermediary needed for pure image files
                var directOcr = scope.ServiceProvider.GetRequiredService<TesseractDirectOcrService>();
                ocrResult = await directOcr.ProcessDocumentAsync(
                    message.DocumentId, fileStream, message.Language ?? "eng", ct);
            }
            else
            {
                // Existing ocrmypdf path for PDFs and other formats
                ocrResult = await ocrService.ProcessDocumentAsync(
                    message.DocumentId, fileStream, message.Language ?? "eng", ct);
            }
        }
        catch (Exception ex)
        {
            // OcrmyPdfOcrService now throws on non-zero exit codes so we catch here
            // and mark failed — this propagates up to the consumer which will nack
            // and force-close the connection to restart the consumer (v7 fix).
            _logger.LogError(ex, "❌ OCR service threw for document {DocId}", document.Id);
            await MarkOcrFailedAsync(document.Id, ex.Message, ct);
            return; // re-throw so the consumer handler nacks + triggers reconnect
        }

        _logger.LogInformation(
            "[{PipelineId}] Stage text_extracted: {Chars} chars",
            pipelineId, ocrResult.ExtractedText?.Length);

        _logger.LogInformation(
            "📝 Tesseract result: success={Ok}, chars={Chars}, confidence={Conf:F2}",
            ocrResult.Success, ocrResult.ExtractedText?.Length ?? 0, ocrResult.AverageConfidence);

        if (!ocrResult.Success || string.IsNullOrWhiteSpace(ocrResult.ExtractedText))
        {
            var errMsg = ocrResult.ErrorMessage ?? "OCR returned no text";
            _logger.LogWarning("OCR returned no text for {DocId}: {Error}", message.DocumentId, errMsg);
            await MarkOcrFailedAsync(document.Id, errMsg, ct);
            return;
        }

        document.OcrText        = ocrResult.ExtractedText;
        document.ExtractedText  = ocrResult.ExtractedText;
        document.IsOcrProcessed = true;
        document.ModifiedAt     = DateTime.UtcNow;
        document.Metadata     ??= new Dictionary<string, object>();
        document.Metadata["ocr_stage"]        = "text_extracted";
        document.Metadata["ocr_raw_length"]   = ocrResult.ExtractedText.Length;
        document.Metadata["ocr_processed_at"] = DateTime.UtcNow.ToString("o");

        
        if (ocrResult.AverageConfidence > 0)
            document.Metadata["ocr_confidence"] = ocrResult.AverageConfidence;

        // ── OCR quality indicator ─────────────────────────────────────────────────
        // chars-per-page is a cheap proxy for OCR quality:
        //   ocrmypdf doesn't expose per-word confidence without --pdf-renderer hocr,
        //   which adds significant processing overhead.
        //   < 100 chars/page = likely poor scan, > 500 = good readable text.
        float charsPerPage = ocrResult.PageCount > 0
            ? (float)(ocrResult.ExtractedText?.Length ?? 0) / ocrResult.PageCount
            : 0f;

        string ocrQuality = charsPerPage switch
        {
            > 500 => "good",
            > 100 => "fair",
            _     => "poor"
        };

        document.Metadata["ocr_quality"]          = ocrQuality;
        document.Metadata["ocr_chars_per_page"]   = (int)charsPerPage;
        document.Metadata["ocr_confidence_proxy"] = Math.Min(charsPerPage / 500f, 1.0f);

        _logger.LogInformation(
            "📊 OCR quality for {DocId}: {Quality} ({CharsPerPage} chars/page, proxy={Proxy:F2})",
            document.Id, ocrQuality, (int)charsPerPage, Math.Min(charsPerPage / 500f, 1.0f));
        // ─────────────────────────────────────────────────────────────────────────

        await db.SaveChangesAsync(ct);
        await ReIndexDocumentAsync(document, search, ct);

        _logger.LogInformation(
            "✅ Stage 'text_extracted' committed for {DocId} — frontend polls will resolve. " +
            "Continuing with LLM cleaning + enrichment…",
            document.Id);

        await SetStageAsync(db, document, "llm_cleaning", ct);

        string cleanedText = ocrResult.ExtractedText!;

        if (textCleaner != null)
        {
            _logger.LogInformation("🧹 Sending {Chars} chars to Ollama for cleaning…",
                ocrResult.ExtractedText!.Length);
            try
            {
                cleanedText = await textCleaner.CleanOcrTextAsync(ocrResult.ExtractedText, ct)
                    ?? ocrResult.ExtractedText;
                _logger.LogInformation("✅ Ollama cleaning: {Before} → {After} chars",
                    ocrResult.ExtractedText.Length, cleanedText.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM cleaning failed for {DocId} — keeping raw Tesseract text", document.Id);
                cleanedText = ocrResult.ExtractedText;
            }
        }

        await EnrichAndSaveAsync(
            db, document, search, enricher, dateExtractor,
            cleanedText!, "ocr_llm", scope, ct);
    }

    private async Task EnrichAndSaveAsync(
        GedDbContext db,
        DocumentEntity document,
        ISearchService search,
        OcrMetadataEnrichmentService? enricher,
        DocumentDateExtractor? dateExtractor,
        string textToAnalyze,
        string enrichmentSource,
        IServiceScope scope,
        CancellationToken ct)
    {
        document.Metadata ??= new Dictionary<string, object>();

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

        if (enricher != null)
        {
            try
            {
                var enrichResult = await enricher.EnrichAsync(
                    textToAnalyze, document.FileName, document.Category ?? "Other", ct);

                if (enrichResult != null)
                {
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
                        "ℹ️  AI enrichment returned null for {DocId} — keeping keyword tags", document.Id);
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
            ApplyKeywordTagFallback(document, textToAnalyze);
        }

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
        // ── Chunk-level indexing for RAG ─────────────────────────────────────────────
        try
        {
            var chunker    = scope.ServiceProvider.GetRequiredService<DocumentChunkingService>();
            var openSearch = scope.ServiceProvider.GetRequiredService<OpenSearchService>();

            // Build domain object inline — MapToDomain is in DocumentService, not here
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
                IsOcrProcessed = document.IsOcrProcessed
            };

            var chunks = chunker.ChunkText(document.Id, textToAnalyze);
            if (chunks.Any())
            {
                await openSearch.IndexChunksAsync(domainDoc, chunks, ct);
                _logger.LogInformation(
                    "✅ Chunk indexing complete for {DocId}: {Count} chunks",
                    document.Id, chunks.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Chunk indexing failed for {DocId} — RAG will use full-doc fallback", document.Id);
        }
    }

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

    private async Task SetStageAsync(
        GedDbContext db, DocumentEntity document, string stage, CancellationToken ct)
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
        DocumentEntity document, ISearchService search, CancellationToken ct)
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

    private async Task MarkOcrFailedAsync(Guid documentId, string error, CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db  = scope.ServiceProvider.GetRequiredService<GedDbContext>();
            var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
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