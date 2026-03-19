using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace GED.Infrastructure.Services;

/// <summary>
/// Splits document text into overlapping fixed-size chunks for semantic indexing.
///
/// Why overlapping: a 512-token window sliding with 128-token overlap ensures that
/// sentences split at a boundary are still retrievable from the neighbouring chunk.
/// The "lost in the middle" problem — where LLMs ignore context in the middle of a
/// long prompt — is avoided because RAG now retrieves only the 3-5 most relevant
/// chunks rather than the full document.
///
/// Chunking strategy (in priority order):
///   1. Paragraph-aware: if the document has clear double-newline paragraph structure,
///      merge consecutive paragraphs up to ChunkSize. This keeps semantically coherent
///      units together and avoids cutting mid-idea.
///   2. Sliding window fallback: for flat OCR text (no paragraph breaks), use the
///      original overlapping character-window with sentence-boundary detection.
/// </summary>
public class DocumentChunkingService
{
    private readonly ILogger<DocumentChunkingService> _logger;

    private readonly int _chunkSize;
    private readonly int _chunkOverlap;
    private readonly int _minChunkLen;

    public DocumentChunkingService(ILogger<DocumentChunkingService> logger, IConfiguration configuration)
    {
        _logger       = logger;
        _chunkSize    = configuration.GetValue<int>("RAG:ChunkSize",    2000);
        _chunkOverlap = configuration.GetValue<int>("RAG:ChunkOverlap", 500);
        _minChunkLen  = configuration.GetValue<int>("RAG:MinChunkLen",  100);
    }

    /// <summary>
    /// Splits text into chunks. Returns an empty list if text is null/empty.
    /// Each chunk carries its zero-based index and the total chunk count for the document.
    /// Tries paragraph-aware splitting first; falls back to sliding window for flat text.
    /// </summary>
    public List<DocumentChunk> ChunkText(Guid documentId, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<DocumentChunk>();

        var chunks = new List<DocumentChunk>();

        // ── Strategy 1: paragraph-aware splitting ────────────────────────────
        // Split on two or more consecutive newlines (double newline = paragraph boundary).
        // Only activate if the document has meaningful paragraph structure:
        //   - more than one paragraph after filtering short ones
        //   - majority of paragraphs are under ChunkSize (i.e. not just one giant block)
        var paragraphs = Regex.Split(text, @"\n{2,}")
            .Select(p => p.Trim())
            .Where(p => p.Length >= _minChunkLen)
            .ToList();

        bool useParagraphs = paragraphs.Count > 1 &&
            paragraphs.Count(p => p.Length <= _chunkSize) > paragraphs.Count / 2;

        if (useParagraphs)
        {
            _logger.LogDebug(
                "📄 Paragraph-aware chunking activated for {DocId}: {Count} paragraphs found",
                documentId, paragraphs.Count);

            var buffer      = new System.Text.StringBuilder();
            int index       = 0;
            string? lastParagraph = null;  // kept for overlap: repeat last para in next chunk

            foreach (var para in paragraphs)
            {
                // If adding this paragraph would overflow the chunk, flush first
                if (buffer.Length > 0 && buffer.Length + para.Length + 2 > _chunkSize)
                {
                    var chunkText = buffer.ToString().Trim();
                    if (chunkText.Length >= _minChunkLen)
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

                    // Overlap: seed the next chunk with the last paragraph of this one
                    // so a sentence spanning a boundary is retrievable from either chunk.
                    buffer.Clear();
                    if (lastParagraph != null && lastParagraph.Length < _chunkSize / 2)
                    {
                        buffer.AppendLine(lastParagraph);
                        buffer.AppendLine();
                    }
                }

                buffer.AppendLine(para);
                buffer.AppendLine();
                lastParagraph = para;
            }

            // Flush any remaining content
            if (buffer.Length > 0)
            {
                var chunkText = buffer.ToString().Trim();
                if (chunkText.Length >= _minChunkLen)
                {
                    chunks.Add(new DocumentChunk
                    {
                        ChunkId    = $"{documentId}_{index}",
                        DocumentId = documentId,
                        ChunkIndex = index,
                        Text       = chunkText
                    });
                }
            }

            _logger.LogInformation(
                "📦 Paragraph chunked {DocId} into {Count} chunks ({TextLen} chars total)",
                documentId, chunks.Count, text.Length);

            return chunks;
        }

        // ── Strategy 2: sliding window fallback ───────────────────────────────
        // Used for flat OCR text that has no paragraph structure (e.g. raw Tesseract output).
        _logger.LogDebug(
            "📄 Sliding window chunking activated for {DocId} (no paragraph structure detected)",
            documentId);

        int start = 0;
        int slideIndex = 0;

        while (start < text.Length)
        {
            int end = Math.Min(start + _chunkSize, text.Length);

            // Try to break at a sentence boundary ('. ', '.\n', '! ', '? ')
            // to avoid cutting in the middle of a word or sentence.
            if (end < text.Length)
            {
                int breakPoint = FindSentenceBreak(text, start, end);
                if (breakPoint > start + _minChunkLen)
                    end = breakPoint;
            }

            var chunkText = text[start..end].Trim();

            if (chunkText.Length >= _minChunkLen)
            {
                chunks.Add(new DocumentChunk
                {
                    ChunkId    = $"{documentId}_{slideIndex}",
                    DocumentId = documentId,
                    ChunkIndex = slideIndex,
                    Text       = chunkText
                });
                slideIndex++;
            }

            // Move forward by _chunkOverlap so consecutive chunks share exactly
            // _chunkOverlap characters of context. This ensures that if a sentence
            // break cuts chunk0 early, chunk1 starts at start+overlap (which is
            // BEFORE the break), covering the gap that would exist if step were
            // _chunkSize-_chunkOverlap.
            start += _chunkOverlap;
        }

        _logger.LogInformation(
            "📦 Sliding-window chunked {DocId} into {Count} chunks ({TextLen} chars total)",
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
    public string   ChunkId    { get; init; } = string.Empty;
    public Guid     DocumentId { get; init; }
    public int      ChunkIndex { get; init; }
    public string   Text       { get; init; } = string.Empty;
    public float[]? Embedding  { get; set; }   // filled in by OpenSearchService
}