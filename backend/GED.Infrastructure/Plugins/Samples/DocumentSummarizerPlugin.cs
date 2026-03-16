using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;

namespace GED.Infrastructure.Plugins.Samples;

/// <summary>
/// Sample document action plugin that generates document summaries using the LLM.
/// </summary>
public class DocumentSummarizerPlugin : IDocumentActionPlugin
{
    private readonly ILogger<DocumentSummarizerPlugin> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly string _llmEndpoint;
    private readonly string _llmModel;
    private readonly int _maxSummaryLength;

    public string Id => "ged.action.summarizer";
    public string Name => "Document Summarizer";
    public string Version => "1.0.0";
    public string Description => "Generates AI-powered summaries of documents";
    public string Category => "action";
    public bool IsEnabledByDefault => false;
    public string DisplayName => "Generate Summary";
    public string Icon => "📝";

    public DocumentSummarizerPlugin(
        ILogger<DocumentSummarizerPlugin> logger,
        IConfiguration configuration,
        HttpClient httpClient)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClient;
        _llmEndpoint = configuration["NLP:LlmApiEndpoint"] ?? "http://localhost:11434/api/generate";
        _llmModel = configuration["NLP:Model"] ?? "llama3.2";
        _maxSummaryLength = configuration.GetValue<int>("Plugins:Summarizer:MaxLength", 300);
    }

    public Task InitializeAsync()
    {
        _logger.LogInformation("Initialized Document Summarizer plugin");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        _logger.LogInformation("Shutdown Document Summarizer plugin");
        return Task.CompletedTask;
    }

    public async Task<DocumentActionResult> ExecuteAsync(DocumentActionRequest request, CancellationToken ct = default)
    {
        if (request.ActionName != "summarize")
        {
            return new DocumentActionResult
            {
                Success = false,
                Message = $"Unknown action: {request.ActionName}"
            };
        }

        try
        {
            var docId = request.DocumentId;
            _logger.LogInformation("Generating summary for document {DocId}", docId);

            var summary = await GenerateSummaryAsync(docId, ct);

            return new DocumentActionResult
            {
                Success = true,
                Message = "Summary generated successfully",
                Data = new Dictionary<string, object>
                {
                    { "summary", summary },
                    { "documentId", docId }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate summary for document {DocId}", request.DocumentId);
            return new DocumentActionResult
            {
                Success = false,
                Message = $"Failed to generate summary: {ex.Message}"
            };
        }
    }

    private async Task<string> GenerateSummaryAsync(Guid documentId, CancellationToken ct)
    {
        var prompt = $@"Summarize the following document content in {_maxSummaryLength} characters or less.
Focus on the main points and key information.

Document ID: {documentId}

[Note: In production, this would include the actual document content]

SUMMARY:";

        var requestBody = new
        {
            model = _llmModel,
            prompt = prompt,
            stream = false,
            temperature = 0.3,
            options = new { num_predict = _maxSummaryLength }
        };

        var response = await _httpClient.PostAsJsonAsync(_llmEndpoint, requestBody, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"LLM request failed: {response.StatusCode}");
        }

        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(ct);
        return result?.Response?.Trim() ?? "Unable to generate summary.";
    }

    private class OllamaResponse
    {
        public string? Response { get; set; }
    }
}
