namespace GED.Core.Models;

/// <summary>
/// A single chunk of text extracted from a document.
/// ChunkId format: "{documentId}_{chunkIndex}" — used as the OpenSearch document ID.
/// </summary>
public record DocumentChunk
{
    public string   ChunkId    { get; init; } = string.Empty;
    public Guid     DocumentId { get; init; }
    public int      ChunkIndex { get; init; }
    public string   Text       { get; init; } = string.Empty;
    public float[]? Embedding  { get; set; }   // filled in by OpenSearchService
}
