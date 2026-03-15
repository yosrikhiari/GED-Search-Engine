using GED.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace GED.Infrastructure.Services;

/// <summary>
/// Text extraction using Apache Tika Server (REST API).
///
/// FIXES vs original:
///   1. Set Accept: text/plain header so Tika returns plain text instead of
///      XHTML. The original sent no Accept header, so Tika defaulted to
///      returning an XHTML document — this caused extracted_text and
///      description fields to contain raw XML like
///      "<?xml version="1.1"...><html xmlns=...>" instead of document content.
///   2. Strip any residual XML/HTML that slips through (defensive cleanup).
///   3. Timeout added per request (Tika can hang on corrupt files).
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
        _tikaEnabled  = configuration.GetValue<bool>("Tika:Enabled", true);

        // Set a sensible default timeout on the shared HttpClient
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

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task<string> ExtractWithTikaAsync(
        Stream fileStream,
        string contentType,
        CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await fileStream.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;

        using var content = new StreamContent(ms);
        content.Headers.ContentType =
            new MediaTypeHeaderValue(contentType);

        // ── FIX: Tell Tika we want plain text, not XHTML ──────────────────
        // Without this header Tika defaults to returning an XHTML document
        // wrapping the extracted text, so callers receive raw XML instead of
        // the actual document content.
        content.Headers.Add("Accept", "text/plain");

        var tikaEndpoint = $"{_tikaUrl.TrimEnd('/')}/tika";

        _logger.LogDebug(
            "Sending {Bytes} bytes to Tika ({ContentType}) → {Url}",
            ms.Length, contentType, tikaEndpoint);

        using var request = new HttpRequestMessage(HttpMethod.Put, tikaEndpoint)
        {
            Content = content
        };
        // Explicitly request plain text in the Accept header on the request too
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

        // ── Defensive cleanup: strip any residual XML/HTML ────────────────
        // If Tika ignores the Accept header (older versions), do a best-effort
        // strip so downstream services don't receive markup.
        if (text.TrimStart().StartsWith("<?xml") ||
            text.TrimStart().StartsWith("<html"))
        {
            _logger.LogWarning(
                "Tika returned XML/HTML despite Accept: text/plain — stripping markup");
            text = StripHtmlTags(text);
        }

        _logger.LogInformation(
            "✅ Tika extracted {Chars} chars from {ContentType}",
            text.Length, contentType);

        return text;
    }

    /// <summary>
    /// Very simple HTML/XML tag stripper used only as a fallback when Tika
    /// ignores the Accept: text/plain header.
    /// </summary>
    private static string StripHtmlTags(string html)
    {
        // Remove tags
        var stripped = System.Text.RegularExpressions.Regex.Replace(
            html, "<[^>]+>", " ");

        // Decode common HTML entities
        stripped = stripped
            .Replace("&amp;",  "&")
            .Replace("&lt;",   "<")
            .Replace("&gt;",   ">")
            .Replace("&quot;", "\"")
            .Replace("&apos;", "'")
            .Replace("&#160;", " ")
            .Replace("&nbsp;", " ");

        // Collapse whitespace
        stripped = System.Text.RegularExpressions.Regex.Replace(
            stripped, @"\s{2,}", " ").Trim();

        return stripped;
    }
}