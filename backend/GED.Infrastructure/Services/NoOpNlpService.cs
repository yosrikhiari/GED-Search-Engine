using GED.Core.Interfaces;
using GED.Core.Models;

namespace GED.Infrastructure.Services;

public class NoOpNlpService : INlpService
{
    public Task<NaturalLanguageQuery> UnderstandQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        var result = new NaturalLanguageQuery
        {
            OriginalQuery = query,
            ProcessedQuery = query,
            Keywords = new List<string>(),
            Entities = new List<string>(),
            ExtractedFilters = null
        };
        return Task.FromResult(result);
    }

    public Task<List<string>> ExtractKeywordsAsync(string text, int maxKeywords = 10, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<string>());
    }

    public Task<List<string>> ExtractEntitiesAsync(string text, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<string>());
    }

    public Task<float> CalculateSimilarityAsync(string text1, string text2, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0f);
    }

    public Task<string> SummarizeTextAsync(string text, int maxLength = 200, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(string.Empty);
    }

    public Task<float[]?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<float[]?>(null);
    }
}