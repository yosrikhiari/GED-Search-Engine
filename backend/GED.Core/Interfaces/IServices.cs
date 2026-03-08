using GED.Core.Models;

namespace GED.Core.Interfaces;

public interface ISearchService
{
    Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
    Task<List<DocumentSuggestion>> GetRelatedDocumentsAsync(Guid documentId, int count = 5, CancellationToken cancellationToken = default);
    Task<NaturalLanguageQuery> ProcessNaturalLanguageQueryAsync(string query, CancellationToken cancellationToken = default);
    Task<bool> IndexDocumentAsync(Document document, CancellationToken cancellationToken = default);
    Task<bool> UpdateDocumentIndexAsync(Document document, CancellationToken cancellationToken = default);
    Task<bool> DeleteDocumentIndexAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<bool> BulkIndexDocumentsAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default);
}

public interface IDocumentService
{
    Task<Document?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Document> UploadDocumentAsync(Stream fileStream, string fileName, string contentType, string? title = null, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Document> UpdateDocumentAsync(Guid id, Document document, CancellationToken cancellationToken = default);
    Task<Stream> GetDocumentContentAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Document>> GetDocumentsByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
}

public interface IOcrService
{
    Task<Guid> QueueOcrJobAsync(Guid documentId, string? language = null, CancellationToken cancellationToken = default);
    Task<OcrResult> ProcessDocumentAsync(Guid documentId, Stream documentStream, string? language = null, CancellationToken cancellationToken = default);
    Task<OcrJob?> GetOcrJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<List<OcrJob>> GetPendingJobsAsync(int count = 10, CancellationToken cancellationToken = default);
}

public interface INlpService
{
    /// <summary>
    /// Understand a natural language query: detect language, extract keywords/entities/filters.
    /// Fully local — no LLM calls. Fast, deterministic.
    /// </summary>
    Task<NaturalLanguageQuery> UnderstandQueryAsync(string query, CancellationToken cancellationToken = default);

    Task<List<string>> ExtractKeywordsAsync(string text, int maxKeywords = 10, CancellationToken cancellationToken = default);
    Task<List<string>> ExtractEntitiesAsync(string text, CancellationToken cancellationToken = default);
    Task<float> CalculateSimilarityAsync(string text1, string text2, CancellationToken cancellationToken = default);
    Task<string> SummarizeTextAsync(string text, int maxLength = 200, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a semantic embedding vector for the given text using Ollama nomic-embed-text.
    /// Returns null if Ollama is unavailable — callers must degrade gracefully to BM25-only.
    /// </summary>
    Task<float[]?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}

public interface IStorageService
{
    Task<string> StoreFileAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> RetrieveFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<bool> DeleteFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);
    Task<long> GetFileSizeAsync(string filePath, CancellationToken cancellationToken = default);
}

public interface ITextExtractionService
{
    Task<string> ExtractTextAsync(Stream fileStream, string contentType, CancellationToken cancellationToken = default);
    Task<bool> SupportsContentType(string contentType);
}

public interface IMessageQueueService
{
    Task PublishAsync<T>(string queueName, T message, CancellationToken cancellationToken = default);
    Task SubscribeAsync<T>(string queueName, Func<T, Task> handler, CancellationToken cancellationToken = default);
}
