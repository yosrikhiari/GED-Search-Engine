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
        _logger = logger;
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
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Status = OcrStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            Language = language ?? "eng"
        };

        _jobCache[job.Id] = job;

        await _messageQueue.PublishAsync("ocr-queue", new
        {
            JobId = job.Id,
            DocumentId = documentId,
            Language = language ?? "eng"
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
        var jobId = Guid.NewGuid();

        try
        {
            _logger.LogInformation(
                "Starting OCR processing for document {DocumentId} (tessdata: {Path})",
                documentId, _tessDataPath);

            // ⭐ FIX: Verify tessdata path exists and log what's in it
            if (!Directory.Exists(_tessDataPath))
            {
                _logger.LogError(
                    "❌ Tesseract data directory does not exist: {Path}. " +
                    "OCR will fail. Make sure tesseract-ocr is installed.",
                    _tessDataPath);
            }
            else
            {
                var langFiles = Directory.GetFiles(_tessDataPath, "*.traineddata");
                _logger.LogDebug(
                    "Tesseract data directory found with {Count} language files: {Files}",
                    langFiles.Length,
                    string.Join(", ", langFiles.Select(Path.GetFileNameWithoutExtension)));
            }

            var job = new OcrJob
            {
                Id = jobId,
                DocumentId = documentId,
                Status = OcrStatus.Processing,
                CreatedAt = startTime,
                StartedAt = startTime,
                Language = language ?? "eng"
            };

            _jobCache[jobId] = job;

            // Detect if it's a PDF or image by reading the magic bytes
            documentStream.Position = 0;
            var header = new byte[4];
            await documentStream.ReadAsync(header, 0, 4, cancellationToken);
            documentStream.Position = 0;

            bool isPdf = header[0] == 0x25 && header[1] == 0x50 &&
                         header[2] == 0x44 && header[3] == 0x46; // %PDF

            _logger.LogInformation(
                "Document {DocumentId} detected as {Type}",
                documentId, isPdf ? "PDF" : "Image");

            List<PageOcrResult> pages;
            if (isPdf)
            {
                pages = await ProcessPdfAsync(documentStream, language ?? "eng", cancellationToken);
            }
            else
            {
                pages = await ProcessImageAsync(documentStream, language ?? "eng", cancellationToken);
            }

            var allText = string.Join("\n\n", pages.Select(p => p.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
            var avgConfidence = pages.Any() ? pages.Average(p => p.Confidence) : 0f;

            _logger.LogInformation(
                "OCR completed for {DocumentId}: {Pages} pages, {TextLength} chars, confidence {Confidence:F2}",
                documentId, pages.Count, allText.Length, avgConfidence);

            job.Status = OcrStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.ExtractedText = allText;
            job.PageCount = pages.Count;
            job.Confidence = avgConfidence;
            _jobCache[jobId] = job;

            return new OcrResult
            {
                JobId = jobId,
                DocumentId = documentId,
                Success = !string.IsNullOrWhiteSpace(allText),
                ExtractedText = allText,
                PageCount = pages.Count,
                Pages = pages,
                AverageConfidence = avgConfidence,
                ProcessingTime = DateTime.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing OCR for document {DocumentId}", documentId);

            if (_jobCache.TryGetValue(jobId, out var job))
            {
                job.Status = OcrStatus.Failed;
                job.ErrorMessage = ex.Message;
                job.CompletedAt = DateTime.UtcNow;
            }

            return new OcrResult
            {
                JobId = jobId,
                DocumentId = documentId,
                Success = false,
                ErrorMessage = ex.Message,
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

        using var pdfReader = new PdfReader(memoryStream);
        using var pdfDocument = new PdfDocument(pdfReader);

        for (int i = 1; i <= pdfDocument.GetNumberOfPages(); i++)
        {
            try
            {
                var page = pdfDocument.GetPage(i);

                var strategy = new SimpleTextExtractionStrategy();
                string extractedText = PdfTextExtractor.GetTextFromPage(page, strategy);

                if (!string.IsNullOrWhiteSpace(extractedText) && extractedText.Trim().Length > 50)
                {
                    results.Add(new PageOcrResult
                    {
                        PageNumber = i,
                        Text = extractedText.Trim(),
                        Confidence = 1.0f
                    });
                    _logger.LogDebug("PDF page {Page}: extracted {Chars} chars via native text", i, extractedText.Length);
                }
                else
                {
                    _logger.LogWarning("PDF page {PageNumber} has little/no native text — full image-OCR not yet implemented", i);
                    results.Add(new PageOcrResult
                    {
                        PageNumber = i,
                        Text = extractedText?.Trim() ?? string.Empty,
                        Confidence = 0f
                    });
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
        var results = new List<PageOcrResult>();
        string? tempPath = null;

        try
        {
            // ⭐ FIX: Copy stream to memory first so we can re-read it
            using var ms = new MemoryStream();
            await imageStream.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;

            _logger.LogInformation(
                "Processing image for OCR: {Bytes} bytes, language={Lang}",
                ms.Length, language);

            // ⭐ FIX: Load and preprocess image with ImageSharp
            using var image = await Image.LoadAsync<Rgba32>(ms, cancellationToken);

            _logger.LogInformation(
                "Image loaded: {Width}x{Height} pixels",
                image.Width, image.Height);

            // ⭐ FIX: Scale up small images — Tesseract works much better at ~300 DPI.
            // A typical document page is ~2480x3508 at 300 DPI.
            // If image is small, scale it up for better OCR accuracy.
            const int minDimension = 1000;
            if (image.Width < minDimension || image.Height < minDimension)
            {
                var scaleFactor = Math.Max(
                    (double)minDimension / image.Width,
                    (double)minDimension / image.Height);

                var newWidth  = (int)(image.Width  * scaleFactor);
                var newHeight = (int)(image.Height * scaleFactor);

                _logger.LogInformation(
                    "Scaling image from {OldW}x{OldH} to {NewW}x{NewH} for better OCR",
                    image.Width, image.Height, newWidth, newHeight);

                image.Mutate(x => x.Resize(newWidth, newHeight));
            }

            // ⭐ FIX: Better preprocessing pipeline for OCR
            image.Mutate(x => x
                .Grayscale()           // Convert to grayscale
                .Contrast(1.5f)        // Boost contrast (was 1.2f)
                .Brightness(1.1f)      // Slightly brighten
                .GaussianSharpen(1.0f) // Sharpen edges to help character recognition
            );

            // Save to temp PNG (lossless — important for OCR quality)
            tempPath = Path.Combine(Path.GetTempPath(), $"ocr_{Guid.NewGuid():N}.png");
            await image.SaveAsPngAsync(tempPath, cancellationToken);

            _logger.LogInformation(
                "Preprocessed image saved to temp: {Path}", tempPath);

            // ⭐ FIX: Try OCR with better error handling and fallback language
            string ocrText = string.Empty;
            float confidence = 0f;

            var langList = new[] { language, "eng" }.Distinct().ToArray();

            foreach (var lang in langList)
            {
                try
                {
                    _logger.LogInformation(
                        "Attempting OCR with language '{Lang}', tessdata at '{Path}'",
                        lang, _tessDataPath);

                    using var engine = new TesseractEngine(_tessDataPath, lang, EngineMode.Default);

                    // ⭐ FIX: Set page segmentation mode — PSM_AUTO works well for most docs
                    engine.SetVariable("tessedit_pageseg_mode", "1"); // Automatic page segmentation with OSD

                    using var img = Pix.LoadFromFile(tempPath);
                    using var page = engine.Process(img);

                    ocrText    = page.GetText() ?? string.Empty;
                    confidence = page.GetMeanConfidence();

                    _logger.LogInformation(
                        "OCR succeeded with lang='{Lang}': {Chars} chars, confidence={Conf:F2}",
                        lang, ocrText.Length, confidence);

                    // If we got meaningful text, stop trying other languages
                    if (!string.IsNullOrWhiteSpace(ocrText) && ocrText.Trim().Length > 10)
                        break;
                }
                catch (TesseractException tex)
                {
                    _logger.LogWarning(tex,
                        "Tesseract failed with language '{Lang}' — trying next option", lang);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Unexpected error during OCR with language '{Lang}'", lang);
                }
            }

            var trimmedText = ocrText.Trim();

            _logger.LogInformation(
                "Final OCR result: {Length} chars, confidence={Confidence:F2}, " +
                "preview='{Preview}'",
                trimmedText.Length,
                confidence,
                trimmedText.Length > 100 ? trimmedText[..100] + "..." : trimmedText);

            results.Add(new PageOcrResult
            {
                PageNumber = 1,
                Text = trimmedText,
                Confidence = confidence
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error performing OCR on image");
            results.Add(new PageOcrResult
            {
                PageNumber = 1,
                Text = string.Empty,
                Confidence = 0f,
                Metadata = new Dictionary<string, object> { ["error"] = ex.Message }
            });
        }
        finally
        {
            // Clean up temp file
            if (tempPath != null && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch { /* best effort */ }
            }
        }

        return results;
    }

    public Task<OcrJob?> GetOcrJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        _jobCache.TryGetValue(jobId, out var job);
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
}