using GED.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace GED.Infrastructure.Services;

/// <summary>
/// Splits document text into overlapping fixed-size chunks for semantic indexing.
/// 
/// Why overlapping chunks:
/// A fixed-size window sliding with overlap ensures that sentences split at a
/// boundary are still retrievable from the neighboring chunk. This addresses
/// the "lost in the middle" problem where LLMs ignore context in the middle of
/// a long context window — RAG retrieves only the 3-5 most relevant chunks
/// rather than the full document.
/// 
/// Chunking strategy (in priority order):
/// <list type="number">
///   <item>
///     <term>Paragraph-aware splitting</term>
///     <description>
///       If the document has clear double-newline paragraph structure,
///       merge consecutive paragraphs up to ChunkSize. This keeps semantically
///       coherent units together and avoids cutting mid-idea.
///     </description>
///   </item>
///   <item>
///     <term>Sliding window fallback</term>
///     <description>
///       For flat OCR text (no paragraph breaks), use the original overlapping
///       character-window with sentence-boundary detection.
///     </description>
///   </item>
/// </list>
/// </summary>
public class DocumentChunkingService
{
    private readonly ILogger<DocumentChunkingService> _logger;

    /// <summary>
    /// Maximum character length of each chunk.
    /// </summary>
    private readonly int _chunkSize;

    /// <summary>
    /// Number of characters to overlap between consecutive chunks.
    /// </summary>
    private readonly int _chunkOverlap;

    /// <summary>
    /// Minimum character length for a paragraph to be considered meaningful.
    /// </summary>
    private readonly int _minChunkLen;

    /// <summary>
    /// Initializes a new instance of <see cref="DocumentChunkingService"/>.
    /// </summary>
    /// <param name="logger">Logger for chunking events.</param>
    /// <param name="configuration">Application configuration with RAG:* settings.</param>
    public DocumentChunkingService(ILogger<DocumentChunkingService> logger, IConfiguration configuration)
    {
        _logger       = logger;
        _chunkSize    = configuration.GetValue<int>("RAG:ChunkSize",    2000);
        _chunkOverlap = configuration.GetValue<int>("RAG:ChunkOverlap", 500);
        _minChunkLen  = configuration.GetValue<int>("RAG:MinChunkLen",  100);
    }

    /// <summary>
    /// Splits text into chunks for semantic indexing.
    /// </summary>
    /// <param name="documentId">The document ID to include in chunk IDs.</param>
    /// <param name="text">The text to chunk.</param>
    /// <returns>List of <see cref="DocumentChunk"/> objects with zero-based indices.</returns>
    /// <remarks>
    /// Returns an empty list if text is null/empty.
    /// Each chunk carries its zero-based index and the total chunk count for the document.
    /// Tries paragraph-aware splitting first; falls back to sliding window for flat text.
    /// </remarks>
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

            int step = _chunkSize - _chunkOverlap;
            if (step <= 0)
                step = 1;
            start += step;
        }

        _logger.LogInformation(
            "📦 Sliding-window chunked {DocId} into {Count} chunks ({TextLen} chars total)",
            documentId, chunks.Count, text.Length);

        return chunks;
    }

    /// <summary>
    /// Finds the best sentence break point within a text window.
    /// Searches backwards from preferred end for sentence-ending punctuation.
    /// Falls back to last space if no sentence break found.
    /// </summary>
    /// <param name="text">Full text being chunked.</param>
    /// <param name="start">Start position of the chunk.</param>
    /// <param name="preferredEnd">Preferred end position (chunk start + chunk size).</param>
    /// <returns>Position after the best break point found.</returns>
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
