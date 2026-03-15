using GED.Core.Models;

namespace GED.Core.Interfaces;

/// <summary>
/// RAG (Retrieval Augmented Generation) service.
/// Combines OpenSearch retrieval with LLM generation to answer
/// natural-language questions about documents.
/// </summary>
public interface IRagService
{
    /// <summary>
    /// Ask a question in natural language.
    /// Returns an AI-generated answer and the source documents used.
    /// </summary>
    Task<RagResponse> AskAsync(RagRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streaming version of AskAsync.
    /// Yields LLM tokens one by one via Server-Sent Events.
    /// </summary>
    IAsyncEnumerable<string> AskStreamAsync(RagRequest request, CancellationToken cancellationToken = default);
}