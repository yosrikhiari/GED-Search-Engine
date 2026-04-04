using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GED.Infrastructure.Services;

/// <summary>
/// Uses Ollama (local LLM) to generate meaningful tags and a human-readable
/// description from OCR-extracted document text.
/// 
/// <para>
/// This service runs AFTER <see cref="OcrTextCleaningService"/> so it receives
/// clean, readable text rather than raw OCR output.
/// </para>
/// 
/// <para>
/// The enrichment includes:
/// <list type="bullet">
///   <item>
///     <term>Tags</term>
///     <description>
///       4-12 lowercase tags from a predefined vocabulary plus free-form tags
///       for specific entities (names, organizations, amounts).
///     </description>
///   </item>
///   <item>
///     <term>Description</term>
///     <description>
///       A single plain-text sentence summarizing the document (max 300 chars).
///     </description>
///   </item>
/// </list>
/// </para>
/// 
/// <para>
/// Returns null on any failure — callers fall back to keyword-based tags.
/// </para>
/// </summary>
public class OcrMetadataEnrichmentService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OcrMetadataEnrichmentService> _logger;

    /// <summary>
    /// Ollama API endpoint for metadata enrichment requests.
    /// </summary>
    private readonly string _endpoint;

    /// <summary>
    /// Ollama model name for enrichment.
    /// </summary>
    private readonly string _model;

    /// <summary>
    /// Whether the enrichment service is enabled.
    /// </summary>
    private readonly bool _enabled;

    /// <summary>
    /// Maximum characters sent to LLM (keeps prompt under context window).
    /// </summary>
    private const int MaxTextChars = 3000;

    /// <summary>
    /// Timeout for LLM requests in seconds.
    /// </summary>
    private const int TimeoutSeconds = 60;

    /// <summary>
    /// Minimum tags required for successful enrichment.
    /// </summary>
    private const int MinTags = 4;

    /// <summary>
    /// Maximum tags returned by the LLM.
    /// </summary>
    private const int MaxTags = 12;

    /// <summary>
    /// Maximum characters in generated description.
    /// </summary>
    private const int MaxDescriptionChars = 300;

    /// <summary>
    /// Initializes a new instance of <see cref="OcrMetadataEnrichmentService"/>.
    /// </summary>
    /// <param name="httpClient">HTTP client for Ollama API calls.</param>
    /// <param name="logger">Logger for enrichment events.</param>
    /// <param name="configuration">Application configuration with NLP:* settings.</param>
    public OcrMetadataEnrichmentService(
        HttpClient httpClient,
        ILogger<OcrMetadataEnrichmentService> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger     = logger;
        _endpoint   = configuration["NLP:LlmApiEndpoint"] ?? "http://localhost:11434/api/generate";
        _model      = configuration["NLP:Model"] ?? "llama3.2";
        _enabled    = configuration.GetValue<bool>("NLP:Enabled", true);
    }

    /// <summary>
    /// Result of metadata enrichment containing tags.
    /// Note: Description is now provided by Tika metadata extraction, not LLM.
    /// This property is kept for backward compatibility but is not populated.
    /// </summary>
    public class EnrichmentResult
    {
        /// <summary>
        /// Extracted tags from the document.
        /// </summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>
        /// Description - now provided by Tika metadata, not LLM.
        /// Kept for backward compatibility but no longer populated.
        /// </summary>
        [Obsolete("Description is now provided by Tika metadata extraction")]
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// Generates AI tags from OCR text. 
    /// Description is now provided by Tika metadata extraction.
    /// </summary>
    /// <param name="ocrText">Cleaned OCR text to analyze.</param>
    /// <param name="fileName">Original filename for context.</param>
    /// <param name="category">Document category.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="EnrichmentResult"/> with tags, or null on failure.
    /// </returns>
    /// <remarks>
    /// Returns null on any failure — never throws.
    /// Callers should fall back to keyword-based tags when null is returned.
    /// </remarks>
    public async Task<EnrichmentResult?> EnrichAsync(
        string ocrText,
        string fileName,
        string category,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(ocrText))
            return null;

        try
        {
            _logger.LogInformation(
                "🏷️  Enriching tags with Ollama for {FileName} ({Chars} chars)",
                fileName, ocrText.Length);

            // Truncate text to max chars for efficiency
            var preview = ocrText.Length > MaxTextChars
                ? ocrText[..MaxTextChars] + "…"
                : ocrText;

            var prompt = BuildPrompt(preview, fileName, category);

            var requestBody = new
            {
                model       = _model,
                prompt      = prompt,
                stream      = false,
                temperature = 0.2,  // Low temperature for consistent output
                format      = "json",
                options     = new { num_predict = 512 }
            };

            // Set timeout for LLM request
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            var response = await _httpClient.PostAsJsonAsync(_endpoint, requestBody, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ollama returned {Status} for metadata enrichment", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(cts.Token);
            var raw    = result?.Response?.Trim();

            if (string.IsNullOrWhiteSpace(raw))
            {
                _logger.LogWarning("Ollama returned empty response for enrichment");
                return null;
            }

            _logger.LogDebug("Ollama enrichment raw: {Raw}", raw.Length > 300 ? raw[..300] : raw);

            return ParseResult(raw);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Ollama enrichment timed out for {FileName}", fileName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata enrichment failed for {FileName} — skipping", fileName);
            return null;
        }
    }

    /// <summary>
    /// Builds the prompt for metadata enrichment with predefined tag vocabulary.
    /// Note: Description is now provided by Tika metadata extraction, not LLM.
    /// </summary>
    private static string BuildPrompt(string text, string fileName, string category)
    {
        return $@"You are a document analysis assistant. Analyze this {category} document and return tags.

DOCUMENT FILE: {fileName}
DOCUMENT TEXT:
{text}

PREDEFINED TAG VOCABULARY — prefer tags from this list when they apply:
Document types: invoice, contract, report, letter, memo, presentation, spreadsheet, receipt, order, quote, agreement, minutes, notice, certificate, permit, visa, passport, id-card, bank-statement, payslip, tax-return, insurance, medical, legal
Status/action: signed, draft, pending, approved, rejected, cancelled, expired, urgent, confidential, archived
Time: 2020, 2021, 2022, 2023, 2024, 2025, q1, q2, q3, q4
Topics: finance, accounting, hr, legal, procurement, logistics, it, marketing, operations, real-estate, construction, healthcare, education
Languages: french, english, arabic

Return ONLY a JSON object (no markdown, no explanation) with exactly this field:

""tags"": array of 4-12 lowercase tags. Use predefined tags when they fit. You MAY add free-form tags for specific names, organizations, project names, or amounts not covered above.
Do NOT include generic tags like ""document"", ""text"", ""file"", ""content""

Example output:
{{""tags"":[""invoice"",""finance"",""2024"",""acme-corp"",""approved""]}}

Now analyze the document above and return JSON:";
    }

    /// <summary>
    /// Parses the LLM response into an <see cref="EnrichmentResult"/>.
    /// </summary>
    /// <param name="raw">Raw LLM response text.</param>
    /// <returns>Parsed enrichment result, or null on parse failure.</returns>
    private EnrichmentResult? ParseResult(string raw)
    {
        try
        {
            // Strip markdown fences if present
            var cleaned = Regex.Replace(raw, @"^```(json)?\s*", "", RegexOptions.Multiline);
            cleaned     = Regex.Replace(cleaned, @"```\s*$", "", RegexOptions.Multiline).Trim();

            // Extract JSON object if there's surrounding text
            var jsonMatch = Regex.Match(cleaned, @"\{.*\}", RegexOptions.Singleline);
            if (jsonMatch.Success) cleaned = jsonMatch.Value;

            using var doc  = JsonDocument.Parse(cleaned);
            var root       = doc.RootElement;

            // Parse tags
            var tags = new List<string>();
            if (root.TryGetProperty("tags", out var tagsEl) &&
                tagsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tagsEl.EnumerateArray())
                {
                    var tag = t.GetString()?.Trim().ToLower();
                    if (!string.IsNullOrWhiteSpace(tag) && tag.Length > 1)
                        tags.Add(tag);
                }
            }

            // Enforce tag count
            if (tags.Count < MinTags)
            {
                _logger.LogWarning("Ollama returned only {Count} tags (min {Min}) — enrichment skipped", tags.Count, MinTags);
                return null;
            }
            if (tags.Count > MaxTags)
                tags = tags[..MaxTags];

            // Description is now provided by Tika metadata extraction, not LLM
            // Keep backward compatibility - don't fail if LLM still returns description

            _logger.LogInformation(
                "✅ Metadata enriched: {TagCount} tags",
                tags.Count);

            return new EnrichmentResult { Tags = tags };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse enrichment JSON: {Raw}", raw.Length > 200 ? raw[..200] : raw);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error parsing enrichment result");
            return null;
        }
    }

    /// <summary>
    /// Ollama API response wrapper.
    /// </summary>
    private class OllamaResponse
    {
        /// <summary>
        /// The generated text response from Ollama.
        /// </summary>
        public string? Response { get; set; }
    }
}
