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
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GED.Infrastructure.Services;

/// <summary>
/// Background worker that consumes OCR jobs from RabbitMQ and processes them asynchronously.
/// 
/// <para>
/// This worker handles the asynchronous enrichment phase that runs after
/// <see cref="DocumentIngestionPipeline"/> completes the initial text extraction.
///
/// <see cref="DocumentIngestionPipeline"/> runs during upload (fast, synchronous).
/// This worker runs after upload (slower, asynchronous via RabbitMQ).
/// </para>
/// 
/// <para>
/// Decision logic per document:
/// <list type="bullet">
///   <item>
///     <term>Native-text PDF (>= 100 extracted chars)</term>
///     <description>
///       OCR is skipped entirely. Document is indexed immediately and enriched
///       asynchronously with LLM cleaning, date extraction, and AI metadata.
///     </description>
///   </item>
///   <item>
///     <term>Scanned PDF or image</term>
///     <description>
///       OCR runs (ocrmypdf for PDFs, direct Tesseract for images).
///       Followed by LLM text cleaning, date extraction, and AI metadata.
///     </description>
///   </item>
/// </list>
/// </para>
/// 
/// <para>
/// Pipeline stages (each stage is persisted to DB so the frontend can track progress):
/// <list type="number">
///   <item>
///     <term>Tesseract OCR</term>
///     <description>
///       Extracts text from image/scanned PDF. Sets IsOcrProcessed = true.
///       Frontend polling resolves here — no more 60-second waits.
///     </description>
///   </item>
///   <item>
///     <term>LLM text cleaning</term>
///     <description>
///       Uses Ollama to fix common OCR artifacts (broken words, misread chars).
///     </description>
///   </item>
///   <item>
///     <term>Date extraction</term>
///     <description>
///       Identifies the primary document date with confidence score.
///     </description>
///   </item>
///   <item>
///     <term>AI tag + description</term>
///     <description>
///       Generates metadata from cleaned text.
///     </description>
///   </item>
///   <item>
///     <term>Final save + re-index</term>
///     <description>
///       Persists enriched data and updates search index.
///     </description>
///   </item>
/// </list>
/// </para>
/// 
/// <para>
/// RabbitMQ.Client v7 compatibility notes:
/// <list type="bullet">
///   <item>
///     <term>ReceivedAsync exceptions</term>
///     <description>
///       Any unhandled exception permanently silences the consumer without closing the channel.
///       Fix: force-close connection on consumer error to trigger reconnect loop.
///     </description>
///   </item>
///   <item>
///     <term>Connection shutdown handling</term>
///     <description>
///       Uses TaskCompletionSource tied to ConnectionShutdownAsync for reliable
///       "connection is dead" signal.
///     </description>
///   </item>
/// </list>
/// </para>
/// </summary>
public class OcrWorkerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OcrWorkerService> _logger;

    /// <summary>
    /// RabbitMQ hostname for connection.
    /// </summary>
    private readonly string _rabbitHost;

    /// <summary>
    /// RabbitMQ username for authentication.
    /// </summary>
    private readonly string _rabbitUser;

    /// <summary>
    /// RabbitMQ password for authentication.
    /// </summary>
    private readonly string _rabbitPass;

    /// <summary>
    /// Name of the OCR job queue.
    /// </summary>
    private const string QueueName = "ocr-queue";

    /// <summary>
    /// Maximum connection retry attempts before waiting.
    /// </summary>
    private const int MaxRetries = 5;

    /// <summary>
    /// Base delay between retry attempts (multiplied by attempt number).
    /// </summary>
    private const int RetryDelayMs = 5000;

    /// <summary>
    /// Minimum confidence threshold for accepting extracted date.
    /// Read from configuration (OCR:DateConfidenceThreshold), defaults to 0.7.
    /// </summary>
    private readonly float _dateConfidenceThreshold;

    /// <summary>
    /// Minimum characters of native text to skip OCR processing.
    /// </summary>
    /// <remarks>
    /// Skip OCR only if the PDF has sufficient native text.
    /// 1500 chars ≈ roughly 1 page of dense text. This threshold was raised
    /// from 300 because headers/footers alone can exceed that threshold
    /// on a mostly-scanned document.
    /// </remarks>
    private const int NativeTextMinChars = 1500;

    /// <summary>
    /// Whether ResourceSaver mode is enabled.
    /// </summary>
    private readonly bool _resourceSaverEnabled;

    /// <summary>
    /// Initializes a new instance of <see cref="OcrWorkerService"/>.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scoped dependencies.</param>
    /// <param name="logger">Logger for worker events.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="rabbitHost">RabbitMQ hostname.</param>
    /// <param name="rabbitUser">RabbitMQ username.</param>
    /// <param name="rabbitPass">RabbitMQ password.</param>
    public OcrWorkerService(
        IServiceProvider serviceProvider,
        ILogger<OcrWorkerService> logger,
        IConfiguration configuration,
        string rabbitHost = "localhost",
        string rabbitUser = "admin",
        string rabbitPass = "admin123")
    {
        _serviceProvider = serviceProvider;
        _logger          = logger;
        _resourceSaverEnabled = configuration.GetValue<bool>("ResourceSaver:Enabled", false);
        _rabbitHost      = rabbitHost;
        _rabbitUser      = rabbitUser;
        _rabbitPass      = rabbitPass;
        _dateConfidenceThreshold = configuration.GetValue<float>("OCR:DateConfidenceThreshold", 0.7f);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_resourceSaverEnabled)
        {
            _logger.LogInformation("🔄 OCR Worker starting in ResourceSaver mode (prefetch=1, sequential LLM)");
        }
        else
        {
            _logger.LogInformation("🔄 OCR Worker starting in standard mode (prefetch=4, parallel LLM)");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            IConnection? connection = null;

            // Connect with exponential backoff retries
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
                    // TCS that fires when the connection dies for ANY reason
                    // This is the correct v7 pattern for detecting connection loss
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
                        // Messages that fail processing go to DLQ for manual inspection
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

                        // Main queue with DLX configuration
                        await channel.QueueDeclareAsync(
                            queue:      QueueName,
                            durable:    true,
                            exclusive:  false,
                            autoDelete: false,
                            arguments: new Dictionary<string, object?>
                            {
                                ["x-dead-letter-exchange"]    = DlxName,
                                ["x-dead-letter-routing-key"] = QueueName,
                                ["x-message-ttl"]             = 3_600_000,  // 1 hour TTL
                                ["x-max-length"]              = 1000       // Max 1000 messages
                            },
                            cancellationToken: stoppingToken);

                        // Process multiple messages concurrently for better throughput
                        // prefetchCount=4 means up to 4 messages are delivered before ack
                        // Each message is processed in parallel by the async consumer handler
                        // When ResourceSaver is enabled, prefetch=1 to prevent Ollama resource exhaustion
                        var prefetchCount = _resourceSaverEnabled ? (ushort)1 : (ushort)4;
                        await channel.BasicQosAsync(
                            prefetchSize:  0,
                            prefetchCount: prefetchCount,
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
                                // Shutdown in progress — requeue for other workers
                                _logger.LogInformation("OCR job cancelled during shutdown — nacking for requeue");
                                ackSent = await SafeNackAsync(channel, deliveryTag, requeue: true, ackSent);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "❌ OCR job failed for document {DocId}", msg?.DocumentId);

                                // Nack first — if the DB call below also fails we still want
                                // the message dead-lettered rather than stuck unacked
                                ackSent = await SafeNackAsync(channel, deliveryTag, requeue: false, ackSent);

                                if (msg != null)
                                    await MarkOcrFailedAsync(msg.DocumentId, ex.Message, stoppingToken);

                                // v7 FIX: Force-close connection to trigger reconnect loop
                                // In v7, unhandled exceptions in ReceivedAsync permanently silence the consumer
                                _logger.LogWarning("⚠️ Forcing connection close to restart consumer (v7 safety)");
                                try { await connection.CloseAsync(); } catch { /* best effort */ }
                            }
                        };

                        // Handle silent consumer unregistration (e.g., channel-level protocol error)
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

                        // Wait until connection dies OR host stops
                        // IMPORTANT: Do NOT use connection.CloseAsync() here — it closes immediately
                        // Task.WhenAny returns when either stoppingToken fires or connectionClosed.Task fires
                        try
                        {
                            await Task.WhenAny(
                                Task.Delay(Timeout.Infinite, stoppingToken),
                                connectionClosed.Task);
                        }
                        catch (OperationCanceledException)
                        {
                            // stoppingToken fired — graceful shutdown
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
    // Prevents exceptions from escaping the consumer handler (v7 bug mitigation)
    // Returns true if ack/nack was sent successfully (or already sent)

    /// <summary>
    /// Safely acknowledges a message, handling channel closure gracefully.
    /// </summary>
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

    /// <summary>
    /// Safely negatively acknowledges a message, handling channel closure gracefully.
    /// </summary>
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

    /// <summary>
    /// Processes a single OCR job through the full enrichment pipeline.
    /// </summary>
    private async Task ProcessOcrJobAsync(OcrJobMessage message, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();

        var db            = scope.ServiceProvider.GetRequiredService<GedDbContext>();
        var ocrService    = scope.ServiceProvider.GetRequiredService<IOcrService>();
        var search        = scope.ServiceProvider.GetRequiredService<ISearchService>();
        var textCleaner   = scope.ServiceProvider.GetService<OcrTextCleaningService>();
        var dateExtractor = scope.ServiceProvider.GetService<DocumentDateExtractor>();
        var enricher      = scope.ServiceProvider.GetService<OcrMetadataEnrichmentService>();

        var correlationId = message.CorrelationId ?? Guid.NewGuid().ToString("N")[..12];
        
        // Add correlation ID to logging scope for distributed tracing
        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            var document = await db.Documents
                .FirstOrDefaultAsync(d => d.Id == message.DocumentId, ct);
            
            if (document == null)
            {
                _logger.LogWarning("Document {DocumentId} not found — skipping", message.DocumentId);
                return;
            }

            var jobStartTime = DateTime.UtcNow;

            _logger.LogInformation(
                "🖼️  OCR pipeline: docId={DocId}, type={Type}, category={Cat}, hasNativeText={HasText} ({NativeChars} chars)",
                document.Id, document.ContentType, document.Category,
                !string.IsNullOrWhiteSpace(document.ExtractedText),
                document.ExtractedText?.Length ?? 0);

            var pipelineId = $"ocr-{message.DocumentId.ToString()[..8]}";

        await SetStageAsync(db, document, "processing", ct);
        await RecordHistoryAsync(db, document.Id, "started", "success", "OCR job received from queue", null, ct);

        // Check if document already has searchable native text (skip OCR)
        bool hasNativeText = !string.IsNullOrWhiteSpace(document.ExtractedText)
                             && document.ExtractedText.Trim().Length >= NativeTextMinChars;
        bool fileExists = File.Exists(document.FilePath);

        if (hasNativeText && document.ContentType == "application/pdf")
        {
            // Native text available — index immediately and enrich asynchronously
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

            // Index immediately with raw native text — document is now searchable
            await ReIndexDocumentAsync(document, search, ct);

            // Continue with LLM enrichment in parallel
            DocumentDateInfo? nativeDateInfo = null;
            OcrMetadataEnrichmentService.EnrichmentResult? nativeEnrichResult = null;

            var nativeDateTask   = dateExtractor != null && document.DocumentDate == null
                ? dateExtractor.ExtractDocumentDateAsync(document.ExtractedText!, document.FileName, document.Category ?? "Other", ct)
                : Task.FromResult<DocumentDateInfo?>(null);

            var nativeEnrichTask = enricher != null
                ? enricher.EnrichAsync(document.ExtractedText!, document.FileName, document.Category ?? "Other", ct)
                : Task.FromResult<OcrMetadataEnrichmentService.EnrichmentResult?>(null);

            try { await Task.WhenAll(nativeDateTask, nativeEnrichTask); } catch { }
            if (nativeDateTask.IsCompletedSuccessfully)    nativeDateInfo    = nativeDateTask.Result;
            if (nativeEnrichTask.IsCompletedSuccessfully) nativeEnrichResult = nativeEnrichTask.Result;

            await EnrichAndSaveAsync(
                db, document, search, nativeEnrichResult, nativeDateInfo,
                document.ExtractedText!, "native_text_llm", scope, jobStartTime, ct);

            return;
        }

        if (!fileExists)
        {
            _logger.LogWarning("File missing at {Path} — marking OCR failed", document.FilePath);
            await MarkOcrFailedAsync(document.Id, "File not found on disk", ct);
            return;
        }

        _logger.LogInformation("🔍 Starting OCR for {DocId}…", document.Id);

        bool isImageUpload = document.ContentType.StartsWith("image/");
        var ocrStartTime = DateTime.UtcNow;

        OcrResult ocrResult;
        try
        {
            using var fileStream = File.OpenRead(document.FilePath);
            if (isImageUpload)
            {
                // Use ocrmypdf directly on images - it handles conversion internally
                var imageOcr = scope.ServiceProvider.GetRequiredService<ImageOcrService>();
                
                var tempDir = Path.GetTempPath();
                var jobId = Guid.NewGuid().ToString("N");
                var outputPdfPath = Path.Combine(tempDir, $"ocr_out_{jobId}.pdf");
                
                var processedPdfPath = await imageOcr.ProcessImageAsync(
                    document.FilePath, outputPdfPath, message.Language ?? "eng", ct);
                
                // Now extract text from the OCR-processed PDF
                ocrResult = await ocrService.ProcessDocumentAsync(
                    message.DocumentId, File.OpenRead(processedPdfPath), message.Language ?? "eng", ct);
                
                // Clean up temp PDF
                try { File.Delete(processedPdfPath); } catch { }
            }
            else
            {
                // ocrmypdf path for PDFs and other formats
                ocrResult = await ocrService.ProcessDocumentAsync(
                    message.DocumentId, fileStream, message.Language ?? "eng", ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ OCR service threw for document {DocId}", document.Id);
            await MarkOcrFailedAsync(document.Id, ex.Message, ct);
            return;
        }

        if (!ocrResult.Success || string.IsNullOrWhiteSpace(ocrResult.ExtractedText))
        {
            var errMsg = ocrResult.ErrorMessage ?? "OCR returned no text";
            _logger.LogWarning("OCR returned no text for {DocId}: {Error}", message.DocumentId, errMsg);
            await MarkOcrFailedAsync(document.Id, errMsg, ct);
            return;
        }

        // Store OCR results
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

        // OCR quality indicator based on chars-per-page
        // < 100 chars/page = likely poor scan, > 500 = good readable text
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

        await db.SaveChangesAsync(ct);
        await RecordHistoryAsync(db, document.Id, "ocr", "success", 
            $"Extracted {ocrResult.ExtractedText?.Length ?? 0} chars, quality: {ocrQuality}",
            (long)(DateTime.UtcNow - ocrStartTime).TotalMilliseconds, ct);
        await ReIndexDocumentAsync(document, search, ct);

        // ── Run Tika metadata extraction AFTER OCR ─────────────────────────────────
        // For scanned PDFs, we extract metadata from the OCR-processed PDF
        // For native PDFs that skipped OCR, this doesn't apply
        var tikaMetadata = new Dictionary<string, string>();
        var tikaStartTime = DateTime.UtcNow;
        try
        {
            var tikaService = scope.ServiceProvider.GetService<TikaTextExtractionService>();
            if (tikaService != null && !string.IsNullOrWhiteSpace(document.FilePath))
            {
                _logger.LogInformation("📄 Extracting metadata with Tika for {DocId}...", document.Id);
                
                // If this was a scanned document (went through OCR), use the processed file
                // Otherwise use the original file (for native PDFs that skipped OCR)
                string filePathToUse = document.FilePath;
                
                // Check if we have a temporary OCR output file - in current implementation,
                // the OCR service handles the output internally, so we use the original
                // The key is that we call Tika AFTER OCR completes
                
                tikaMetadata = await tikaService.ExtractMetadataAsync(filePathToUse, ct);
                
                if (tikaMetadata.Any())
                {
                    var (tikaCategory, tikaDescription) = TikaTextExtractionService.MapMetadataToFields(
                        tikaMetadata, document.FileName);
                    
                    // Apply category from Tika if not already set
                    if (!string.IsNullOrWhiteSpace(tikaCategory) && string.IsNullOrWhiteSpace(document.Category))
                    {
                        document.Category = tikaCategory;
                        document.Metadata["category_source"] = "tika";
                        _logger.LogInformation("✅ Category set from Tika: {Category}", tikaCategory);
                    }
                    
                    // Apply description from Tika if current description is generic
                    if (!string.IsNullOrWhiteSpace(tikaDescription) && 
                        IsGenericDescription(document.Description, document.FileName))
                    {
                        document.Description = tikaDescription;
                        document.Metadata["description_source"] = "tika";
                        _logger.LogInformation("✅ Description set from Tika: {DescLen} chars", 
                            tikaDescription.Length);
                    }
                    
                    // Store Tika metadata in document metadata
                    foreach (var kvp in tikaMetadata.Take(20)) // Limit to avoid bloat
                    {
                        document.Metadata[$"tika_{kvp.Key}"] = kvp.Value;
                    }
                    
                    await db.SaveChangesAsync(ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tika metadata extraction failed for {DocId}", document.Id);
            await RecordHistoryAsync(db, document.Id, "tika", "failed", $"Metadata extraction failed: {ex.Message}", null, ct);
        }

        _logger.LogInformation(
            "✅ Stage 'text_extracted' committed for {DocId} — continuing with LLM cleaning + enrichment…",
            document.Id);

        await SetStageAsync(db, document, "llm_cleaning", ct);
        await RecordHistoryAsync(db, document.Id, "tika", "success", 
            $"Category: {document.Category ?? "none"}, extracted {tikaMetadata.Count} metadata fields",
            (long)(DateTime.UtcNow - tikaStartTime).TotalMilliseconds, ct);

        var llmStartTime = DateTime.UtcNow;

        string cleanedText = ocrResult.ExtractedText!;
        DocumentDateInfo? dateInfo = null;
        OcrMetadataEnrichmentService.EnrichmentResult? enrichResult = null;

        // Step 1: Clean OCR text first (must be sequential)
        try
        {
            if (textCleaner != null)
            {
                _logger.LogInformation("🧹 Sending {Chars} chars to Ollama for cleaning…",
                    ocrResult.ExtractedText!.Length);
                cleanedText = await textCleaner.CleanOcrTextAsync(ocrResult.ExtractedText, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM text cleaning failed for {DocId} — using raw OCR text", document.Id);
        }

        // Step 2: Date extraction and metadata enrichment can run in parallel on cleaned text
        Task<DocumentDateInfo?>? dateTask = null;
        Task<OcrMetadataEnrichmentService.EnrichmentResult?>? enrichTask = null;

        try
        {
            // Date extraction (only if not already extracted)
            if (dateExtractor != null && document.DocumentDate == null)
            {
                _logger.LogInformation("📅 Extracting document date with Ollama…");
                dateTask = dateExtractor.ExtractDocumentDateAsync(
                    cleanedText, document.FileName, document.Category ?? "Other", ct);
            }

            // Metadata enrichment
            if (enricher != null)
            {
                _logger.LogInformation("🏷️ Enriching document metadata with Ollama…");
                enrichTask = enricher.EnrichAsync(
                    cleanedText, document.FileName, document.Category ?? "Other", ct);
            }

            // When ResourceSaver is enabled, run sequentially to prevent Ollama resource exhaustion
            if (_resourceSaverEnabled)
            {
                if (dateTask != null)
                    dateInfo = await dateTask;
                if (enrichTask != null)
                    enrichResult = await enrichTask;
            }
            else
            {
                // Run in parallel for better performance
                var allTasks = new List<Task>();
                if (dateTask != null) allTasks.Add(dateTask);
                if (enrichTask != null) allTasks.Add(enrichTask);

                if (allTasks.Count > 0)
                {
                    await Task.WhenAll(allTasks);
                }

                dateInfo = dateTask?.IsCompletedSuccessfully == true ? dateTask.Result : null;
                enrichResult = enrichTask?.IsCompletedSuccessfully == true ? enrichTask.Result : null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "One or more LLM calls failed for {DocId}", document.Id);
        }

        // Record LLM processing completion
        await RecordHistoryAsync(db, document.Id, "llm_enrichment", "success", 
            $"Text cleaned: {cleanedText.Length} chars, tags: {enrichResult?.Tags?.Count ?? 0}, date: {dateInfo?.DocumentDate?.ToString("yyyy-MM-dd") ?? "none"}",
            (long)(DateTime.UtcNow - llmStartTime).TotalMilliseconds, ct);

        // Final enrichment (date + tags + description)
        await EnrichAndSaveAsync(
            db, document, search, enrichResult, dateInfo,
            cleanedText!, "ocr_llm", scope, jobStartTime, ct);
        }
    }

    /// <summary>
    /// Performs final enrichment (date extraction, AI tags, description) and persists results.
    /// </summary>
    private async Task EnrichAndSaveAsync(
        GedDbContext db,
        DocumentEntity document,
        ISearchService search,
        OcrMetadataEnrichmentService.EnrichmentResult? enrichResult,
        DocumentDateInfo? dateInfo,
        string textToAnalyze,
        string enrichmentSource,
        IServiceScope scope,
        DateTime jobStartTime,
        CancellationToken ct)
    {
        document.Metadata ??= new Dictionary<string, object>();
        var chunkStartTime = DateTime.UtcNow;

        // Apply document date
        if (dateInfo?.DocumentDate != null && dateInfo.Confidence >= _dateConfidenceThreshold)
        {
            document.DocumentDate = DateTime.SpecifyKind(dateInfo.DocumentDate.Value, DateTimeKind.Utc);
            document.DateConfidenceScore = dateInfo.Confidence;
            document.Metadata["extracted_date"]  = document.DocumentDate.Value.ToString("yyyy-MM-dd");
            document.Metadata["date_confidence"] = dateInfo.Confidence;
            document.Metadata["date_type"]       = dateInfo.DateType;
            document.Metadata["date_source"]     = enrichmentSource;

            _logger.LogInformation("✅ DocumentDate set: {Date} (conf={Conf:F2})",
                document.DocumentDate.Value.ToString("yyyy-MM-dd"), dateInfo.Confidence);
        }

        // Apply AI tag and description enrichment
        if (enrichResult != null)
        {
            // Merge tags: default tags (category, extension) + enriched tags
            var mergedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add default tags: category
            if (!string.IsNullOrWhiteSpace(document.Category))
                mergedTags.Add(document.Category.ToLower());

            // Add default tags: file extension
            if (!string.IsNullOrWhiteSpace(document.FileName))
            {
                var ext = Path.GetExtension(document.FileName).TrimStart('.').ToLower();
                if (!string.IsNullOrWhiteSpace(ext)) mergedTags.Add(ext);
            }

            // Add enriched tags from LLM
            foreach (var tag in enrichResult.Tags)
                mergedTags.Add(tag);

            document.Tags = mergedTags
                .Where(t => t.Length > 1)
                .OrderBy(t => t)
                .Take(15)
                .ToList();

            document.Metadata["enrichment_source"] = enrichmentSource;

            _logger.LogInformation(
                "✅ AI enrichment applied: {TagCount} tags for document {DocId}",
                document.Tags.Count, document.Id);
        }
        else
        {
            _logger.LogInformation(
                "ℹ️  AI enrichment returned null for {DocId} — keeping keyword tags", document.Id);
            ApplyKeywordTagFallback(document, textToAnalyze);
        }

        // Fallback description if current description is generic
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

        // Update status to Indexed now that OCR and enrichment are complete
        document.Status = DocumentStatus.Indexed;

        var indexingStartTime = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "💾 Enrichment complete for {DocId}: tags=[{Tags}], date={Date}",
            document.Id,
            string.Join(", ", document.Tags?.Take(5) ?? Array.Empty<string>()),
            document.DocumentDate?.ToString("yyyy-MM-dd") ?? "none");

        // Re-index with enriched data
        await ReIndexDocumentAsync(document, search, ct);
        await RecordHistoryAsync(db, document.Id, "indexing", "success", 
            "Document indexed in OpenSearch", (long)(DateTime.UtcNow - indexingStartTime).TotalMilliseconds, ct);

        // Chunk-level indexing for RAG
        int chunkCount = 0;
        var ragStartTime = DateTime.UtcNow;
        try
        {
            var chunker    = scope.ServiceProvider.GetRequiredService<DocumentChunkingService>();
            var openSearch = scope.ServiceProvider.GetRequiredService<OpenSearchService>();

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
            chunkCount = chunks.Count;
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
            await RecordHistoryAsync(db, document.Id, "rag_chunks", "failed", 
                $"Chunk indexing failed: {ex.Message}", null, ct);
        }

        // Record RAG completion
        await RecordHistoryAsync(db, document.Id, "rag_chunks", "success", 
            $"Indexed {chunkCount} chunks for RAG", (long)(DateTime.UtcNow - ragStartTime).TotalMilliseconds, ct);

        // Final completion record
        await RecordHistoryAsync(db, document.Id, "completed", "success", 
            $"Document fully processed: {document.Tags?.Count ?? 0} tags, date: {document.DocumentDate?.ToString("yyyy-MM-dd") ?? "none"}",
            (long)(DateTime.UtcNow - jobStartTime).TotalMilliseconds, ct);
    }

    /// <summary>
    /// Applies keyword-based tag fallback when LLM enrichment is unavailable.
    /// Also adds default tags (category, file extension) if not already present.
    /// </summary>
    private static void ApplyKeywordTagFallback(DocumentEntity document, string text)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add default tags: category
        if (!string.IsNullOrWhiteSpace(document.Category))
            tags.Add(document.Category.ToLower());

        // Add default tags: file extension
        if (!string.IsNullOrWhiteSpace(document.FileName))
        {
            var ext = Path.GetExtension(document.FileName).TrimStart('.').ToLower();
            if (!string.IsNullOrWhiteSpace(ext)) tags.Add(ext);
        }

        // Add keywords from text
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

        // Extract year as a tag
        var yearMatch = Regex.Match(text, @"\b(20\d{2})\b");
        if (yearMatch.Success) tags.Add(yearMatch.Value);

        document.Tags = tags.Where(t => t.Length > 1).OrderBy(t => t).Take(15).ToList();
    }

    /// <summary>
    /// Checks if a description is generic (just filename or "Document:").
    /// </summary>
    private static bool IsGenericDescription(string? description, string fileName)
    {
        if (string.IsNullOrWhiteSpace(description)) return true;
        var d = description.Trim().ToLower();
        if (d.StartsWith("document:")) return true;
        var baseName = Path.GetFileNameWithoutExtension(fileName).ToLower();
        if (!string.IsNullOrEmpty(baseName) && d.Contains(baseName) && d.Length < 80) return true;
        return false;
    }

    /// <summary>
    /// Updates the OCR stage in document metadata for frontend progress tracking.
    /// </summary>
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

    /// <summary>
    /// Records a processing history entry for timeline tracking.
    /// </summary>
    private async Task RecordHistoryAsync(
        GedDbContext db, Guid documentId, string stage, string status, 
        string? message = null, long? durationMs = null, CancellationToken ct = default)
    {
        try
        {
            var entry = new Infrastructure.Data.ProcessingHistory
            {
                Id          = Guid.NewGuid(),
                DocumentId  = documentId,
                Timestamp   = DateTime.UtcNow,
                Stage       = stage,
                Status      = status,
                Message     = message,
                DurationMs  = durationMs
            };
            db.ProcessingHistory.Add(entry);
            await db.SaveChangesAsync(ct);
            _logger.LogDebug("📜 History: {Stage} → {Status} for {DocId}", stage, status, documentId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record history for {Stage} on {DocId}", stage, documentId);
        }
    }

    /// <summary>
    /// Re-indexes a document in the search engine with current data.
    /// </summary>
    private async Task ReIndexDocumentAsync(
        DocumentEntity document, ISearchService search, CancellationToken ct)
    {
        try
        {
            // Calculate IsFullyProcessed: true only when OCR is done AND stage is "completed"
            var ocrStage = document.Metadata?.GetValueOrDefault("ocr_stage")?.ToString();
            var isFullyProcessed = document.IsOcrProcessed && ocrStage == "completed";
            
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
                IsOcrProcessed = document.IsOcrProcessed,
                IsFullyProcessed = isFullyProcessed
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

    /// <summary>
    /// Marks a document as OCR failed and stores the error message.
    /// </summary>
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
                
                await RecordHistoryAsync(db, documentId, "ocr", "failed", error, null, ct);
                await RecordHistoryAsync(db, documentId, "completed", "failed", $"Processing failed: {error}", null, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark OCR failed for {DocId}", documentId);
        }
    }
}

/// <summary>
/// Message format for OCR jobs received from RabbitMQ.
/// </summary>
public class OcrJobMessage
{
    /// <summary>
    /// Unique identifier for this OCR job.
/// </summary>
    public Guid JobId { get; set; }

    /// <summary>
    /// Document ID to process.
/// </summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// Language code(s) for OCR (e.g., "eng", "eng+fra+ara").
    /// </summary>
    public string Language { get; set; } = "eng";

    /// <summary>
    /// Correlation ID for distributed tracing across async workers.
    /// </summary>
    public string? CorrelationId { get; set; }
}
