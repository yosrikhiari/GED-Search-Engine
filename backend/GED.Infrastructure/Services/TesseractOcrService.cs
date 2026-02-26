using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Logging;
using Tesseract;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using System.Text.Json;

namespace GED.Infrastructure.Services;

public class TesseractOcrService : IOcrService
{
    private readonly ILogger<TesseractOcrService> _logger;
    private readonly IMessageQueueService _messageQueue;
    private readonly string _tessDataPath;

    private readonly Dictionary<Guid, OcrJob> _jobCache = new();

    public TesseractOcrService(
        ILogger<TesseractOcrService> logger,
        IMessageQueueService messageQueue,
        string tessDataPath = "/usr/share/tesseract-ocr/5/tessdata")
    {
        _logger       = logger;
        _messageQueue = messageQueue;
        _tessDataPath = tessDataPath;
    }

    public async Task<Guid> QueueOcrJobAsync(
        Guid documentId,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var job = new OcrJob
        {
            Id         = Guid.NewGuid(),
            DocumentId = documentId,
            Status     = OcrStatus.Pending,
            CreatedAt  = DateTime.UtcNow,
            Language   = language ?? "eng"
        };

        _jobCache[job.Id] = job;

        await _messageQueue.PublishAsync("ocr-queue", new
        {
            JobId      = job.Id,
            DocumentId = documentId,
            Language   = language ?? "eng"
        }, cancellationToken);

        _logger.LogInformation("OCR job {JobId} queued for document {DocumentId}", job.Id, documentId);
        return job.Id;
    }

    public async Task<OcrResult> ProcessDocumentAsync(
        Guid documentId,
        Stream documentStream,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var jobId     = Guid.NewGuid();

        try
        {
            _logger.LogInformation(
                "Starting OCR processing for document {DocumentId} (tessdata: {Path})",
                documentId, _tessDataPath);

            if (!Directory.Exists(_tessDataPath))
            {
                _logger.LogError(
                    "❌ Tesseract data directory does not exist: {Path}. Make sure tesseract-ocr is installed.",
                    _tessDataPath);
            }
            else
            {
                var langFiles = Directory.GetFiles(_tessDataPath, "*.traineddata");
                _logger.LogInformation(
                    "Tesseract data directory: {Count} language files: {Files}",
                    langFiles.Length,
                    string.Join(", ", langFiles.Select(Path.GetFileNameWithoutExtension)));
            }

            var job = new OcrJob
            {
                Id         = jobId,
                DocumentId = documentId,
                Status     = OcrStatus.Processing,
                CreatedAt  = startTime,
                StartedAt  = startTime,
                Language   = language ?? "eng"
            };
            _jobCache[jobId] = job;

            documentStream.Position = 0;
            var header = new byte[4];
            await documentStream.ReadAsync(header, 0, 4, cancellationToken);
            documentStream.Position = 0;

            bool isPdf = header[0] == 0x25 && header[1] == 0x50 &&
                         header[2] == 0x44 && header[3] == 0x46; // %PDF

            _logger.LogInformation("Document {DocumentId} detected as {Type}",
                documentId, isPdf ? "PDF" : "Image");

            List<PageOcrResult> pages = isPdf
                ? await ProcessPdfAsync(documentStream, language ?? "eng", cancellationToken)
                : await ProcessImageAsync(documentStream, language ?? "eng", cancellationToken);

            var allText       = string.Join("\n\n", pages.Select(p => p.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
            var avgConfidence = pages.Any() ? pages.Average(p => p.Confidence) : 0f;

            _logger.LogInformation(
                "OCR completed for {DocumentId}: {Pages} pages, {TextLength} chars, confidence {Confidence:F2}",
                documentId, pages.Count, allText.Length, avgConfidence);

            job.Status        = OcrStatus.Completed;
            job.CompletedAt   = DateTime.UtcNow;
            job.ExtractedText = allText;
            job.PageCount     = pages.Count;
            job.Confidence    = avgConfidence;
            _jobCache[jobId]  = job;

            return new OcrResult
            {
                JobId             = jobId,
                DocumentId        = documentId,
                Success           = !string.IsNullOrWhiteSpace(allText),
                ExtractedText     = allText,
                PageCount         = pages.Count,
                Pages             = pages,
                AverageConfidence = avgConfidence,
                ProcessingTime    = DateTime.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing OCR for document {DocumentId}", documentId);

            if (_jobCache.TryGetValue(jobId, out var job))
            {
                job.Status       = OcrStatus.Failed;
                job.ErrorMessage = ex.Message;
                job.CompletedAt  = DateTime.UtcNow;
            }

            return new OcrResult
            {
                JobId          = jobId,
                DocumentId     = documentId,
                Success        = false,
                ErrorMessage   = ex.Message,
                ProcessingTime = DateTime.UtcNow - startTime
            };
        }
    }

    private async Task<List<PageOcrResult>> ProcessPdfAsync(
        Stream pdfStream,
        string language,
        CancellationToken cancellationToken)
    {
        var results = new List<PageOcrResult>();

        using var memoryStream = new MemoryStream();
        await pdfStream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        using var pdfReader   = new PdfReader(memoryStream);
        using var pdfDocument = new PdfDocument(pdfReader);

        for (int i = 1; i <= pdfDocument.GetNumberOfPages(); i++)
        {
            try
            {
                var page     = pdfDocument.GetPage(i);
                var strategy = new SimpleTextExtractionStrategy();
                string text  = PdfTextExtractor.GetTextFromPage(page, strategy);

                if (!string.IsNullOrWhiteSpace(text) && text.Trim().Length > 50)
                {
                    results.Add(new PageOcrResult { PageNumber = i, Text = text.Trim(), Confidence = 1.0f });
                    _logger.LogDebug("PDF page {Page}: {Chars} chars via native text", i, text.Length);
                }
                else
                {
                    _logger.LogWarning("PDF page {Page} has little/no native text", i);
                    results.Add(new PageOcrResult { PageNumber = i, Text = text?.Trim() ?? string.Empty, Confidence = 0f });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing PDF page {PageNumber}", i);
            }
        }

        return results;
    }

    private async Task<List<PageOcrResult>> ProcessImageAsync(
        Stream imageStream,
        string language,
        CancellationToken cancellationToken)
    {
        var results  = new List<PageOcrResult>();
        string? tempPath = null;

        try
        {
            using var ms = new MemoryStream();
            await imageStream.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;

            _logger.LogInformation("Processing image for OCR: {Bytes} bytes, language={Lang}", ms.Length, language);

            using var image = await Image.LoadAsync<Rgba32>(ms, cancellationToken);

            _logger.LogInformation("Image loaded: {Width}x{Height} pixels", image.Width, image.Height);

            // Scale up small images for better OCR accuracy
            const int minDimension = 1000;
            if (image.Width < minDimension || image.Height < minDimension)
            {
                var scaleFactor = Math.Max(
                    (double)minDimension / image.Width,
                    (double)minDimension / image.Height);
                var newWidth  = (int)(image.Width  * scaleFactor);
                var newHeight = (int)(image.Height * scaleFactor);
                _logger.LogInformation("Scaling image {OldW}x{OldH} → {NewW}x{NewH}", image.Width, image.Height, newWidth, newHeight);
                image.Mutate(x => x.Resize(newWidth, newHeight));
            }

            // Preprocessing for OCR
            image.Mutate(x => x
                .Grayscale()
                .Contrast(1.5f)
                .Brightness(1.1f)
                .GaussianSharpen(1.0f)
            );

            // Save to temp PNG (lossless — Leptonica requirement)
            tempPath = Path.Combine(Path.GetTempPath(), $"ocr_{Guid.NewGuid():N}.png");
            await image.SaveAsPngAsync(tempPath, cancellationToken);
            _logger.LogInformation("Preprocessed image saved: {Path} ({Size} bytes)", tempPath, new FileInfo(tempPath).Length);

            string ocrText    = string.Empty;
            float  confidence = 0f;
            string? lastError = null;

            var langList = new[] { language, "eng" }.Distinct().ToArray();

            foreach (var lang in langList)
            {
                try
                {
                    _logger.LogInformation("Attempting OCR with lang='{Lang}', tessdata='{Path}'", lang, _tessDataPath);

                    // ── FIX: Pass PageSegMode directly to engine.Process() ──────────
                    // The old code called engine.SetVariable("tessedit_pageseg_mode", "1")
                    // after construction, which is unreliable in some Tesseract.NET builds.
                    // Using the PageSegMode enum in the Process() call is the correct approach.
                    using var engine = new TesseractEngine(_tessDataPath, lang, EngineMode.Default);

                    using var pix  = Pix.LoadFromFile(tempPath);
                    using var page = engine.Process(pix, PageSegMode.Auto);

                    ocrText    = page.GetText() ?? string.Empty;
                    confidence = page.GetMeanConfidence();

                    _logger.LogInformation(
                        "OCR lang='{Lang}': {Chars} chars, confidence={Conf:F2}",
                        lang, ocrText.Length, confidence);

                    if (!string.IsNullOrWhiteSpace(ocrText) && ocrText.Trim().Length > 5)
                        break;

                    _logger.LogWarning("lang='{Lang}' returned near-empty result — trying next", lang);
                }
                catch (TesseractException tex)
                {
                    lastError = tex.Message;
                    // ── FIX: Log the FULL exception, not just type name ──
                    _logger.LogWarning(tex,
                        "❌ TesseractException lang='{Lang}': {Message}. " +
                        "Check that '{LangFile}' exists in tessdata dir.",
                        lang, tex.Message,
                        Path.Combine(_tessDataPath, lang + ".traineddata"));
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    _logger.LogError(ex,
                        "❌ {ExType} during OCR lang='{Lang}': {Message}",
                        ex.GetType().Name, lang, ex.Message);
                }
            }

            var trimmedText = ocrText.Trim();

            _logger.LogInformation(
                "Final OCR result: {Length} chars, confidence={Conf:F2}, lastError='{Error}', preview='{Preview}'",
                trimmedText.Length, confidence,
                lastError ?? "none",
                trimmedText.Length > 100 ? trimmedText[..100] + "..." : trimmedText);

            results.Add(new PageOcrResult
            {
                PageNumber = 1,
                Text       = trimmedText,
                Confidence = confidence,
                Metadata   = lastError != null
                    ? new Dictionary<string, object> { ["last_tesseract_error"] = lastError }
                    : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in ProcessImageAsync: {ExType}: {Message}",
                ex.GetType().Name, ex.Message);

            results.Add(new PageOcrResult
            {
                PageNumber = 1,
                Text       = string.Empty,
                Confidence = 0f,
                Metadata   = new Dictionary<string, object> { ["error"] = ex.Message }
            });
        }
        finally
        {
            if (tempPath != null && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch { /* best effort */ }
            }
        }

        return results;
    }

    public Task<OcrJob?> GetOcrJobStatusAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var job = _jobCache.Values.FirstOrDefault(j => j.DocumentId == documentId);
        return Task.FromResult(job);
    }

    public Task<List<OcrJob>> GetPendingJobsAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        var pendingJobs = _jobCache.Values
            .Where(j => j.Status == OcrStatus.Pending)
            .OrderBy(j => j.CreatedAt)
            .Take(count)
            .ToList();
        return Task.FromResult(pendingJobs);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // HELPER: Sanitize Metadata before sending to OpenSearch
    //
    // ROOT CAUSE: PostgreSQL stores Metadata as JSONB. When EF Core reads it back,
    // it deserializes all values as JsonElement (not plain string/int/bool).
    // The SearchService then serializes the document to send to OpenSearch, and
    // System.Text.Json serializes JsonElement objects as their raw representation
    // ({valueKind: 3} for a string), not the actual value.
    // OpenSearch then fails: "failed to parse field [metadata.category] of type [text]"
    //
    // FIX: Call SanitizeMetadata() before indexing in UpdateDocumentIndexAsync /
    // IndexDocumentAsync in SearchService. This unwraps JsonElement to C# primitives.
    // ─────────────────────────────────────────────────────────────────────────────
    public static Dictionary<string, object>? SanitizeMetadata(Dictionary<string, object>? metadata)
    {
        if (metadata == null) return null;

        var result = new Dictionary<string, object>(metadata.Count);
        foreach (var (key, value) in metadata)
        {
            result[key] = value is JsonElement je ? UnwrapJsonElement(je) : (value ?? string.Empty);
        }
        return result;
    }

    private static object UnwrapJsonElement(JsonElement je) => je.ValueKind switch
    {
        JsonValueKind.String  => je.GetString() ?? string.Empty,
        JsonValueKind.Number  => je.TryGetInt64(out var l)  ? (object)l
                               : je.TryGetDouble(out var d) ? (object)d
                               : je.GetRawText(),
        JsonValueKind.True    => true,
        JsonValueKind.False   => false,
        JsonValueKind.Null    => string.Empty,
        _                     => je.GetRawText()  // Array/Object → keep as raw JSON string
    };
}