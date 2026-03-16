using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace GED.Infrastructure.Plugins.Samples;

/// <summary>
/// Sample OCR post-processing plugin that improves OCR text quality.
/// Supports English, French, and Arabic text cleanup.
/// </summary>
public class MultilingualOcrCleanerPlugin : IOcrPostProcessingPlugin
{
    private readonly ILogger<MultilingualOcrCleanerPlugin> _logger;
    private readonly IConfiguration _configuration;
    private readonly bool _enabled;

    public string Id => "ged.ocr.multilingual-cleaner";
    public string Name => "Multilingual OCR Cleaner";
    public string Version => "1.0.0";
    public string Description => "Cleans and improves OCR output for EN/FR/AR documents";
    public string Category => "ocr";
    public bool IsEnabledByDefault => true;
    public int Priority => 10;

    public MultilingualOcrCleanerPlugin(
        ILogger<MultilingualOcrCleanerPlugin> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _enabled = configuration.GetValue<bool>("Plugins:Ged.Ocr.MultilingualCleaner:Enabled", true);
    }

    public Task InitializeAsync()
    {
        _logger.LogInformation("Initialized Multilingual OCR Cleaner plugin");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        _logger.LogInformation("Shutdown Multilingual OCR Cleaner plugin");
        return Task.CompletedTask;
    }

    public async Task<string> ProcessOcrTextAsync(string ocrText, Document document, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ocrText))
            return ocrText;

        var result = ocrText;

        result = FixCommonOcrErrors(result);
        result = NormalizeWhitespace(result);
        result = FixBrokenWords(result);
        result = await FixEncodingIssuesAsync(result);

        _logger.LogDebug("OCR cleaned for document {DocId}: {OriginalLen} -> {CleanedLen} chars",
            document.Id, ocrText.Length, result.Length);

        return result;
    }

    private static string FixCommonOcrErrors(string text)
    {
        var sb = new StringBuilder(text);

        sb.Replace("|", "I");
        sb.Replace("0", "O");
        sb.Replace("rn", "m");
        sb.Replace("vv", "w");
        sb.Replace("—", "-");
        sb.Replace("–", "-");

        return sb.ToString();
    }

    private static string NormalizeWhitespace(string text)
    {
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n\s*\n\s*\n+", "\n\n");
        text = text.Trim();

        return text;
    }

    private static string FixBrokenWords(string text)
    {
        var lines = text.Split('\n');
        var sb = new StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (i < lines.Length - 1 && !string.IsNullOrWhiteSpace(line) && 
                !line.EndsWith('.') && !line.EndsWith(',') && !line.EndsWith('!') && 
                !line.EndsWith('?') && !line.EndsWith(':') && !line.EndsWith(';') &&
                !line.EndsWith('"') && !line.EndsWith('\'') && !line.EndsWith(')'))
            {
                var nextLine = lines[i + 1].TrimStart();
                if (!string.IsNullOrWhiteSpace(nextLine) && char.IsLower(nextLine.FirstOrDefault()))
                {
                    line += " " + nextLine;
                    lines[i + 1] = "";
                    i++;
                }
            }

            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private Task<string> FixEncodingIssuesAsync(string text)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var valid = Encoding.UTF8.GetString(bytes);
            return Task.FromResult(valid);
        }
        catch
        {
            return Task.FromResult(text);
        }
    }
}
