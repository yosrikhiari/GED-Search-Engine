using GED.Core.Interfaces;
using GED.Core.Models;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace GED.Infrastructure.Services;

/// <summary>
/// OCR service for scanned PDF files using ocrmypdf (which internally calls Tesseract).
/// 
/// <para>
/// This service adds a searchable text layer to image-only PDFs by:
/// <list type="number">
///   <item>
///     Running ocrmypdf with --force-ocr to process image pages
///   </item>
///   <item>
///     Using iText to extract text from the resulting searchable PDF
///   </item>
/// </list>
/// </para>
/// 
/// <para>
/// For standalone image files (JPEG, PNG, etc.) use <see cref="TesseractDirectOcrService"/>.
/// For native PDF text extraction, the <see cref="OcrWorkerService"/> skips this service entirely.
/// </para>
/// 
/// <para>
/// Supported ocrmypdf exit codes:
/// <list type="bullet">
///   <item>0 = success</item>
///   <item>6 = skipped (PDF already has text, only with --skip-text)</item>
///   <item>1-5, 8-9, 15 = various error conditions (logged and thrown)</item>
/// </list>
/// </para>
/// </summary>
public class OcrmyPdfOcrService : IOcrService
{
    private readonly ILogger<OcrmyPdfOcrService> _logger;
    private readonly IMessageQueueService _messageQueue;

    /// <summary>
    /// Path to the ocrmypdf executable.
    /// </summary>
    private readonly string _ocrmypdfPath;

    /// <summary>
    /// Initializes a new instance of <see cref="OcrmyPdfOcrService"/>.
    /// </summary>
    /// <param name="logger">Logger for OCR events.</param>
    /// <param name="messageQueue">Message queue for job queuing (not used in direct processing).</param>
    /// <param name="ocrmypdfPath">Path to ocrmypdf executable. Defaults to "ocrmypdf" (PATH lookup).</param>
    public OcrmyPdfOcrService(
        ILogger<OcrmyPdfOcrService> logger,
        IMessageQueueService messageQueue,
        string ocrmypdfPath = "ocrmypdf")
    {
        _logger = logger;
        _messageQueue = messageQueue;
        _ocrmypdfPath = ocrmypdfPath;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    /// <remarks>
    /// Processes a PDF document by:
    /// <list type="number">
    ///   <item>Writing input stream to a temporary file</item>
    ///   <item>Running ocrmypdf with --force-ocr --rotate-pages --deskew</item>
    ///   <item>Extracting text from the output PDF using iText</item>
    ///   <item>Cleaning up temporary files</item>
    /// </list>
    /// </remarks>
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
            // Create temporary files with UUID names to avoid conflicts
            string tempBaseName = Guid.NewGuid().ToString("N");
            inputPath  = Path.Combine(Path.GetTempPath(), $"ocr_in_{tempBaseName}.pdf");
            outputPath = Path.Combine(Path.GetTempPath(), $"ocr_out_{tempBaseName}.pdf");

            // Write the stream to disk with the correct extension
            using (var fs = File.Create(inputPath))
                await documentStream.CopyToAsync(fs, cancellationToken);

            // Build ocrmypdf command line arguments
            // -l: language pack(s)
            // --force-ocr: always run OCR even on image-only pages
            // --rotate-pages: automatically correct page rotation
            // --deskew: correct skewed pages
            // --output-type pdf: output searchable PDF
            string args = $"-l {lang} --force-ocr --rotate-pages --deskew " +
                $"--output-type pdf \"{inputPath}\" \"{outputPath}\"";

            _logger.LogInformation("Running: {Exe} {Args}", _ocrmypdfPath, args);

            var (exitCode, stdout, stderr) = await RunProcessAsync(_ocrmypdfPath, args, cancellationToken);

            _logger.LogInformation(
                "ocrmypdf exit={ExitCode} stdout={Stdout} stderr={Stderr}",
                exitCode,
                stdout.Length > 500 ? stdout[..500] : stdout,
                stderr.Length > 500 ? stderr[..500] : stderr);

            // Handle ocrmypdf exit codes
            // Exit codes:
            //   0  = success
            //   6  = skipped (PDF already has text, only with --skip-text)
            //   10 = missing input file (should not happen)
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
                    9  => "input PDF already has OCR and --skip-text was used",
                    15 => "tesseract failed (language pack missing?)",
                    _ => "unknown error"
                };
                
                _logger.LogError(
                    "ocrmypdf exited {Code} ({Reason}) for document {DocId}. stderr: {Stderr}",
                    exitCode, reason, documentId, stderr);

                // Throw so OcrWorkerService.MarkOcrFailedAsync gets called
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR failed for document {DocumentId}", documentId);
            // Don't swallow — rethrow so OcrWorkerService.MarkOcrFailedAsync is called
            throw;
        }
        finally
        {
            // Clean up temporary files
            TryDelete(inputPath);
            TryDelete(outputPath);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Note: This method is not used by OcrWorkerService directly.
    /// The worker uses <see cref="ProcessDocumentAsync"/> instead.
    /// </remarks>
    public Task<OcrJob?> GetOcrJobStatusAsync(Guid jobId, CancellationToken ct = default)
        => Task.FromResult<OcrJob?>(null);

    /// <inheritdoc />
    /// <remarks>
    /// Note: This method is not used by OcrWorkerService directly.
    /// </remarks>
    public Task<List<OcrJob>> GetPendingJobsAsync(int count = 10, CancellationToken ct = default)
        => Task.FromResult(new List<OcrJob>());

    /// <summary>
    /// Extracts text from all pages of a PDF using iText.
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file.</param>
    /// <returns>Tuple of full text and per-page results.</returns>
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
                Confidence = 1.0f  // iText text extraction assumes perfect accuracy
            });
        }

        return (sb.ToString(), pages);
    }

    /// <summary>
    /// Runs an external process and captures stdout/stderr.
    /// </summary>
    /// <param name="exe">Executable path.</param>
    /// <param name="args">Command line arguments.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tuple of exit code, stdout, and stderr.</returns>
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

        // 5 minute timeout for OCR processing
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

    /// <summary>
    /// Attempts to delete a temporary file, logging warnings on failure.
    /// </summary>
    /// <param name="path">Path to the file to delete.</param>
    private void TryDelete(string? path)
    {
        if (path == null) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete temp file {Path}", path); }
    }
}
