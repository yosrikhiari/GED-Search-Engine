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
/// Runs AFTER OcrTextCleaningService so it receives clean, readable text.
/// Returns null on any failure — callers fall back to keyword-based tags.
/// </summary>
public class OcrMetadataEnrichmentService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OcrMetadataEnrichmentService> _logger;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly bool _enabled;

    private const int MaxTextChars        = 3000;
    private const int TimeoutSeconds      = 60;
    private const int MinTags             = 4;
    private const int MaxTags             = 12;
    private const int MaxDescriptionChars = 300;

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

    public class EnrichmentResult
    {
        public List<string> Tags { get; set; } = new();
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// Generates AI tags and description from OCR text.
    /// Returns null on failure — never throws.
    /// </summary>
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
                "🏷️  Enriching metadata with Ollama for {FileName} ({Chars} chars)",
                fileName, ocrText.Length);

            var preview = ocrText.Length > MaxTextChars
                ? ocrText[..MaxTextChars] + "…"
                : ocrText;

            var prompt = BuildPrompt(preview, fileName, category);

            var requestBody = new
            {
                model       = _model,
                prompt      = prompt,
                stream      = false,
                temperature = 0.2,
                format      = "json",
                options     = new { num_predict = 512 }
            };

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

    // ── Private helpers ───────────────────────────────────────────────────────

private static string BuildPrompt(string text, string fileName, string category)
{
    return $@"You are a document analysis assistant. Analyze this {category} document and return metadata.

DOCUMENT FILE: {fileName}
DOCUMENT TEXT:
{text}

PREDEFINED TAG VOCABULARY — prefer tags from this list when they apply:
Document types: invoice, contract, report, letter, memo, presentation, spreadsheet, receipt, order, quote, agreement, minutes, notice, certificate, permit, visa, passport, id-card, bank-statement, payslip, tax-return, insurance, medical, legal
Status/action: signed, draft, pending, approved, rejected, cancelled, expired, urgent, confidential, archived
Time: 2020, 2021, 2022, 2023, 2024, 2025, q1, q2, q3, q4
Topics: finance, accounting, hr, legal, procurement, logistics, it, marketing, operations, real-estate, construction, healthcare, education
Languages: french, english, arabic

Return ONLY a JSON object (no markdown, no explanation) with exactly these two fields:

1. ""tags"": array of 4-12 lowercase tags. Use predefined tags when they fit. You MAY add free-form tags for specific names, organizations, project names, or amounts not covered above.
   Do NOT include generic tags like ""document"", ""text"", ""file"", ""content""

2. ""description"": a single plain-text sentence (max 250 characters) summarizing what this document is about.

Example output:
{{""tags"":[""invoice"",""finance"",""2024"",""acme-corp"",""approved""],""description"":""Invoice #1042 from Acme Corp for consulting services rendered in Q3 2024, total amount 12,500 EUR.""}}

Now analyze the document above and return JSON:";
}


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

            // Parse description
            string description = string.Empty;
            if (root.TryGetProperty("description", out var descEl) &&
                descEl.ValueKind == JsonValueKind.String)
            {
                description = descEl.GetString()?.Trim() ?? string.Empty;
                if (description.Length > MaxDescriptionChars)
                    description = description[..(MaxDescriptionChars - 3)] + "...";
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                _logger.LogWarning("Ollama returned empty description — enrichment partial");
            }

            _logger.LogInformation(
                "✅ Metadata enriched: {TagCount} tags, {DescLen} char description",
                tags.Count, description.Length);

            return new EnrichmentResult { Tags = tags, Description = description };
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

    private class OllamaResponse
    {
        public string? Response { get; set; }
    }
}