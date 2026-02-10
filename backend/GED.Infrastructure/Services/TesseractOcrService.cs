using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Logging;
using Tesseract;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

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
        string tessDataPath = "/usr/share/tesseract-ocr/4.00/tessdata")
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

        // Queue for async processing
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
            _logger.LogInformation("Starting OCR processing for document {DocumentId}", documentId);

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

            // Detect if it's a PDF or image
            documentStream.Position = 0;
            var header = new byte[4];
            await documentStream.ReadAsync(header, 0, 4, cancellationToken);
            documentStream.Position = 0;

            bool isPdf = header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46; // %PDF

            List<PageOcrResult> pages;
            if (isPdf)
            {
                pages = await ProcessPdfAsync(documentStream, language ?? "eng", cancellationToken);
            }
            else
            {
                pages = await ProcessImageAsync(documentStream, language ?? "eng", cancellationToken);
            }

            var allText = string.Join("\n\n", pages.Select(p => p.Text));
            var avgConfidence = pages.Any() ? pages.Average(p => p.Confidence) : 0f;

            job.Status = OcrStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.ExtractedText = allText;
            job.PageCount = pages.Count;
            job.Confidence = avgConfidence;

            _jobCache[jobId] = job;

            _logger.LogInformation(
                "OCR processing completed for document {DocumentId}. Pages: {PageCount}, Confidence: {Confidence}",
                documentId, pages.Count, avgConfidence);

            return new OcrResult
            {
                JobId = jobId,
                DocumentId = documentId,
                Success = true,
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
                
                // Try to extract text first (native text in PDF)
                var strategy = new SimpleTextExtractionStrategy();
                string extractedText = PdfTextExtractor.GetTextFromPage(page, strategy);

                if (!string.IsNullOrWhiteSpace(extractedText) && extractedText.Length > 50)
                {
                    // PDF has native text, use it
                    results.Add(new PageOcrResult
                    {
                        PageNumber = i,
                        Text = extractedText.Trim(),
                        Confidence = 1.0f // Native text is 100% accurate
                    });
                }
                else
                {
                    // PDF is scanned image, need OCR
                    // This would require converting PDF page to image first
                    // For now, add placeholder
                    _logger.LogWarning("Page {PageNumber} appears to be an image, full OCR not yet implemented", i);
                    
                    results.Add(new PageOcrResult
                    {
                        PageNumber = i,
                        Text = "[Image-based PDF page - OCR processing required]",
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

        try
        {
            // Load and preprocess image
            using var image = await Image.LoadAsync(imageStream, cancellationToken);
            
            // Convert to grayscale and enhance contrast for better OCR
            image.Mutate(x => x
                .Grayscale()
                .Contrast(1.2f)
            );

            // Save to temporary file for Tesseract
            var tempPath = Path.GetTempFileName();
            try
            {
                await image.SaveAsPngAsync(tempPath, cancellationToken);

                // Perform OCR
                using var engine = new TesseractEngine(_tessDataPath, language, EngineMode.Default);
                using var img = Pix.LoadFromFile(tempPath);
                using var page = engine.Process(img);

                var text = page.GetText();
                var confidence = page.GetMeanConfidence();

                results.Add(new PageOcrResult
                {
                    PageNumber = 1,
                    Text = text.Trim(),
                    Confidence = confidence
                });

                _logger.LogInformation("OCR completed with confidence: {Confidence}", confidence);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing OCR on image");
            throw;
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
