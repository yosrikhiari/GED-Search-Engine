using GED.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GED.Infrastructure.Services;

public class DocumentMetadataService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DocumentMetadataService> _logger;
    private readonly ITextExtractionService _textExtractionService;
    private readonly string? _llmEndpoint;
    private readonly string? _llmModel;
    private readonly bool _enabled;

    public DocumentMetadataService(
        HttpClient httpClient,
        ILogger<DocumentMetadataService> logger,
        ITextExtractionService textExtractionService,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _textExtractionService = textExtractionService;
        
        // Support for local Ollama (Llama) or OpenAI-compatible APIs
        _llmEndpoint = configuration["NLP:LlmApiEndpoint"] ?? "http://localhost:11434/api/generate";
        _llmModel = configuration["NLP:Model"] ?? "llama3.2";
        _enabled = configuration.GetValue<bool>("NLP:Enabled", true);
    }

    public async Task<DocumentMetadataSuggestion> SuggestMetadataAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            _logger.LogInformation("NLP is disabled, returning fallback suggestion");
            return CreateFallbackSuggestion(fileName);
        }

        try
        {
            // Extract preview text from document
            var previewText = await ExtractPreviewTextAsync(fileStream, contentType, fileName, cancellationToken);
            
            if (string.IsNullOrWhiteSpace(previewText))
            {
                _logger.LogWarning("No text extracted from document, using fallback");
                return CreateFallbackSuggestion(fileName);
            }

            // Generate suggestions using LLM
            return await GenerateSuggestionsWithLlmAsync(previewText, fileName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating metadata suggestions for {FileName}", fileName);
            return CreateFallbackSuggestion(fileName);
        }
    }

    private async Task<string> ExtractPreviewTextAsync(
        Stream fileStream,
        string contentType,
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            // Reset stream position
            fileStream.Position = 0;

            string extractedText;

            // Handle text files directly
            if (contentType == "text/plain")
            {
                using var reader = new StreamReader(fileStream, leaveOpen: true);
                extractedText = await reader.ReadToEndAsync();
                fileStream.Position = 0;
            }
            // Try to extract text from PDFs and other documents
            else if (await _textExtractionService.SupportsContentType(contentType))
            {
                extractedText = await _textExtractionService.ExtractTextAsync(fileStream, contentType, cancellationToken);
                fileStream.Position = 0;
            }
            // For images and other files, use filename
            else
            {
                extractedText = $"Filename: {fileName}";
            }

            // Limit preview to first 1000 characters for efficiency
            var result = extractedText.Length > 1000 
                ? extractedText.Substring(0, 1000) + "..." 
                : extractedText;
                
            _logger.LogInformation("Extracted {Length} characters from {FileName}", result.Length, fileName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not extract preview text from {FileName}", fileName);
            return $"Filename: {fileName}";
        }
    }

    private async Task<DocumentMetadataSuggestion> GenerateSuggestionsWithLlmAsync(
        string previewText,
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            var prompt = $@"Analyze this document and suggest a clear title and appropriate category.

Document filename: {fileName}
Document preview:
{previewText}

Based on the content, suggest:
1. A clear, descriptive title (max 100 characters)
2. A single category from: Invoice, Contract, Report, Letter, Memo, Presentation, Spreadsheet, Image, Other
3. A confidence score (0.0 to 1.0) indicating how confident you are in your suggestions based on the available content

IMPORTANT: 
- If you can clearly identify the document type and content, use high confidence (0.8-1.0)
- If the content is ambiguous or limited, use medium confidence (0.5-0.7)
- If you're mostly guessing based on filename alone, use low confidence (0.3-0.5)

Respond ONLY with valid JSON in this exact format:
{{
  ""title"": ""suggested title here"",
  ""category"": ""category name here"",
  ""confidence"": 0.85
}}";

            // Check if using Ollama (local Llama)
            if (_llmEndpoint?.Contains("ollama") == true || _llmEndpoint?.Contains("11434") == true)
            {
                return await CallOllamaAsync(prompt, fileName, previewText, cancellationToken);
            }
            else
            {
                return await CallOpenAiCompatibleAsync(prompt, fileName, previewText, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling LLM for metadata suggestions");
            return CreateFallbackSuggestion(fileName);
        }
    }

    private async Task<DocumentMetadataSuggestion> CallOllamaAsync(
        string prompt,
        string fileName,
        string previewText,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            model = _llmModel,
            prompt = prompt,
            stream = false,
            temperature = 0.3,
            format = "json"
        };

        _logger.LogInformation("Calling Ollama API for {FileName}", fileName);

        var response = await _httpClient.PostAsJsonAsync(_llmEndpoint, requestBody, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken);
        
        if (result?.Response != null)
        {
            _logger.LogInformation("Received response from Ollama: {Response}", result.Response);
            return ParseLlmResponse(result.Response, fileName, previewText);
        }

        throw new Exception("Empty response from Ollama");
    }

    private async Task<DocumentMetadataSuggestion> CallOpenAiCompatibleAsync(
        string prompt,
        string fileName,
        string previewText,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            model = _llmModel ?? "gpt-3.5-turbo",
            messages = new[]
            {
                new { role = "system", content = "You are a document analysis assistant. Always respond with valid JSON." },
                new { role = "user", content = prompt }
            },
            temperature = 0.3,
            max_tokens = 200
        };

        _logger.LogInformation("Calling OpenAI-compatible API for {FileName}", fileName);

        var response = await _httpClient.PostAsJsonAsync(_llmEndpoint, requestBody, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OpenAiResponse>(cancellationToken);
        
        if (result?.Choices?.Length > 0)
        {
            var content = result.Choices[0].Message?.Content;
            if (!string.IsNullOrWhiteSpace(content))
            {
                _logger.LogInformation("Received response from OpenAI-compatible API: {Response}", content);
                return ParseLlmResponse(content, fileName, previewText);
            }
        }

        throw new Exception("Empty response from LLM");
    }

    private DocumentMetadataSuggestion ParseLlmResponse(string response, string fileName, string previewText)
    {
        try
        {
            // Clean up response - sometimes LLMs add markdown code blocks
            var cleanedResponse = response.Trim();
            if (cleanedResponse.StartsWith("```json"))
            {
                cleanedResponse = cleanedResponse.Substring(7);
            }
            if (cleanedResponse.StartsWith("```"))
            {
                cleanedResponse = cleanedResponse.Substring(3);
            }
            if (cleanedResponse.EndsWith("```"))
            {
                cleanedResponse = cleanedResponse.Substring(0, cleanedResponse.Length - 3);
            }
            cleanedResponse = cleanedResponse.Trim();

            _logger.LogInformation("Parsing cleaned LLM response: {Response}", cleanedResponse);

            var suggestion = JsonSerializer.Deserialize<DocumentMetadataSuggestion>(cleanedResponse);
            
            if (suggestion != null && !string.IsNullOrWhiteSpace(suggestion.Title))
            {
                // ✨ NEW: Validate and adjust confidence based on content quality
                suggestion.Confidence = CalculateAdjustedConfidence(suggestion.Confidence, previewText, fileName);
                
                _logger.LogInformation(
                    "Successfully parsed suggestion - Title: '{Title}', Category: '{Category}', Confidence: {Confidence:F2}", 
                    suggestion.Title, suggestion.Category, suggestion.Confidence
                );
                
                return suggestion;
            }
            
            _logger.LogWarning("Parsed suggestion was null or had empty title");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM response as JSON: {Response}", response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error parsing LLM response: {Response}", response);
        }

        // If parsing failed, return fallback
        return CreateFallbackSuggestion(fileName);
    }

    /// <summary>
    /// ✨ NEW: Calculate adjusted confidence based on content quality
    /// </summary>
    private float CalculateAdjustedConfidence(float llmConfidence, string previewText, string fileName)
    {
        float adjustedConfidence = llmConfidence;
        
        // Factor 1: Content length - more content = higher confidence
        if (previewText.Length < 50)
        {
            // Very little content, reduce confidence
            adjustedConfidence *= 0.6f;
            _logger.LogInformation("Low content length ({Length} chars), reducing confidence", previewText.Length);
        }
        else if (previewText.Length < 200)
        {
            // Limited content
            adjustedConfidence *= 0.8f;
            _logger.LogInformation("Medium content length ({Length} chars), slightly reducing confidence", previewText.Length);
        }
        // else: Good content length, no reduction
        
        // Factor 2: Content quality - check if we actually extracted text vs just filename
        var isOnlyFilename = previewText.Trim().StartsWith("Filename:", StringComparison.OrdinalIgnoreCase);
        if (isOnlyFilename)
        {
            // We only have filename, significantly reduce confidence
            adjustedConfidence *= 0.5f;
            _logger.LogInformation("Only filename available, significantly reducing confidence");
        }
        
        // Factor 3: Meaningful content indicators
        var hasMeaningfulWords = previewText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 10;
        if (!hasMeaningfulWords && !isOnlyFilename)
        {
            adjustedConfidence *= 0.7f;
            _logger.LogInformation("Few meaningful words detected, reducing confidence");
        }
        
        // Ensure confidence stays within valid range [0.0, 1.0]
        adjustedConfidence = Math.Clamp(adjustedConfidence, 0.0f, 1.0f);
        
        _logger.LogInformation(
            "Confidence adjusted from {Original:F2} to {Adjusted:F2}", 
            llmConfidence, adjustedConfidence
        );
        
        return adjustedConfidence;
    }

    private DocumentMetadataSuggestion CreateFallbackSuggestion(string fileName)
    {
        // Remove extension and clean up filename for title
        var title = Path.GetFileNameWithoutExtension(fileName)
            .Replace("_", " ")
            .Replace("-", " ");

        // Capitalize first letter of each word
        title = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(title.ToLower());

        // Guess category based on filename
        var category = GuessCategory(fileName);

        // ✨ IMPROVED: Calculate confidence for fallback based on how good our guess is
        var confidence = CalculateFallbackConfidence(fileName, category);

        _logger.LogInformation(
            "Created fallback suggestion - Title: '{Title}', Category: '{Category}', Confidence: {Confidence:F2}",
            title, category, confidence
        );

        return new DocumentMetadataSuggestion
        {
            Title = title,
            Category = category,
            Confidence = confidence
        };
    }

    /// <summary>
    /// ✨ NEW: Calculate confidence for fallback suggestions based on filename patterns
    /// </summary>
    private float CalculateFallbackConfidence(string fileName, string category)
    {
        var lowerFileName = fileName.ToLower();
        
        // High confidence (0.7-0.8): Strong filename indicators
        if (lowerFileName.Contains("invoice") || lowerFileName.Contains("contract") || 
            lowerFileName.Contains("report") || lowerFileName.Contains("agreement"))
        {
            return 0.75f;
        }
        
        // Medium-high confidence (0.5-0.6): File extension gives good hints
        if (category == "Presentation" && (lowerFileName.EndsWith(".ppt") || lowerFileName.EndsWith(".pptx")))
        {
            return 0.6f;
        }
        if (category == "Spreadsheet" && (lowerFileName.EndsWith(".xls") || lowerFileName.EndsWith(".xlsx")))
        {
            return 0.6f;
        }
        if (category == "Image" && (lowerFileName.EndsWith(".jpg") || lowerFileName.EndsWith(".png")))
        {
            return 0.55f;
        }
        
        // Low confidence (0.3-0.4): Just guessing based on extension or defaulting to "Other"
        if (category == "Other")
        {
            return 0.3f;
        }
        
        // Default medium-low confidence
        return 0.4f;
    }

    private string GuessCategory(string fileName)
    {
        var lowerFileName = fileName.ToLower();

        if (lowerFileName.Contains("invoice") || lowerFileName.Contains("bill"))
            return "Invoice";
        if (lowerFileName.Contains("contract") || lowerFileName.Contains("agreement"))
            return "Contract";
        if (lowerFileName.Contains("report"))
            return "Report";
        if (lowerFileName.Contains("letter"))
            return "Letter";
        if (lowerFileName.Contains("memo"))
            return "Memo";
        if (lowerFileName.Contains("presentation") || lowerFileName.EndsWith(".ppt") || lowerFileName.EndsWith(".pptx"))
            return "Presentation";
        if (lowerFileName.EndsWith(".xls") || lowerFileName.EndsWith(".xlsx") || lowerFileName.EndsWith(".csv"))
            return "Spreadsheet";
        if (lowerFileName.EndsWith(".jpg") || lowerFileName.EndsWith(".jpeg") || lowerFileName.EndsWith(".png") || lowerFileName.EndsWith(".gif"))
            return "Image";

        return "Other";
    }

    // Response models for different LLM APIs
    private class OllamaResponse
    {
        public string? Response { get; set; }
    }

    private class OpenAiResponse
    {
        public OpenAiChoice[]? Choices { get; set; }
    }

    private class OpenAiChoice
    {
        public OpenAiMessage? Message { get; set; }
    }

    private class OpenAiMessage
    {
        public string? Content { get; set; }
    }
}

public class DocumentMetadataSuggestion
{
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public float Confidence { get; set; }
}