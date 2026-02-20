using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenSearch.Client;
using System.Text.Json;

namespace GED.Infrastructure.Services;

/// <summary>
/// Handles all vector / semantic search concerns:
///   1. Creating the knn_vector mapping on the "ged-documents-vector" index
///   2. Indexing documents with their embeddings
///   3. Running k-NN queries and returning ranked document IDs
///
/// Works alongside the existing OpenSearchService (keyword search) so results
/// from both can be merged / RRF-fused in SemanticSearchService.
/// </summary>
public class VectorSearchService
{
    private readonly IOpenSearchClient          _client;
    private readonly IEmbeddingService          _embeddings;
    private readonly ILogger<VectorSearchService> _logger;
    private readonly bool                       _enabled;

    private const string VectorIndex = "ged-documents-vector";

    public VectorSearchService(
        IOpenSearchClient          client,
        IEmbeddingService          embeddings,
        ILogger<VectorSearchService> logger,
        IConfiguration             configuration)
    {
        _client     = client;
        _embeddings = embeddings;
        _logger     = logger;
        _enabled    = configuration.GetValue<bool>("Embeddings:Enabled", false);
    }

    // ── Index management ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates the vector index with the knn_vector field mapping.
    /// Safe to call repeatedly — skips if the index already exists.
    /// Must be called BEFORE the first document is indexed.
    /// </summary>
    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled) return;

        try
        {
            var exists = await _client.Indices.ExistsAsync(
                VectorIndex, ct: cancellationToken);

            if (exists.Exists)
            {
                _logger.LogInformation(
                    "✅ Vector index '{Index}' already exists", VectorIndex);
                return;
            }

            // Build raw JSON mapping because the NEST/OSC fluent API
            // doesn't expose knn_vector natively in v1.x
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

            var response = await _client.LowLevel.Indices.CreateAsync<StringResponse>(
                VectorIndex,
                PostData.String(createBody),
                ctx: cancellationToken);

            if (response.Success)
                _logger.LogInformation(
                    "✅ Vector index '{Index}' created ({Dims} dims, HNSW/cosine)",
                    VectorIndex, dims);
            else
                _logger.LogError(
                    "❌ Failed to create vector index: {Body}", response.Body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring vector index '{Index}'", VectorIndex);
        }
    }

    // ── Document indexing ─────────────────────────────────────────────────────

    /// <summary>
    /// Generates an embedding for the document's textual content and
    /// upserts a vector document into the vector index.
    /// No-ops gracefully when embeddings are disabled or unavailable.
    /// </summary>
    public async Task IndexDocumentVectorAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled) return;

        try
        {
            // Build the text we want to embed — richer text = better semantic recall
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

            var response = await _client.LowLevel.IndexAsync<StringResponse>(
                VectorIndex,
                document.Id.ToString(),
                PostData.String(json),
                ctx: cancellationToken);

            if (response.Success)
                _logger.LogInformation(
                    "🔢 Vector indexed document {DocumentId} ({Dims} dims)",
                    document.Id, vector.Length);
            else
                _logger.LogWarning(
                    "Vector index failed for {DocumentId}: {Body}",
                    document.Id, response.Body);
        }
        catch (Exception ex)
        {
            // Non-fatal — keyword search still works
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
            await _client.LowLevel.DeleteAsync<StringResponse>(
                VectorIndex, documentId.ToString(), ctx: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to delete vector for document {DocumentId}", documentId);
        }
    }

    // ── Semantic search ───────────────────────────────────────────────────────

    /// <summary>
    /// Runs a k-NN search against the vector index.
    /// Returns (documentId, similarity score) pairs ordered by cosine similarity.
    /// </summary>
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

            // Raw k-NN query — OSC v1.x doesn't have a typed knn descriptor
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

            var response = await _client.LowLevel.SearchAsync<StringResponse>(
                VectorIndex,
                PostData.String(knnQuery),
                ctx: cancellationToken);

            if (!response.Success)
            {
                _logger.LogWarning(
                    "k-NN search failed: {Body}", response.Body);
                return new List<(Guid, float)>();
            }

            return ParseKnnResponse(response.Body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Semantic search failed — returning empty results");
            return new List<(Guid, float)>();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Concatenate the most semantically meaningful fields for embedding.
    /// Title and category are weighted by repetition.
    /// </summary>
    private static string BuildEmbeddingText(Document document)
    {
        var parts = new List<string>();

        // Repeat title & category to upweight them in the embedding
        if (!string.IsNullOrWhiteSpace(document.Title))
        {
            parts.Add(document.Title);
            parts.Add(document.Title);   // intentional duplicate for weight
        }
        if (!string.IsNullOrWhiteSpace(document.Category))
            parts.Add(document.Category);
        if (!string.IsNullOrWhiteSpace(document.Description))
            parts.Add(document.Description);

        // Truncate extracted text — first 2000 chars carries most signal
        var bodyText = document.ExtractedText ?? document.OcrText ?? "";
        if (!string.IsNullOrWhiteSpace(bodyText))
            parts.Add(bodyText.Length > 2000 ? bodyText[..2000] : bodyText);

        if (document.Tags?.Any() == true)
            parts.Add(string.Join(" ", document.Tags));

        return string.Join(". ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private List<(Guid DocumentId, float Score)> ParseKnnResponse(string body)
    {
        var results = new List<(Guid, float)>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            var hits = doc.RootElement
                .GetProperty("hits")
                .GetProperty("hits");

            foreach (var hit in hits.EnumerateArray())
            {
                var idStr = hit.GetProperty("_source")
                              .GetProperty("documentId")
                              .GetString();
                var score = (float)hit.GetProperty("_score").GetDouble();

                if (Guid.TryParse(idStr, out var id))
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