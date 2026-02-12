using System.Text.Json;
using System.Net.Http.Json;
using GED.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

/// <summary>
/// Service that uses LLM to intelligently extract the actual document date from content
/// (e.g., contract effective date, invoice date, letter date)
/// </summary>
public class DocumentDateExtractor
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DocumentDateExtractor> _logger;
    private readonly string? _llmEndpoint;
    private readonly string? _llmModel;
    private readonly bool _enabled;

    public DocumentDateExtractor(
        HttpClient httpClient,
        ILogger<DocumentDateExtractor> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        _llmEndpoint = configuration["NLP:LlmApiEndpoint"] ?? "http://localhost:11434/api/generate";
        _llmModel = configuration["NLP:Model"] ?? "llama3.2";
        _enabled = configuration.GetValue<bool>("NLP:Enabled", true);
    }

    /// <summary>
    /// Extract the primary document date from text content using LLM
    /// </summary>
    public async Task<DocumentDateInfo?> ExtractDocumentDateAsync(
        string content,
        string fileName,
        string category,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            // ⭐ IMPROVED: Take first 3000 characters (contracts often have long preambles)
            var preview = content.Length > 3000 
                ? content.Substring(0, 3000) + "..." 
                : content;

            // ⭐ IMPROVED: Much more specific prompt with examples
            var prompt = $@"Analyze this document and extract the MOST RELEVANT DATE for this type of document.

Document Category: {category}
Filename: {fileName}

Document Content:
{preview}

CRITICAL RULES FOR DATE EXTRACTION:

1. For CONTRACTS:
   - Extract ONLY the ""Effective Date"", ""Agreement Date"", or ""as of"" date
   - Look for exact phrases like:
     * ""as of December 1, 2024""
     * ""Effective Date: December 1, 2024""
     * ""entered into as of December 1, 2024""
   - DO NOT use signature dates, execution dates, or filing dates
   - The effective date is usually in the first few lines of the contract

2. For INVOICES:
   - Extract ONLY the ""Invoice Date"" or main ""Date:"" field
   - Look for:
     * ""Date: January 15, 2024""
     * ""Invoice Date: January 15, 2024""
   - IGNORE ""Due Date"", ""Payment Terms"", or any future dates

3. For LETTERS:
   - Extract the date at the top of the letter (usually near the sender's address)

4. For REPORTS:
   - Extract the report period end date or publication date
   - Look for ""For the period ending..."" or ""Report Date:""

5. IMPORTANT RULES:
   - If you find multiple dates, choose the PRIMARY document date
   - Ignore payment dates, due dates, signature dates, or filing dates
   - The date should represent WHEN the document is dated, not when it expires or is due
   - Current date for reference: {DateTime.UtcNow:yyyy-MM-dd}

RESPOND WITH VALID JSON ONLY (no markdown code blocks, no extra text):
{{
  ""documentDate"": ""YYYY-MM-DD"",
  ""confidence"": 0.95,
  ""dateType"": ""contract_effective_date"",
  ""explanation"": ""Found 'as of December 1, 2024' in the opening line""
}}

If NO clear date is found, respond with:
{{
  ""documentDate"": null,
  ""confidence"": 0.0,
  ""dateType"": ""none"",
  ""explanation"": ""No clear document date found in content""
}}";

            _logger.LogInformation("🗓️ Extracting document date for {Category} document: {FileName}", 
                category, fileName);

            // Call LLM
            var response = await CallLlmAsync(prompt, cancellationToken);
            
            if (response == null)
            {
                _logger.LogWarning("LLM returned null response for date extraction");
                return null;
            }

            return ParseDateResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting document date");
            return null;
        }
    }

    private async Task<string?> CallLlmAsync(string prompt, CancellationToken cancellationToken)
    {
        try
        {
            var requestBody = new
            {
                model = _llmModel,
                prompt = prompt,
                stream = false,
                temperature = 0.1, // Low temperature for factual extraction
                format = "json"
            };

            var response = await _httpClient.PostAsJsonAsync(_llmEndpoint, requestBody, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken);
            
            return result?.Response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling LLM for date extraction");
            return null;
        }
    }

    private DocumentDateInfo? ParseDateResponse(string response)
    {
        try
        {
            // Clean up response - remove markdown code blocks
            var cleaned = response.Trim();
            if (cleaned.StartsWith("```json"))
            {
                cleaned = cleaned.Substring(7);
            }
            if (cleaned.StartsWith("```"))
            {
                cleaned = cleaned.Substring(3);
            }
            if (cleaned.EndsWith("```"))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 3);
            }
            cleaned = cleaned.Trim();

            var dateInfo = JsonSerializer.Deserialize<DocumentDateInfo>(cleaned);
            
            if (dateInfo?.DocumentDate != null)
            {
                _logger.LogInformation(
                    "✅ Extracted document date: {Date} (type: {Type}, confidence: {Confidence:F2}, explanation: {Explanation})",
                    dateInfo.DocumentDate.Value.ToString("yyyy-MM-dd"),
                    dateInfo.DateType,
                    dateInfo.Confidence,
                    dateInfo.Explanation
                );
            }
            else
            {
                _logger.LogInformation("❌ No document date found: {Explanation}", 
                    dateInfo?.Explanation ?? "Unknown");
            }

            return dateInfo;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM date response: {Response}", response);
            return null;
        }
    }

    private class OllamaResponse
    {
        public string? Response { get; set; }
    }
}

/// <summary>
/// Information about a document's extracted date
/// </summary>
public class DocumentDateInfo
{
    public DateTime? DocumentDate { get; set; }
    public float Confidence { get; set; }
    public string DateType { get; set; } = "unknown";
    public string? Explanation { get; set; }
}