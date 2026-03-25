using GED.Core.Models;

namespace GED.Core.Interfaces;

/// <summary>
/// Chunk reranker service for improving RAG retrieval quality.
/// Uses a cross-encoder model to re-score chunks against the query
/// for better relevance ranking.
/// </summary>
public interface IChunkRerankerService
{
    /// <summary>
    /// Re-ranks a list of chunks based on their relevance to the query.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="chunks">List of chunks to re-rank.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Re-ranked list of chunks with updated scores.</returns>
    Task<List<ChunkSearchHit>> ReRankAsync(
        string query,
        List<ChunkSearchHit> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the reranker is available and configured.
    /// </summary>
    bool IsAvailable { get; }
}
