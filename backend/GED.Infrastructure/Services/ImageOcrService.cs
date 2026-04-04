using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace GED.Infrastructure.Services;

/// <summary>
/// Service that runs ocrmypdf on image files.
/// ocrmypdf accepts images (JPEG, PNG, TIFF, etc.) directly and handles 
/// conversion to PDF internally before performing OCR.
/// This replaces the old TesseractDirectOcrService approach.
/// </summary>
public class ImageOcrService
{
    private readonly ILogger<ImageOcrService> _logger;
    private readonly string _ocrmypdfPath;

    public ImageOcrService(
        ILogger<ImageOcrService> logger,
        string ocrmypdfPath = "ocrmypdf")
    {
        _logger = logger;
        _ocrmypdfPath = ocrmypdfPath;
    }

    /// <summary>
    /// Runs OCR on an image file using ocrmypdf.
    /// ocrmypdf internally converts the image to PDF and runs Tesseract.
    /// </summary>
    /// <param name="imagePath">Path to the input image file.</param>
    /// <param name="outputPdfPath">Path for the output searchable PDF.</param>
    /// <param name="language">Language for OCR (e.g., "eng", "eng+fra").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Path to the output PDF with OCR text layer.</returns>
    public async Task<string> ProcessImageAsync(
        string imagePath,
        string outputPdfPath,
        string language = "eng",
        CancellationToken ct = default)
    {
        // Convert RGBA to RGB if needed (ocrmypdf doesn't accept alpha channel)
        var args = $"-l {language} --force-ocr --rotate-pages --deskew --image-dpi 300 --output-type pdf";

        // Check if image has alpha channel and convert if needed
        var pngCheck = await RunProcessAsync("file", $"\"{imagePath}\"", ct);
        bool hasAlpha = pngCheck.stdout.Contains("RGBA") || pngCheck.stdout.Contains("alpha");
        
        string inputForOcrmypdf = imagePath;
        string? tempConvertedPath = null;
        
        if (hasAlpha)
        {
            tempConvertedPath = Path.Combine(Path.GetTempPath(), $"convert_{Guid.NewGuid():N}.png");
            var pythonScript = $"from PIL import Image; img = Image.open(\"{imagePath}\"); bg = Image.new(\"RGB\", img.size, (255,255,255)); bg.paste(img, mask=img.split()[3] if img.mode=='RGBA' else None); bg.save(\"{tempConvertedPath}\")";
            _logger.LogInformation("Converting RGBA image to RGB using Python/Pillow");
            
            var pythonResult = await RunProcessAsync("python3", $"-c \"{pythonScript}\"", ct);
            if (pythonResult.exitCode != 0)
            {
                _logger.LogWarning("Python conversion failed: {Error}, will try alternative approach", pythonResult.stderr);
                // Try using PIL directly with different approach
                var altScript = $"import sys; from PIL import Image; img = Image.open('{imagePath}'); img.convert('RGB').save('{tempConvertedPath}')";
                var altResult = await RunProcessAsync("python3", $"-c \"{altScript}\"", ct);
                if (altResult.exitCode != 0)
                {
                    throw new InvalidOperationException($"Image conversion failed: Python PIL: {altResult.stderr}");
                }
            }
            inputForOcrmypdf = tempConvertedPath;
        }

        args = $"{args} \"{inputForOcrmypdf}\" \"{outputPdfPath}\"";
        
        _logger.LogInformation("Running ocrmypdf on image: {Exe} {Args}", _ocrmypdfPath, args);

        var (exitCode, stdout, stderr) = await RunProcessAsync(_ocrmypdfPath, args, ct);

        // Clean up temp converted file
        if (tempConvertedPath != null)
        {
            try { File.Delete(tempConvertedPath); } catch { }
        }

        _logger.LogInformation(
            "ocrmypdf exit={ExitCode} stdout={Stdout} stderr={Stderr}",
            exitCode,
            stdout.Length > 200 ? stdout[..200] : stdout,
            stderr.Length > 200 ? stderr[..200] : stderr);

        if (exitCode != 0 && exitCode != 6)
        {
            var reason = exitCode switch
            {
                1 => "invalid input file or arguments",
                2 => "input file not found",
                3 => "output file already exists",
                4 => "missing dependency (ghostscript/tesseract)",
                5 => "input file is encrypted",
                8 => "input file is damaged or invalid",
                15 => "tesseract failed (language pack missing?)",
                _ => "unknown error"
            };

            throw new InvalidOperationException(
                $"ocrmypdf exit {exitCode} ({reason}): {(stderr.Length > 300 ? stderr[..300] : stderr)}");
        }

        _logger.LogInformation("Image OCR completed: {OutputPath}", outputPdfPath);
        return outputPdfPath;
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
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
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
}