using System.Text.Json;
using System.Net.Http.Json;
using GED.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace GED.Infrastructure.Services;

/// <summary>
/// Extracts document dates using a local LLM (Ollama).
/// 
/// This service analyzes document text to identify the primary date (e.g., effective date
/// for contracts, invoice date for invoices) and returns it in a structured format with
/// confidence scores.
/// 
/// The extraction is asynchronous and runs after initial document upload, ensuring the
/// user experience isn't blocked by potentially slow LLM calls.
/// </summary>
public class DocumentDateExtractor
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DocumentDateExtractor> _logger;

    /// <summary>
    /// Ollama API endpoint for date extraction requests.
    /// </summary>
    private readonly string? _llmEndpoint;

    /// <summary>
    /// Ollama model name for date extraction.
    /// </summary>
    private readonly string? _llmModel;

    /// <summary>
    /// Whether the date extraction service is enabled.
    /// </summary>
    private readonly bool _enabled;

    /// <summary>
    /// Initializes a new instance of <see cref="DocumentDateExtractor"/>.
    /// </summary>
    /// <param name="httpClient">HTTP client for Ollama API calls.</param>
    /// <param name="logger">Logger for extraction events.</param>
    /// <param name="configuration">Application configuration with NLP:* settings.</param>
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
    /// Extracts the primary date from a document's text content.
    /// </summary>
    /// <param name="content">Document text content to analyze.</param>
    /// <param name="fileName">Original filename for context.</param>
    /// <param name="category">Document category (e.g., "Invoice", "Contract").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see cref="DocumentDateInfo"/> with extracted date, or null if extraction failed.</returns>
    /// <remarks>
    /// Uses category-specific prompts to improve date detection accuracy:
    /// - Contracts: looks for "Effective Date" or "entered into as of"
    /// - Invoices: looks for "Invoice Date"
    /// Returns null if NLP is disabled or content is empty.
    /// </remarks>
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
            // Take first 3000 characters for efficient processing
            var preview = content.Length > 3000 
                ? content.Substring(0, 3000) + "..." 
                : content;

            // Build category-specific prompt for better date extraction
            var prompt = $@"Extract the main date from this {category} document.

DOCUMENT TEXT:
{preview}

INSTRUCTIONS:
1. Find the primary date in this document
2. For contracts: look for ""Effective Date"" or ""entered into as of""
3. For invoices: look for ""Invoice Date""
4. Convert to YYYY-MM-DD format (e.g., ""December 1, 2024"" becomes ""2024-12-01"")

Respond with ONLY this JSON format (no extra text, no markdown, no backticks):
{{""date"":""2024-12-01"",""confidence"":0.9}}

If no date found:
{{""date"":null,""confidence"":0.0}}";

            _logger.LogInformation("🗓️ Extracting document date for {Category}: {FileName}", 
                category, fileName);

            // Call LLM for date extraction
            var response = await CallLlmAsync(prompt, cancellationToken);
            
            if (string.IsNullOrWhiteSpace(response))
            {
                _logger.LogWarning("LLM returned empty response");
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

    /// <summary>
    /// Calls the Ollama API for date extraction.
    /// </summary>
    /// <param name="prompt">The extraction prompt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>LLM response text, or null on error.</returns>
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
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("LLM API error: {StatusCode} - {Error}", response.StatusCode, error);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken);
            
            if (result?.Response != null)
            {
                _logger.LogInformation("📝 LLM raw response: {Response}", result.Response);
                return result.Response;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling LLM for date extraction");
            return null;
        }
    }

    /// <summary>
    /// Parses the LLM response into a <see cref="DocumentDateInfo"/> object.
    /// Uses multiple parsing strategies to handle different response formats.
    /// </summary>
    /// <param name="response">Raw LLM response text.</param>
    /// <returns>Parsed <see cref="DocumentDateInfo"/>, or null on parse failure.</returns>
    private DocumentDateInfo? ParseDateResponse(string response)
    {
        try
        {
            // Clean the LLM response (remove markdown, extract JSON)
            var cleaned = CleanJsonResponse(response);
            
            _logger.LogInformation("🧹 Cleaned JSON: {Cleaned}", cleaned);

            // Strategy 1: Try parsing simple format {"date":"2024-12-01","confidence":0.9}
            if (TryParseSimpleFormat(cleaned, out var simpleResult))
            {
                return simpleResult;
            }

            // Strategy 2: Try parsing detailed format with all fields
            if (TryParseDetailedFormat(cleaned, out var detailedResult))
            {
                return detailedResult;
            }

            // Strategy 3: Fallback to regex extraction for YYYY-MM-DD patterns
            if (TryExtractDateWithRegex(response, out var regexResult))
            {
                return regexResult;
            }

            _logger.LogWarning("Could not parse LLM response: {Response}", response);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM date response: {Response}", response);
            return null;
        }
    }

    /// <summary>
    /// Cleans JSON response by removing markdown code fences and extracting
    /// only the JSON object from potentially mixed output.
    /// </summary>
    /// <param name="response">Raw LLM response.</param>
    /// <returns>Cleaned JSON string.</returns>
    private string CleanJsonResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "{}";

        var cleaned = response.Trim();

        // Remove markdown code blocks
        cleaned = Regex.Replace(cleaned, @"^```(json)?\s*", "", RegexOptions.Multiline);
        cleaned = Regex.Replace(cleaned, @"```\s*$", "", RegexOptions.Multiline);

        // Remove any leading/trailing whitespace again
        cleaned = cleaned.Trim();

        // If response contains explanation text before JSON, extract just the JSON
        var jsonMatch = Regex.Match(cleaned, @"\{.*\}", RegexOptions.Singleline);
        if (jsonMatch.Success)
        {
            cleaned = jsonMatch.Value;
        }

        return cleaned;
    }

    /// <summary>
    /// Parses simple date format: {"date":"2024-12-01","confidence":0.9}
    /// </summary>
    private bool TryParseSimpleFormat(string json, out DocumentDateInfo? result)
    {
        result = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            DateTime? date = null;
            if (root.TryGetProperty("date", out var dateElement) && 
                dateElement.ValueKind == JsonValueKind.String)
            {
                var dateStr = dateElement.GetString();
                if (!string.IsNullOrWhiteSpace(dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
                {
                    date = parsedDate;
                }
            }

            float confidence = 0.0f;
            if (root.TryGetProperty("confidence", out var confElement))
            {
                confidence = (float)confElement.GetDouble();
            }

            result = new DocumentDateInfo
            {
                DocumentDate = date,
                Confidence = confidence,
                DateType = date != null ? "extracted" : "none",
                Explanation = date != null ? "Found date in document" : "No date found"
            };

            _logger.LogInformation("✅ Parsed simple format: Date={Date}, Confidence={Conf}", 
                date?.ToString("yyyy-MM-dd") ?? "null", confidence);
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses detailed date format with all optional fields.
    /// </summary>
    private bool TryParseDetailedFormat(string json, out DocumentDateInfo? result)
    {
        result = null;
        try
        {
            var dateInfo = JsonSerializer.Deserialize<DocumentDateInfo>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dateInfo != null)
            {
                _logger.LogInformation("✅ Parsed detailed format: Date={Date}, Confidence={Conf}, Type={Type}", 
                    dateInfo.DocumentDate?.ToString("yyyy-MM-dd") ?? "null", 
                    dateInfo.Confidence,
                    dateInfo.DateType);
                
                result = dateInfo;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Fallback extraction using regex to find YYYY-MM-DD patterns.
    /// </summary>
    private bool TryExtractDateWithRegex(string response, out DocumentDateInfo? result)
    {
        result = null;
        try
        {
            // Look for YYYY-MM-DD pattern
            var datePattern = @"\b(20\d{2})-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])\b";
            var match = Regex.Match(response, datePattern);

            if (match.Success && DateTime.TryParse(match.Value, out var date))
            {
                result = new DocumentDateInfo
                {
                    DocumentDate = date,
                    Confidence = 0.7f, // Lower confidence for regex extraction
                    DateType = "regex_extracted",
                    Explanation = "Extracted using regex pattern"
                };

                _logger.LogInformation("✅ Regex extracted date: {Date}", date.ToString("yyyy-MM-dd"));
                return true;
            }

            return false;
        }
        catch
        {
            return false;
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

/// <summary>
/// Information about a document's extracted date.
/// </summary>
public class DocumentDateInfo
{
    /// <summary>
    /// The extracted date in UTC.
    /// </summary>
    [JsonPropertyName("documentDate")]
    public DateTime? DocumentDate { get; set; }
    
    /// <summary>
    /// Confidence score (0.0 to 1.0) indicating extraction reliability.
    /// </summary>
    [JsonPropertyName("confidence")]
    public float Confidence { get; set; }
    
    /// <summary>
    /// How the date was extracted (e.g., "extracted", "regex_extracted", "none").
    /// </summary>
    [JsonPropertyName("dateType")]
    public string DateType { get; set; } = "unknown";
    
    /// <summary>
    /// Human-readable explanation of how the date was found.
    /// </summary>
    [JsonPropertyName("explanation")]
    public string? Explanation { get; set; }

    /// <summary>
    /// Alternative property name for date (used when LLM returns "date" instead of "documentDate").
    /// </summary>
    [JsonPropertyName("date")]
    public string? DateString
    {
        get => DocumentDate?.ToString("yyyy-MM-dd");
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && DateTime.TryParse(value, out var date))
            {
                DocumentDate = date;
            }
        }
    }
}
