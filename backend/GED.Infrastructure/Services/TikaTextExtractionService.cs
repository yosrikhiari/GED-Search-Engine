using GED.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace GED.Infrastructure.Services;

/// <summary>
/// Text extraction using Apache Tika Server (REST API), with built-in fallback.
/// 
/// <para>
/// This service is the primary <see cref="ITextExtractionService"/> implementation
/// registered in DI. It attempts extraction via Tika Server first, then falls back
/// to <see cref="TextExtractionService"/> (built-in parsers) if Tika is unavailable,
/// disabled, or returns an error.
/// </para>
/// 
/// <para>
/// Tika supports: PDF, DOC/DOCX, XLSX, PPTX, RTF, HTML, XML, EPUB, plain text.
/// Fallback covers: PDF (iText), DOCX, XLSX, plain text.
/// If both fail, the pipeline continues without extracted text.
/// </para>
/// 
/// <para>
/// This service handles synchronous text extraction during upload.
/// For scanned/image documents, OCR runs asynchronously in <see cref="OcrWorkerService"/>.
/// Documents with 100+ chars of native text skip OCR entirely.
/// </para>
/// 
/// <para>
/// Tika Metadata Extraction:
/// This service can also extract metadata from documents by calling the /meta endpoint.
/// Available metadata fields vary by file type but typically include:
/// - dc:title, dc:description - Document title and description
/// - Content-Type - MIME type
/// - dcterms:created, dcterms:modified - Creation and modification dates
/// - dc:creator, meta:author - Author information
/// - meta:keywords - Keywords/tags
/// - For PDFs: pdf:creator, pdf:producer, xmp:CreatorTool
/// - For Office: cp:category, cp:subject
/// </para>
/// </summary>
public class TikaTextExtractionService : ITextExtractionService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TikaTextExtractionService> _logger;
    private readonly ITextExtractionService _fallback;
    private readonly string _tikaUrl;
    private readonly bool _tikaEnabled;

    private static readonly TimeSpan TikaTimeout = TimeSpan.FromSeconds(60);

    private static readonly HashSet<string> TikaSupportedTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
            "text/plain",
            "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.ms-excel",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/vnd.ms-powerpoint",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "application/rtf",
            "text/html",
            "text/xml",
            "application/xml",
            "application/epub+zip"
        };

    public TikaTextExtractionService(
        HttpClient httpClient,
        ILogger<TikaTextExtractionService> logger,
        ITextExtractionService fallback,
        IConfiguration configuration)
    {
        _httpClient   = httpClient;
        _logger       = logger;
        _fallback     = fallback;
        _tikaUrl      = configuration["Tika:Url"] ?? "http://localhost:9998";
        
        var enabledStr = configuration["Tika:Enabled"];
        _tikaEnabled = bool.TryParse(enabledStr, out var enabled) ? enabled : true;

        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan ||
            _httpClient.Timeout > TikaTimeout)
        {
            _httpClient.Timeout = TikaTimeout;
        }
    }

    public async Task<string> ExtractTextAsync(
        Stream fileStream,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (!_tikaEnabled)
        {
            _logger.LogDebug("Tika disabled — using fallback for {ContentType}", contentType);
            return await _fallback.ExtractTextAsync(fileStream, contentType, cancellationToken);
        }

        try
        {
            return await ExtractWithTikaAsync(fileStream, contentType, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Tika extraction failed for {ContentType} — falling back to built-in extractor",
                contentType);

            if (fileStream.CanSeek) fileStream.Position = 0;
            return await _fallback.ExtractTextAsync(fileStream, contentType, cancellationToken);
        }
    }

    public Task<bool> SupportsContentType(string contentType)
    {
        if (_tikaEnabled && TikaSupportedTypes.Contains(contentType))
            return Task.FromResult(true);

        return _fallback.SupportsContentType(contentType);
    }

    /// <summary>
    /// Extracts metadata from a document using Tika's /meta endpoint.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of metadata key-value pairs.</returns>
    public async Task<Dictionary<string, string>> ExtractMetadataAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!_tikaEnabled)
        {
            return metadata;
        }

        try
        {
            var metaEndpoint = $"{_tikaUrl.TrimEnd('/')}/meta";

            _logger.LogDebug("Extracting metadata from {FilePath} via Tika", filePath);

            using var fileStream = File.OpenRead(filePath);
            using var content = new StreamContent(fileStream);

            using var request = new HttpRequestMessage(HttpMethod.Put, metaEndpoint)
            {
                Content = content
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var metaText = await response.Content.ReadAsStringAsync(cancellationToken);
                metadata = ParseTikaMetadata(metaText);
                
                _logger.LogInformation("✅ Tika extracted {Count} metadata fields from {FilePath}",
                    metadata.Count, Path.GetFileName(filePath));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tika metadata extraction failed for {FilePath}", filePath);
        }

        return metadata;
    }

    /// <summary>
    /// Parses Tika's metadata output format (key: value per line).
    /// </summary>
    private static Dictionary<string, string> ParseTikaMetadata(string metaText)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(metaText))
            return metadata;

        var lines = metaText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex > 0)
            {
                var key = line[..colonIndex].Trim();
                var value = line[(colonIndex + 1)..].Trim();
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                {
                    metadata[key] = value;
                }
            }
        }

        return metadata;
    }

    /// <summary>
    /// Maps Tika metadata to category and description.
    /// </summary>
    public static (string? category, string? description) MapMetadataToFields(
        Dictionary<string, string> metadata, string fileName)
    {
        string? category = null;
        string? description = null;

        // Map Content-Type to category
        if (metadata.TryGetValue("Content-Type", out var contentType))
        {
            category = contentType.ToLower() switch
            {
                var ct when ct.Contains("pdf") => "Document",
                var ct when ct.Contains("word") || ct.Contains("document") => "Document",
                var ct when ct.Contains("spreadsheet") || ct.Contains("excel") => "Spreadsheet",
                var ct when ct.Contains("presentation") || ct.Contains("powerpoint") => "Presentation",
                var ct when ct.Contains("image") => "Image",
                var ct when ct.Contains("text") => "Letter",
                _ => "Other"
            };
        }

        // Map dc:description or dc:title to description
        if (metadata.TryGetValue("dc:description", out var desc) && !string.IsNullOrWhiteSpace(desc))
        {
            description = desc.Length > 300 ? desc[..297] + "..." : desc;
        }
        else if (metadata.TryGetValue("dc:title", out var title) && !string.IsNullOrWhiteSpace(title) && title != Path.GetFileNameWithoutExtension(fileName))
        {
            description = title.Length > 300 ? title[..297] + "..." : title;
        }
        else if (metadata.TryGetValue("meta:subject", out var subject) && !string.IsNullOrWhiteSpace(subject))
        {
            description = subject.Length > 300 ? subject[..297] + "..." : subject;
        }

        return (category, description);
    }

    public Task SimulateFailureAsync()
    {
        throw new HttpRequestException("Simulated Tika failure for testing");
    }

    private async Task<string> ExtractWithTikaAsync(
        Stream fileStream,
        string contentType,
        CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await fileStream.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;

        using var content = new StreamContent(ms);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Headers.Add("Accept", "text/plain");

        var tikaEndpoint = $"{_tikaUrl.TrimEnd('/')}/tika";

        _logger.LogDebug(
            "Sending {Bytes} bytes to Tika ({ContentType}) → {Url}",
            ms.Length, contentType, tikaEndpoint);

        using var request = new HttpRequestMessage(HttpMethod.Put, tikaEndpoint)
        {
            Content = content
        };
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception(
                $"Tika returned {(int)response.StatusCode}: {errorBody[..Math.Min(200, errorBody.Length)]}");
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (text.TrimStart().StartsWith("<?xml") || text.TrimStart().StartsWith("<html"))
        {
            _logger.LogWarning("Tika returned XML/HTML despite Accept: text/plain — stripping markup");
            text = StripHtmlTags(text);
        }

        _logger.LogInformation("✅ Tika extracted {Chars} chars from {ContentType}", text.Length, contentType);

        return text;
    }

    private static string StripHtmlTags(string html)
    {
        var stripped = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        stripped = stripped.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&#160;", " ").Replace("&nbsp;", " ");
        stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"\s{2,}", " ").Trim();
        return stripped;
    }
}