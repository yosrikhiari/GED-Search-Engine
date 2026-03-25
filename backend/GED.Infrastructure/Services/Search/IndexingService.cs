using OpenSearch.Client;
using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services.Search;

/// <summary>
/// Handles document and chunk indexing operations in OpenSearch.
/// </summary>
public class IndexingService
{
    private readonly IOpenSearchClient _client;
    private readonly INlpService _nlpService;
    private readonly GedDbContext _db;
    private readonly ILogger<IndexingService> _logger;
    private readonly string _documentIndex;

    public IndexingService(
        IOpenSearchClient client,
        INlpService nlpService,
        GedDbContext db,
        ILogger<IndexingService> logger,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _client = client;
        _nlpService = nlpService;
        _db = db;
        _logger = logger;
        _documentIndex = configuration["Search:IndexName"] ?? "ged-documents";
    }

    /// <summary>
    /// Indexes a document in OpenSearch with ACL fields and embedding.
    /// </summary>
    public async Task<bool> IndexDocumentAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexModel = MapToIndexModel(document);

            // Fetch ACL grants
            var aclUserIds = await _db.DocumentAcls
                .Where(a => a.DocumentId == document.Id &&
                            (a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow))
                .Select(a => a.UserId.ToString())
                .ToListAsync(cancellationToken);

            indexModel.AllowedUserIds = aclUserIds;
            indexModel.CreatedByUserId = document.CreatedBy;
            indexModel.AccessLevel = aclUserIds.Count > 0 ? "restricted" : "open";

            // Generate embedding
            var embeddingText = BuildEmbeddingText(document);
            if (!string.IsNullOrWhiteSpace(embeddingText))
            {
                var embedding = await _nlpService.GenerateEmbeddingAsync(
                    embeddingText, cancellationToken);

                if (embedding != null)
                {
                    indexModel.Embedding = embedding;
                    _logger.LogInformation(
                        "Generated {Dims}-dim embedding for document {Id}",
                        embedding.Length, document.Id);
                }
                else
                {
                    _logger.LogInformation(
                        "Ollama unavailable — indexing document {Id} without embedding",
                        document.Id);
                }
            }

            var response = await _client.IndexAsync(indexModel, i => i
                .Index(_documentIndex)
                .Id(document.Id.ToString()),
                cancellationToken);

            if (response.IsValid)
            {
                await _client.Indices.RefreshAsync(_documentIndex, r => r, cancellationToken);
                _logger.LogInformation(
                    "✅ Document {Id} indexed (embedding={HasEmb}, aclUsers={AclCount})",
                    document.Id, indexModel.Embedding != null, aclUserIds.Count);
                return true;
            }

            _logger.LogError("❌ Failed to index document {Id}: {Error}",
                document.Id, response.DebugInformation);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing document {Id}", document.Id);
            return false;
        }
    }

    /// <summary>
    /// Indexes document chunks in OpenSearch.
    /// </summary>
    public async Task IndexChunksAsync(
        Document document,
        List<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (!chunks.Any()) return;

        // Delete existing chunks
        await DeleteChunksForDocumentAsync(document.Id, cancellationToken);

        // Fetch ACL grants
        var aclUserIds = await _db.DocumentAcls
            .Where(a => a.DocumentId == document.Id &&
                        (a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow))
            .Select(a => a.UserId.ToString())
            .ToListAsync(cancellationToken);

        var accessLevel = aclUserIds.Count > 0 ? "restricted" : "open";

        int indexed = 0;
        foreach (var chunk in chunks)
        {
            try
            {
                chunk.Embedding = await _nlpService.GenerateEmbeddingAsync(
                    chunk.Text, cancellationToken);

                var chunkDoc = new
                {
                    document_id = document.Id,
                    chunk_id = chunk.ChunkId,
                    chunk_index = chunk.ChunkIndex,
                    text = chunk.Text,
                    title = document.Title,
                    category = document.Category,
                    document_date = document.DocumentDate,
                    created_at = document.CreatedAt,
                    file_name = document.FileName,
                    content_type = document.ContentType,
                    tags = document.Tags,
                    embedding = chunk.Embedding,
                    allowedUserIds = aclUserIds,
                    accessLevel = accessLevel,
                    createdByUserId = document.CreatedBy
                };

                var response = await _client.IndexAsync(
                    chunkDoc,
                    i => i.Index("ged-chunks").Id(chunk.ChunkId),
                    cancellationToken);

                if (response.IsValid) indexed++;
                else
                    _logger.LogWarning("Failed to index chunk {ChunkId}: {Error}",
                        chunk.ChunkId, response.ServerError?.Error?.Reason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error indexing chunk {ChunkId} — skipping", chunk.ChunkId);
            }
        }

        _logger.LogInformation("✅ Indexed {Indexed}/{Total} chunks for document {DocId}",
            indexed, chunks.Count, document.Id);
    }

    /// <summary>
    /// Deletes a document from the index.
    /// </summary>
    public async Task<bool> DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.DeleteAsync<DocumentIndexModel>(
                documentId.ToString(),
                d => d.Index(_documentIndex),
                cancellationToken);

            if (response.IsValid || response.Result == Result.NotFound)
            {
                await _client.Indices.RefreshAsync(_documentIndex, r => r, cancellationToken);
                return true;
            }

            _logger.LogWarning("Failed to delete document {Id}: {Error}",
                documentId, response.DebugInformation);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document {Id}", documentId);
            return false;
        }
    }

    /// <summary>
    /// Deletes all chunks for a document.
    /// </summary>
    public async Task DeleteChunksForDocumentAsync(Guid documentId, CancellationToken ct)
    {
        try
        {
            await _client.DeleteByQueryAsync<ChunkIndexModel>(d => d
                .Index("ged-chunks")
                .Query(q => q.Term(t => t.Field("document_id").Value(documentId.ToString()))),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete existing chunks for document {DocId}", documentId);
        }
    }

    private static string BuildEmbeddingText(Document document)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(document.Title)) parts.Add(document.Title);
        if (!string.IsNullOrWhiteSpace(document.Description)) parts.Add(document.Description);
        if (!string.IsNullOrWhiteSpace(document.Category) ) parts.Add(document.Category);

        var content = !string.IsNullOrWhiteSpace(document.OcrText)
            ? document.OcrText : document.ExtractedText;
        if (!string.IsNullOrWhiteSpace(content))
            parts.Add(content.Length > 3000 ? content[..3000] : content);

        return string.Join(". ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static DocumentIndexModel MapToIndexModel(Document document) => new()
    {
        Id = document.Id,
        Title = document.Title,
        Description = document.Description,
        FileName = document.FileName,
        ContentType = document.ContentType,
        FileSize = document.FileSize,
        CreatedAt = document.CreatedAt,
        DocumentDate = document.DocumentDate,
        ModifiedAt = document.ModifiedAt,
        Category = document.Category,
        Tags = document.Tags,
        ExtractedText = document.ExtractedText,
        OcrText = document.OcrText,
        Status = document.Status.ToString(),
        IsOcrProcessed = document.IsOcrProcessed
    };
}
