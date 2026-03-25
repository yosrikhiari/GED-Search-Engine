using GED.Core.Models;

namespace GED.Core.Interfaces;

/// <summary>
/// Service for classifying user queries into intent types
/// to optimize RAG retrieval strategy.
/// </summary>
public interface IQueryClassifierService
{
    /// <summary>
    /// Classifies a query into one of: factual, summarization, comparison, extraction.
    /// </summary>
    /// <param name="query">The user query to classify.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Classification result with recommended retrieval parameters.</returns>
    Task<QueryClassificationResult> ClassifyAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether classification is enabled.
    /// </summary>
    bool IsEnabled { get; }
}
