using GED.Core.Interfaces;
using GED.Core.Models;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

/// <summary>
/// Direct Tesseract OCR for image uploads (JPEG, PNG, TIFF, BMP, WebP).
/// Bypasses ocrmypdf — no PDF output needed, just raw text extraction.
/// Faster than ocrmypdf for single images.
/// </summary>
public class TesseractDirectOcrService : IOcrService
{
    private readonly ILogger<TesseractDirectOcrService> _logger;
    private readonly IMessageQueueService _queue;
    private readonly string _tesseractPath;

    private static readonly HashSet<string> SupportedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png",
        "image/tiff", "image/bmp", "image/webp"
    };

    public TesseractDirectOcrService(
        ILogger<TesseractDirectOcrService> logger,
        IMessageQueueService queue,
        string tesseractPath = "tesseract")
    {
        _logger        = logger;
        _queue         = queue;
        _tesseractPath = tesseractPath;
    }

    public static bool SupportsContentType(string contentType)
        => SupportedImageTypes.Contains(contentType);

    public async Task<OcrResult> ProcessDocumentAsync(
        Guid documentId, Stream documentStream,
        string? language = null, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var jobId     = Guid.NewGuid();
        var lang      = language ?? "eng";

        // Detect extension from stream header
        var headerBuffer = new byte[4];
        await documentStream.ReadExactlyAsync(headerBuffer, cancellationToken);
        documentStream.Position = 0;
        var ext = DetectExtension(headerBuffer);

        var inputPath  = Path.Combine(Path.GetTempPath(), $"ged_img_{jobId}{ext}");
        var outputBase = Path.Combine(Path.GetTempPath(), $"ged_ocr_{jobId}"); // tesseract appends .txt

        try
        {
            await using (var fs = File.Create(inputPath))
                await documentStream.CopyToAsync(fs, cancellationToken);

            // tesseract <input> <output_base> -l <lang> txt
            // "txt" output type writes <output_base>.txt
            var args = $"\"{inputPath}\" \"{outputBase}\" -l {lang} txt";
            _logger.LogInformation("Running: {Exe} {Args}", _tesseractPath, args);

            var (exitCode, stdout, stderr) = await RunProcessAsync(_tesseractPath, args, cancellationToken);

            if (exitCode != 0)
            {
                _logger.LogError("Tesseract exited {Code}: {Stderr}", exitCode, stderr);
                throw new InvalidOperationException($"Tesseract exit {exitCode}: {stderr[..Math.Min(300, stderr.Length)]}");
            }

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
                PageCount         = 1,
                AverageConfidence = 0f,   // tesseract txt output doesn't include confidence
                ProcessingTime    = DateTime.UtcNow - startTime
            };
        }
        finally
        {
            TryDelete(inputPath);
            TryDelete(outputBase + ".txt");
        }
    }

    // ── Unused IOcrService members ───────────────────────────────────────────
    public Task<Guid> QueueOcrJobAsync(Guid documentId, string? language = null, CancellationToken ct = default)
        => throw new NotSupportedException("Use OcrWorkerService for queued jobs.");
    public Task<OcrJob?> GetOcrJobStatusAsync(Guid jobId, CancellationToken ct = default)
        => Task.FromResult<OcrJob?>(null);
    public Task<List<OcrJob>> GetPendingJobsAsync(int count = 10, CancellationToken ct = default)
        => Task.FromResult(new List<OcrJob>());

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static string DetectExtension(byte[] header)
    {
        if (header[0] == 0x89 && header[1] == 0x50) return ".png";
        if (header[0] == 0xFF && header[1] == 0xD8) return ".jpg";
        if ((header[0] == 0x49 && header[1] == 0x49) || (header[0] == 0x4D && header[1] == 0x4D)) return ".tiff";
        if (header[0] == 0x42 && header[1] == 0x4D) return ".bmp";
        if (header[0] == 0x52 && header[1] == 0x49) return ".webp";
        return ".png";
    }

    private void TryDelete(string? path)
    {
        if (path == null) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

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