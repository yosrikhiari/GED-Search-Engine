using GED.Core.Interfaces;
using GED.Core.Models;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace GED.Infrastructure.Services;

public class OcrmyPdfOcrService : IOcrService
{
    private readonly ILogger<OcrmyPdfOcrService> _logger;
    private readonly IMessageQueueService _messageQueue;
    private readonly string _ocrmypdfPath;

    public OcrmyPdfOcrService(
        ILogger<OcrmyPdfOcrService> logger,
        IMessageQueueService messageQueue,
        string ocrmypdfPath = "ocrmypdf")
    {
        _logger = logger;
        _messageQueue = messageQueue;
        _ocrmypdfPath = ocrmypdfPath;
    }

    public async Task<Guid> QueueOcrJobAsync(
        Guid documentId,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var jobId = Guid.NewGuid();

        await _messageQueue.PublishAsync("ocr-queue", new
        {
            JobId = jobId,
            DocumentId = documentId,
            Language = language ?? "eng"
        }, cancellationToken);

        _logger.LogInformation("OCR job {JobId} queued for document {DocumentId}", jobId, documentId);
        return jobId;
    }

    public async Task<OcrResult> ProcessDocumentAsync(
        Guid documentId,
        Stream documentStream,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var jobId = Guid.NewGuid();
        var lang = language ?? "eng";

        string? inputPath = null;
        string? outputPath = null;

        try
        {
            // Detect file type by reading the magic bytes header
            documentStream.Position = 0;
            var header = new byte[4];
            await documentStream.ReadAsync(header, 0, 4, cancellationToken);
            documentStream.Position = 0;

            bool isPdf = header[0] == 0x25 && header[1] == 0x50
                      && header[2] == 0x44 && header[3] == 0x46; // %PDF

            // ── FIX: pass images directly to ocrmypdf with the correct extension ──
            // ocrmypdf 13.x natively accepts JPEG, PNG, TIFF as input.
            // The old approach wrapped the image in a PDF first via iText, which
            // caused ocrmypdf to re-rasterize an already-rasterized image, often
            // producing a corrupt or empty text layer.
            // Passing the raw image file directly avoids this double-conversion.
            string imageExtension = isPdf ? ".pdf" : DetectImageExtension(header);
            string tempBaseName = Guid.NewGuid().ToString("N");

            inputPath  = Path.Combine(Path.GetTempPath(), $"ocr_in_{tempBaseName}{imageExtension}");
            outputPath = Path.Combine(Path.GetTempPath(), $"ocr_out_{tempBaseName}.pdf");

            // Write the stream to disk with the correct extension
            using (var fs = File.Create(inputPath))
                await documentStream.CopyToAsync(fs, cancellationToken);

            _logger.LogInformation(
                "OCR input: {Path} ({Bytes} bytes, isPdf={IsPdf})",
                inputPath, new FileInfo(inputPath).Length, isPdf);

            // Build ocrmypdf arguments
            //   PDFs  → --skip-text  (don't error if a text layer already exists)
            //   Images → --image-dpi (ocrmypdf needs an explicit DPI hint for images
            //            without embedded DPI metadata; 300 is a safe default for
            //            document scans; 72 is used for screen-resolution images)
            string args;
            if (isPdf)
            {
                // --force-ocr: OCR every page, even if a text layer exists.
                // --skip-text caused silent failures on scanned/image-only PDFs.
                // --rotate-pages: auto-correct page orientation (common in scans)
                // --deskew: straighten skewed pages
                args = $"-l {lang} --force-ocr --rotate-pages --deskew " +
                    $"--output-type pdf \"{inputPath}\" \"{outputPath}\"";
            }
            else
            {
                args = $"-l {lang} --image-dpi 300 --force-ocr --rotate-pages --deskew " +
                    $"--output-type pdf \"{inputPath}\" \"{outputPath}\"";
            }

            _logger.LogInformation("Running: {Exe} {Args}", _ocrmypdfPath, args);

            var (exitCode, stdout, stderr) = await RunProcessAsync(_ocrmypdfPath, args, cancellationToken);

            _logger.LogInformation(
                "ocrmypdf exit={ExitCode} stdout={Stdout} stderr={Stderr}",
                exitCode,
                stdout.Length > 500 ? stdout[..500] : stdout,
                stderr.Length > 500 ? stderr[..500] : stderr);

            // Exit codes:
            //   0  = success
            //   6  = skipped (PDF already has text, only with --skip-text)
            //   10 = missing input file (should not happen)
            // Replace the current exit code check block:
            if (exitCode != 0 && exitCode != 6)
            {
                // Map common ocrmypdf exit codes to readable messages
                var reason = exitCode switch
                {
                    1  => "invalid input file or arguments",
                    2  => "input file not found",
                    3  => "output file already exists",
                    4  => "missing dependency (ghostscript/tesseract not installed?)",
                    5  => "input PDF is encrypted",
                    8  => "input PDF is damaged or invalid",
                    9  => "output file already has OCR and --skip-text was used",
                    15 => "tesseract failed (language pack missing?)",
                    _ => "unknown error"
                };
                
                _logger.LogError(
                    "ocrmypdf exited {Code} ({Reason}) for document {DocId}. stderr: {Stderr}",
                    exitCode, reason, documentId, stderr);

                // THROW so OcrWorkerService.MarkOcrFailedAsync gets called
                throw new InvalidOperationException(
                    $"ocrmypdf exit {exitCode} ({reason}): {(stderr.Length > 300 ? stderr[..300] : stderr)}");
            }

            // Extract text from the output PDF using iText
            string extractedText = string.Empty;
            var pages = new List<PageOcrResult>();

            if (File.Exists(outputPath))
            {
                _logger.LogInformation("Extracting text from output PDF: {Path}", outputPath);
                (extractedText, pages) = ExtractTextFromPdf(outputPath);
            }
            else if (exitCode == 6)
            {
                // PDF already had a text layer — extract from the input PDF directly
                _logger.LogInformation("ocrmypdf skipped (exit 6) — extracting from input PDF");
                (extractedText, pages) = ExtractTextFromPdf(inputPath);
            }
            else
            {
                _logger.LogWarning(
                    "ocrmypdf exit={Code} but output PDF not found at {Path}",
                    exitCode, outputPath);
            }

            _logger.LogInformation(
                "OCR complete for {DocId}: {Pages} pages, {Chars} chars extracted",
                documentId, pages.Count, extractedText.Length);

            return new OcrResult
            {
                JobId = jobId,
                DocumentId = documentId,
                Success = !string.IsNullOrWhiteSpace(extractedText),
                ExtractedText = extractedText,
                PageCount = pages.Count,
                Pages = pages,
                AverageConfidence = 0.0f, // ocrmypdf does not expose per-word confidence
                ProcessingTime = DateTime.UtcNow - startTime
            };
        }
        // In ProcessDocumentAsync, replace the outer catch block:
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR failed for document {DocumentId}", documentId);
            // Don't swallow — rethrow so OcrWorkerService.MarkOcrFailedAsync is called
            throw;
        }
        finally
        {
            TryDelete(inputPath);
            TryDelete(outputPath);
        }
    }

    // IOcrService members not used by OcrWorkerService directly
    public Task<OcrJob?> GetOcrJobStatusAsync(Guid jobId, CancellationToken ct = default)
        => Task.FromResult<OcrJob?>(null);

    public Task<List<OcrJob>> GetPendingJobsAsync(int count = 10, CancellationToken ct = default)
        => Task.FromResult(new List<OcrJob>());

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (string text, List<PageOcrResult> pages) ExtractTextFromPdf(string pdfPath)
    {
        using var reader = new PdfReader(pdfPath);
        using var pdfDoc = new PdfDocument(reader);

        var sb = new StringBuilder();
        var pages = new List<PageOcrResult>();

        for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
        {
            var page = pdfDoc.GetPage(i);
            var strategy = new SimpleTextExtractionStrategy();
            var pageText = PdfTextExtractor.GetTextFromPage(page, strategy);

            sb.AppendLine(pageText);
            pages.Add(new PageOcrResult
            {
                PageNumber = i,
                Text = pageText.Trim(),
                Confidence = 1.0f
            });
        }

        return (sb.ToString(), pages);
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunProcessAsync(
        string exe, string args, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string DetectImageExtension(byte[] header)
    {
        // PNG:  89 50 4E 47
        if (header[0] == 0x89 && header[1] == 0x50) return ".png";
        // JPEG: FF D8
        if (header[0] == 0xFF && header[1] == 0xD8) return ".jpg";
        // TIFF: 49 49 (little-endian) or 4D 4D (big-endian)
        if ((header[0] == 0x49 && header[1] == 0x49) ||
            (header[0] == 0x4D && header[1] == 0x4D)) return ".tiff";
        // BMP:  42 4D
        if (header[0] == 0x42 && header[1] == 0x4D) return ".bmp";
        // WebP: 52 49 46 46 (RIFF)
        if (header[0] == 0x52 && header[1] == 0x49) return ".webp";

        // Default: assume PNG — ocrmypdf will error with a clear message if wrong
        return ".png";
    }

    private void TryDelete(string? path)
    {
        if (path == null) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete temp file {Path}", path); }
    }
}