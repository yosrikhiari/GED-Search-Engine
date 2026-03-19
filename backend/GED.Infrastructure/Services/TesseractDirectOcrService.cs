using GED.Core.Interfaces;
using GED.Core.Models;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

/// <summary>
/// Direct Tesseract OCR service for image uploads (JPEG, PNG, TIFF, BMP, WebP).
/// 
/// <para>
/// This service provides fast, direct OCR for image files without the overhead
/// of PDF conversion (which ocrmypdf does for scanned PDFs).
/// </para>
/// 
/// <para>
/// Supported formats:
/// <list type="bullet">
///   <item>JPEG (.jpg, .jpeg)</item>
///   <item>PNG (.png)</item>
///   <item>TIFF (.tiff, .tif)</item>
///   <item>BMP (.bmp)</item>
///   <item>WebP (.webp)</item>
/// </list>
/// </para>
/// 
/// <para>
/// For scanned PDFs, use <see cref="OcrmyPdfOcrService"/> instead.
/// This service bypasses ocrmypdf — no PDF output needed, just raw text extraction.
/// </para>
/// 
/// <para>
/// File type detection: Uses magic bytes (file header) rather than file extension
/// to correctly identify file format from stream data.
/// </para>
/// </summary>
public class TesseractDirectOcrService : IOcrService
{
    private readonly ILogger<TesseractDirectOcrService> _logger;
    private readonly IMessageQueueService _queue;

    /// <summary>
    /// Path to the Tesseract executable.
    /// </summary>
    private readonly string _tesseractPath;

    /// <summary>
    /// Supported image MIME types for direct OCR.
    /// </summary>
    private static readonly HashSet<string> SupportedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png",
        "image/tiff", "image/bmp", "image/webp"
    };

    /// <summary>
    /// Initializes a new instance of <see cref="TesseractDirectOcrService"/>.
    /// </summary>
    /// <param name="logger">Logger for OCR events.</param>
    /// <param name="queue">Message queue service (not used in direct processing).</param>
    /// <param name="tesseractPath">Path to Tesseract executable. Defaults to "tesseract" (PATH lookup).</param>
    public TesseractDirectOcrService(
        ILogger<TesseractDirectOcrService> logger,
        IMessageQueueService queue,
        string tesseractPath = "tesseract")
    {
        _logger        = logger;
        _queue         = queue;
        _tesseractPath = tesseractPath;
    }

    /// <summary>
    /// Checks if a content type is supported for direct OCR.
    /// </summary>
    /// <param name="contentType">MIME type to check.</param>
    /// <returns>True if the type is a supported image format.</returns>
    public static bool SupportsContentType(string contentType)
        => SupportedImageTypes.Contains(contentType);

    /// <inheritdoc />
    /// <remarks>
    /// Processing steps:
    /// <list type="number">
    ///   <item>Detect file format from magic bytes (header)</item>
    ///   <item>Write input stream to temporary file</item>
    ///   <item>Run Tesseract with language pack and "txt" output</item>
    ///   <item>Read output .txt file</item>
    ///   <item>Clean up temporary files</item>
    /// </list>
    /// </remarks>
    public async Task<OcrResult> ProcessDocumentAsync(
        Guid documentId, Stream documentStream,
        string? language = null, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var jobId     = Guid.NewGuid();
        var lang      = language ?? "eng";

        // Detect file format from magic bytes
        var headerBuffer = new byte[4];
        await documentStream.ReadExactlyAsync(headerBuffer, cancellationToken);
        documentStream.Position = 0;
        var ext = DetectExtension(headerBuffer);

        // Create temporary file paths
        var inputPath  = Path.Combine(Path.GetTempPath(), $"ged_img_{jobId}{ext}");
        var outputBase = Path.Combine(Path.GetTempPath(), $"ged_ocr_{jobId}"); // Tesseract appends .txt

        try
        {
            // Write input stream to temporary file
            await using (var fs = File.Create(inputPath))
                await documentStream.CopyToAsync(fs, cancellationToken);

            // Tesseract command: tesseract <input> <output_base> -l <lang> txt
            // Output will be at <output_base>.txt
            var args = $"\"{inputPath}\" \"{outputBase}\" -l {lang} txt";
            _logger.LogInformation("Running: {Exe} {Args}", _tesseractPath, args);

            var (exitCode, stdout, stderr) = await RunProcessAsync(_tesseractPath, args, cancellationToken);

            if (exitCode != 0)
            {
                _logger.LogError("Tesseract exited {Code}: {Stderr}", exitCode, stderr);
                throw new InvalidOperationException($"Tesseract exit {exitCode}: {stderr[..Math.Min(300, stderr.Length)]}");
            }

            // Read output text file
            var outputTxt = outputBase + ".txt";
            var text = File.Exists(outputTxt)
                ? await File.ReadAllTextAsync(outputTxt, cancellationToken)
                : string.Empty;

            _logger.LogInformation(
                "Tesseract direct: {Chars} chars extracted for image document {DocId}",
                text.Length, documentId);

            return new OcrResult
            {
                JobId             = jobId,
                DocumentId        = documentId,
                Success           = !string.IsNullOrWhiteSpace(text),
                ExtractedText     = text,
                PageCount         = 1,  // Single image = one page
                AverageConfidence = 0f, // Tesseract txt output doesn't include confidence
                ProcessingTime    = DateTime.UtcNow - startTime
            };
        }
        finally
        {
            // Clean up temporary files
            TryDelete(inputPath);
            TryDelete(outputBase + ".txt");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Note: This method is not used by OcrWorkerService directly.
    /// Direct processing uses <see cref="ProcessDocumentAsync"/> instead.
    /// </remarks>
    public Task<Guid> QueueOcrJobAsync(Guid documentId, string? language = null, CancellationToken ct = default)
        => throw new NotSupportedException("Use OcrWorkerService for queued jobs.");

    /// <inheritdoc />
    public Task<OcrJob?> GetOcrJobStatusAsync(Guid jobId, CancellationToken ct = default)
        => Task.FromResult<OcrJob?>(null);

    /// <inheritdoc />
    public Task<List<OcrJob>> GetPendingJobsAsync(int count = 10, CancellationToken ct = default)
        => Task.FromResult(new List<OcrJob>());

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Detects image file format from magic bytes (file header).
    /// </summary>
    /// <param name="header">First 4 bytes of the file.</param>
    /// <returns>File extension including the dot (e.g., ".png", ".jpg").</returns>
    /// <remarks>
    /// Magic byte signatures:
    /// <list type="bullet">
    ///   <item>89 50 4E 47 → PNG</item>
    ///   <item>FF D8 FF → JPEG</item>
    ///   <item>49 49 or 4D 4D → TIFF (little or big endian)</item>
    ///   <item>42 4D → BMP</item>
    ///   <item>52 49 46 46 ... 57 45 42 50 → WebP (RIFF...WEBP)</item>
    /// </list>
    /// Defaults to PNG if unknown.
    /// </remarks>
    private static string DetectExtension(byte[] header)
    {
        if (header[0] == 0x89 && header[1] == 0x50) return ".png";      // PNG
        if (header[0] == 0xFF && header[1] == 0xD8) return ".jpg";      // JPEG
        if ((header[0] == 0x49 && header[1] == 0x49) || (header[0] == 0x4D && header[1] == 0x4D)) return ".tiff"; // TIFF
        if (header[0] == 0x42 && header[1] == 0x4D) return ".bmp";      // BMP
        if (header[0] == 0x52 && header[1] == 0x49) return ".webp";      // WebP
        return ".png"; // Default to PNG for unknown formats
    }

    /// <summary>
    /// Attempts to delete a temporary file, logging warnings on failure.
    /// </summary>
    /// <param name="path">Path to file to delete.</param>
    private void TryDelete(string? path)
    {
        if (path == null) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Runs Tesseract as an external process with timeout.
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
                FileName               = exe,
                Arguments              = args,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            }
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        // 3 minute timeout for single image OCR
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try { await process.WaitForExitAsync(linked.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }
}
