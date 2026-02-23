using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenSearch.Client;
using OpenSearch.Net;
using System.Text.Json;

namespace GED.Infrastructure.Services;

/// <summary>
/// Handles all vector / semantic search concerns:
///   1. Creating the knn_vector mapping on the "ged-documents-vector" index
///   2. Indexing documents with their embeddings
///   3. Running k-NN queries and returning ranked document IDs
/// </summary>
public class VectorSearchService
{
    private readonly IOpenSearchClient           _client;
    private readonly IEmbeddingService           _embeddings;
    private readonly ILogger<VectorSearchService> _logger;
    private readonly bool                        _enabled;

    private const string VectorIndex = "ged-documents-vector";

    public VectorSearchService(
        IOpenSearchClient           client,
        IEmbeddingService           embeddings,
        ILogger<VectorSearchService> logger,
        IConfiguration              configuration)
    {
        _client     = client;
        _embeddings = embeddings;
        _logger     = logger;
        _enabled    = configuration.GetValue<bool>("Embeddings:Enabled", false);
    }

    // ── Index management ──────────────────────────────────────────────────────

    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled) return;

        try
        {
            var exists = await _client.Indices.ExistsAsync(VectorIndex, ct: cancellationToken);
            if (exists.Exists)
            {
                _logger.LogInformation("✅ Vector index '{Index}' already exists", VectorIndex);
                return;
            }

            var dims = _embeddings.Dimensions;
            var createBody = $$"""
            {
              "settings": {
                "index": {
                  "knn": true,
                  "knn.algo_param.ef_search": 100,
                  "number_of_shards": 1,
                  "number_of_replicas": 0
                }
              },
              "mappings": {
                "properties": {
                  "documentId":   { "type": "keyword" },
                  "title":        { "type": "text" },
                  "category":     { "type": "keyword" },
                  "contentType":  { "type": "keyword" },
                  "createdAt":    { "type": "date" },
                  "documentDate": { "type": "date" },
                  "embedding": {
                    "type":       "knn_vector",
                    "dimension":  {{dims}},
                    "method": {
                      "name":       "hnsw",
                      "space_type": "cosinesimil",
                      "engine":     "faiss",
                      "parameters": { "ef_construction": 128, "m": 24 }
                    }
                  }
                }
              }
            }
            """;

            var response = await _client.LowLevel.Indices.CreateAsync<DynamicResponse>(
                VectorIndex,
                PostData.String(createBody),
                ctx: cancellationToken);

            if (response.Success)
                _logger.LogInformation(
                    "✅ Vector index '{Index}' created ({Dims} dims, HNSW/cosine)",
                    VectorIndex, dims);
            else
            {
                // Cast body to string to avoid dynamic dispatch issue with logger extension methods
                string bodyStr = response.Body?.ToString() ?? "unknown error";
                _logger.LogError("❌ Failed to create vector index: {Body}", bodyStr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring vector index '{Index}'", VectorIndex);
        }
    }

    // ── Document indexing ─────────────────────────────────────────────────────

    public async Task IndexDocumentVectorAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled) return;

        try
        {
            var textToEmbed = BuildEmbeddingText(document);
            var vector = await _embeddings.EmbedAsync(textToEmbed, cancellationToken);

            if (vector == null)
            {
                _logger.LogWarning(
                    "Skipping vector index for {DocumentId} — embedding returned null",
                    document.Id);
                return;
            }

            var vectorDoc = new
            {
                documentId   = document.Id.ToString(),
                title        = document.Title,
                category     = document.Category ?? "",
                contentType  = document.ContentType,
                createdAt    = document.CreatedAt,
                documentDate = document.DocumentDate,
                embedding    = vector
            };

            var json = JsonSerializer.Serialize(vectorDoc);

            var response = await _client.LowLevel.IndexAsync<DynamicResponse>(
                VectorIndex,
                document.Id.ToString(),
                PostData.String(json),
                ctx: cancellationToken);

            if (response.Success)
                _logger.LogInformation(
                    "🔢 Vector indexed document {DocumentId} ({Dims} dims)",
                    document.Id, vector.Length);
            else
            {
                string bodyStr = response.Body?.ToString() ?? "unknown error";
                _logger.LogWarning(
                    "Vector index failed for {DocumentId}: {Body}",
                    document.Id, bodyStr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to index vector for document {DocumentId}", document.Id);
        }
    }

    public async Task DeleteDocumentVectorAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled) return;

        try
        {
            await _client.LowLevel.DeleteAsync<DynamicResponse>(
                VectorIndex, documentId.ToString(), ctx: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to delete vector for document {DocumentId}", documentId);
        }
    }

    // ── Semantic search ───────────────────────────────────────────────────────

    public async Task<List<(Guid DocumentId, float Score)>> SemanticSearchAsync(
        string query,
        int topK = 20,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
            return new List<(Guid, float)>();

        try
        {
            var queryVector = await _embeddings.EmbedAsync(query, cancellationToken);
            if (queryVector == null)
                return new List<(Guid, float)>();

            var vectorJson = string.Join(",", queryVector.Select(v => v.ToString("G")));
            var knnQuery = $$"""
            {
              "size": {{topK}},
              "query": {
                "knn": {
                  "embedding": {
                    "vector": [{{vectorJson}}],
                    "k": {{topK}}
                  }
                }
              }
            }
            """;

            var response = await _client.LowLevel.SearchAsync<DynamicResponse>(
                VectorIndex,
                PostData.String(knnQuery),
                ctx: cancellationToken);

            if (!response.Success)
            {
                string bodyStr = response.Body?.ToString() ?? "unknown error";
                _logger.LogWarning("k-NN search failed: {Body}", bodyStr);
                return new List<(Guid, float)>();
            }

            // Serialize the dynamic body to a JSON string for safe parsing
            string responseJson = response.Body?.ToString() ?? "{}";
            return ParseKnnResponse(responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Semantic search failed — returning empty results");
            return new List<(Guid, float)>();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildEmbeddingText(Document document)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(document.Title))
        {
            parts.Add(document.Title);
            parts.Add(document.Title);
        }
        if (!string.IsNullOrWhiteSpace(document.Category))
            parts.Add(document.Category);
        if (!string.IsNullOrWhiteSpace(document.Description))
            parts.Add(document.Description);

        var bodyText = document.ExtractedText ?? document.OcrText ?? "";
        if (!string.IsNullOrWhiteSpace(bodyText))
            parts.Add(bodyText.Length > 2000 ? bodyText[..2000] : bodyText);

        if (document.Tags?.Any() == true)
            parts.Add(string.Join(" ", document.Tags));

        return string.Join(". ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private List<(Guid DocumentId, float Score)> ParseKnnResponse(string json)
    {
        var results = new List<(Guid DocumentId, float Score)>();
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return results;

            using var doc  = JsonDocument.Parse(json);
            var hits = doc.RootElement
                .GetProperty("hits")
                .GetProperty("hits");

            foreach (var hit in hits.EnumerateArray())
            {
                var idStr = hit.GetProperty("_source")
                              .GetProperty("documentId")
                              .GetString();
                var score = (float)hit.GetProperty("_score").GetDouble();

                if (idStr != null && Guid.TryParse(idStr, out Guid id))
                    results.Add((id, score));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse k-NN response");
        }
        return results;
    }
}