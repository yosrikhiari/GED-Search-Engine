using GED.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace GED.Infrastructure.Services;

// ── Interface ─────────────────────────────────────────────────────────────────

public interface IEmbeddingService
{
    /// <summary>
    /// Generate a dense vector embedding for the given text.
    /// Returns null if the embedding service is unavailable.
    /// </summary>
    Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dimensionality of the embedding model (needed for index mapping).
    /// </summary>
    int Dimensions { get; }
}

// ── Ollama implementation ─────────────────────────────────────────────────────

/// <summary>
/// Generates embeddings using Ollama's /api/embeddings endpoint.
/// Compatible with any model that supports embeddings (nomic-embed-text
/// is recommended — 768 dims, fast, good quality).
///
/// Falls back gracefully when Ollama is unavailable so the rest of the
/// application keeps working without semantic search.
/// </summary>
public class OllamaEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaEmbeddingService> _logger;
    private readonly string  _endpoint;
    private readonly string  _model;
    private readonly bool    _enabled;
    private readonly int     _dimensions;

    public int Dimensions => _dimensions;

    public OllamaEmbeddingService(
        HttpClient httpClient,
        ILogger<OllamaEmbeddingService> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger     = logger;
        _endpoint   = configuration["NLP:LlmApiEndpoint"]
                          ?.Replace("/api/generate", "/api/embeddings")
                      ?? "http://localhost:11434/api/embeddings";
        _model      = configuration["Embeddings:Model"] ?? "nomic-embed-text";
        _enabled    = configuration.GetValue<bool>("Embeddings:Enabled", false);
        _dimensions = configuration.GetValue<int>("Embeddings:Dimensions", 768);
    }

    public async Task<float[]?> EmbedAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            // Truncate to avoid exceeding model context window
            var truncated = text.Length > 8000 ? text[..8000] : text;

            var response = await _httpClient.PostAsJsonAsync(
                _endpoint,
                new { model = _model, prompt = truncated },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Ollama embeddings returned {Status} for model {Model}",
                    response.StatusCode, _model);
                return null;
            }

            var result = await response.Content
                .ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken);

            if (result?.Embedding == null || result.Embedding.Length == 0)
            {
                _logger.LogWarning("Ollama returned empty embedding");
                return null;
            }

            _logger.LogDebug(
                "Embedding generated: {Dims} dims, model={Model}",
                result.Embedding.Length, _model);

            return result.Embedding;
        }
        catch (Exception ex)
        {
            // Non-fatal — caller decides whether to proceed without embedding
            _logger.LogWarning(ex, "Embedding generation failed — semantic search unavailable");
            return null;
        }
    }

    private class OllamaEmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }
}