using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace GED.Infrastructure.Services;

/// <summary>
/// Hybrid chunk reranker that combines semantic similarity with lexical overlap
/// to improve RAG retrieval quality.
///
/// Uses the existing embedding model (bge-m3) for semantic scoring
/// and adds keyword-based relevance scoring for better QA retrieval.
///
/// Why hybrid scoring:
/// - Pure semantic similarity (cosine) may retrieve chunks that mention a topic
///   but don't contain the answer
/// - Lexical overlap ensures query keywords appear in the chunk
/// - Combining both improves answer relevance for question-answering tasks
/// </summary>
public class ChunkRerankerService : IChunkRerankerService
{
    private readonly INlpService _nlpService;
    private readonly ILogger<ChunkRerankerService> _logger;
    private readonly bool _enabled;
    private readonly float _semanticWeight;
    private readonly float _lexicalWeight;
    private readonly int _minKeywordOverlap;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","a","an","and","or","but","in","on","at","to","for","of","with","by","from",
        "as","is","was","are","were","be","been","being","have","has","had","do","does",
        "did","will","would","should","could","may","might","can","must","this","that",
        "these","those","i","you","he","she","it","we","they","me","my","your","his","her",
        "its","our","their","what","which","who","whom","where","when","why","how",
        "le","la","les","un","une","des","du","de","et","ou","mais","dans","sur","avec",
        "pour","par","en","ce","se","sa","son","leur","nos","vos","je","tu","il","elle",
        "nous","vous","ils","elles","est","sont","montre","cherche","trouve","affiche"
    };

    public bool IsAvailable => _enabled;

    public ChunkRerankerService(
        INlpService nlpService,
        IConfiguration configuration,
        ILogger<ChunkRerankerService> logger)
    {
        _nlpService = nlpService;
        _logger = logger;
        _enabled = configuration.GetValue<bool>("RAG:EnableReranking", true);
        _semanticWeight = configuration.GetValue<float>("RAG:RerankerSemanticWeight", 0.6f);
        _lexicalWeight = configuration.GetValue<float>("RAG:RerankerLexicalWeight", 0.4f);
        _minKeywordOverlap = configuration.GetValue<int>("RAG:RerankerMinKeywordOverlap", 1);
    }

    /// <inheritdoc />
    public async Task<List<ChunkSearchHit>> ReRankAsync(
        string query,
        List<ChunkSearchHit> chunks,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled || chunks.Count <= 1)
        {
            _logger.LogDebug("Reranking disabled or single chunk - returning original order");
            return chunks;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogWarning("Reranking called with empty query - returning original order");
            return chunks;
        }

        _logger.LogInformation("🤖 Reranking {Count} chunks for query: '{Query}'", chunks.Count, query);

        try
        {
            // Extract query keywords for lexical matching
            var queryKeywords = ExtractKeywords(query);

            // Generate query embedding for semantic similarity
            var queryEmbedding = await _nlpService.GenerateEmbeddingAsync(query, cancellationToken);

            // Score each chunk
            var scoredChunks = new List<(ChunkSearchHit Chunk, float Score)>();

            foreach (var chunk in chunks)
            {
                float semanticScore = 0f;
                float lexicalScore = 0f;

                // Semantic score: cosine similarity
                if (queryEmbedding != null && !string.IsNullOrWhiteSpace(chunk.Text))
                {
                    var chunkEmbedding = await _nlpService.GenerateEmbeddingAsync(
                        chunk.Text[..Math.Min(chunk.Text.Length, 1000)],
                        cancellationToken);

                    if (chunkEmbedding != null)
                    {
                        semanticScore = CosineSimilarity(queryEmbedding, chunkEmbedding);
                    }
                }

                // Lexical score: keyword overlap
                if (queryKeywords.Count > 0)
                {
                    var chunkKeywords = ExtractKeywords(chunk.Text);
                    var overlap = queryKeywords.Intersect(chunkKeywords).Count();
                    var coverage = (float)overlap / queryKeywords.Count;
                    lexicalScore = Math.Min(coverage * 2, 1.0f); // Scale up but cap at 1.0
                }

                // Combined score
                var combinedScore = (_semanticWeight * semanticScore) + (_lexicalWeight * lexicalScore);

                scoredChunks.Add((chunk, combinedScore));

                _logger.LogDebug(
                    "Chunk '{Title}': semantic={Sem:F3}, lexical={Lex:F3}, combined={Comb:F3}",
                    chunk.Title, semanticScore, lexicalScore, combinedScore);
            }

            // Sort by combined score descending
            var reranked = scoredChunks
                .OrderByDescending(x => x.Score)
                .Select(x => x.Chunk)
                .ToList();

            // Update scores with reranking scores
            for (int i = 0; i < reranked.Count; i++)
            {
                reranked[i].Score = scoredChunks[i].Score;
            }

            _logger.LogInformation(
                "✅ Reranking complete: top score={Score:F3}, improved order for {Count} chunks",
                reranked.FirstOrDefault()?.Score ?? 0, reranked.Count);

            return reranked;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reranking failed - returning original chunk order");
            return chunks;
        }
    }

    /// <summary>
    /// Extracts meaningful keywords from text (removes stop words).
    /// </summary>
    private static HashSet<string> ExtractKeywords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new HashSet<string>();

        var words = Regex.Split(text.ToLower(), @"[\s,.\-!?;:'""()\[\]{}]+")
            .Where(w => w.Length > 2 && !StopWords.Contains(w))
            .ToHashSet();

        return words;
    }

    /// <summary>
    /// Computes cosine similarity between two vectors.
    /// </summary>
    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        float dotProduct = 0f;
        float normA = 0f;
        float normB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = (float)Math.Sqrt(normA) * (float)Math.Sqrt(normB);
        return denominator > 0 ? dotProduct / denominator : 0f;
    }
}
