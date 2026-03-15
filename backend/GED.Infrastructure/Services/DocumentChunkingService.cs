using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

/// <summary>
/// Splits document text into overlapping fixed-size chunks for semantic indexing.
///
/// Why overlapping: a 512-token window sliding with 128-token overlap ensures that
/// sentences split at a boundary are still retrievable from the neighbouring chunk.
/// The "lost in the middle" problem — where LLMs ignore context in the middle of a
/// long prompt — is avoided because RAG now retrieves only the 3-5 most relevant
/// chunks rather than the full document.
/// </summary>
public class DocumentChunkingService
{
    private readonly ILogger<DocumentChunkingService> _logger;

    // ~512 tokens ≈ 2000 chars for Latin scripts; use chars as a safe proxy.
    // Overlap = 25% of chunk size — enough to bridge boundary splits.
    private const int ChunkSize    = 2000;
    private const int ChunkOverlap = 500;
    private const int MinChunkLen  = 100;   // discard tiny trailing chunks

    public DocumentChunkingService(ILogger<DocumentChunkingService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Splits text into overlapping chunks. Returns an empty list if text is null/empty.
    /// Each chunk carries its zero-based index and the total chunk count for the document.
    /// </summary>
    public List<DocumentChunk> ChunkText(Guid documentId, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<DocumentChunk>();

        var chunks = new List<DocumentChunk>();
        int start  = 0;
        int index  = 0;

        while (start < text.Length)
        {
            int end = Math.Min(start + ChunkSize, text.Length);

            // Try to break at a sentence boundary ('. ', '.\n', '! ', '? ')
            // to avoid cutting in the middle of a word or sentence.
            if (end < text.Length)
            {
                int breakPoint = FindSentenceBreak(text, start, end);
                if (breakPoint > start + MinChunkLen)
                    end = breakPoint;
            }

            var chunkText = text[start..end].Trim();

            if (chunkText.Length >= MinChunkLen)
            {
                chunks.Add(new DocumentChunk
                {
                    ChunkId    = $"{documentId}_{index}",
                    DocumentId = documentId,
                    ChunkIndex = index,
                    Text       = chunkText
                });
                index++;
            }

            // Move forward by (ChunkSize - overlap) so consecutive chunks share context
            start += ChunkSize - ChunkOverlap;
        }

        _logger.LogInformation(
            "📦 Chunked document {DocId} into {Count} chunks ({TextLen} chars total)",
            documentId, chunks.Count, text.Length);

        return chunks;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static int FindSentenceBreak(string text, int start, int preferredEnd)
    {
        // Search backwards from preferredEnd for '. ', '.\n', '! ', '? '
        for (int i = preferredEnd - 1; i > start + 100; i--)
        {
            char c = text[i];
            if ((c == '.' || c == '!' || c == '?') &&
                i + 1 < text.Length &&
                (text[i + 1] == ' ' || text[i + 1] == '\n'))
            {
                return i + 1; // include the punctuation, break after it
            }
        }

        // Fallback: break at the last space before preferredEnd
        for (int i = preferredEnd - 1; i > start + 100; i--)
        {
            if (text[i] == ' ')
                return i;
        }

        return preferredEnd; // hard cut
    }
}

/// <summary>
/// A single chunk of text extracted from a document.
/// ChunkId format: "{documentId}_{chunkIndex}" — used as the OpenSearch document ID.
/// </summary>
public record DocumentChunk
{
    public string  ChunkId    { get; init; } = string.Empty;
    public Guid    DocumentId { get; init; }
    public int     ChunkIndex { get; init; }
    public string  Text       { get; init; } = string.Empty;
    public float[]? Embedding { get; set; }   // filled in by OpenSearchService
}