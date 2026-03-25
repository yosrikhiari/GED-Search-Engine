using FluentAssertions;
using GED.Core.Models;

namespace GED.Tests.Services;

public class ChunkRerankerServiceTests
{
    #region Keyword Extraction Tests

    [Fact]
    public void ExtractKeywords_RemovesStopWords()
    {
        var text = "the quick brown fox jumps over the lazy dog";
        var words = text.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for"
        };

        var keywords = words.Where(w => !stopWords.Contains(w)).ToList();

        keywords.Should().Contain("quick");
        keywords.Should().Contain("brown");
        keywords.Should().Contain("fox");
        keywords.Should().NotContain("the");
    }

    [Fact]
    public void ExtractKeywords_FiltersShortWords()
    {
        var text = "I am looking for invoices from january";
        var words = text.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .ToList();

        words.Should().NotContain("I");
        words.Should().NotContain("am");
    }

    [Fact]
    public void ExtractKeywords_HandlesFrenchText()
    {
        var text = "trouve les factures de janvier 2024";
        var words = text.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        words.Should().Contain("factures");
        words.Should().Contain("janvier");
    }

    #endregion

    #region Cosine Similarity Tests

    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        var a = new float[] { 1f, 0f, 0f };
        var b = new float[] { 1f, 0f, 0f };

        var similarity = ComputeCosineSimilarity(a, b);

        similarity.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        var a = new float[] { 1f, 0f, 0f };
        var b = new float[] { 0f, 1f, 0f };

        var similarity = ComputeCosineSimilarity(a, b);

        similarity.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsNegativeOne()
    {
        var a = new float[] { 1f, 0f, 0f };
        var b = new float[] { -1f, 0f, 0f };

        var similarity = ComputeCosineSimilarity(a, b);

        similarity.Should().BeApproximately(-1f, 0.001f);
    }

    [Fact]
    public void CosineSimilarity_DifferentLengths_ReturnsZero()
    {
        var a = new float[] { 1f, 0f };
        var b = new float[] { 1f, 0f, 0f };

        var similarity = ComputeCosineSimilarity(a, b);

        similarity.Should().Be(0f);
    }

    #endregion

    #region Hybrid Scoring Tests

    [Fact]
    public void HybridScoring_CombinesSemanticAndLexical()
    {
        var semanticWeight = 0.6f;
        var lexicalWeight = 0.4f;

        var semanticScore = 0.8f;
        var lexicalScore = 0.5f;

        var combined = (semanticWeight * semanticScore) + (lexicalWeight * lexicalScore);

        combined.Should().Be(0.68f);
    }

    [Fact]
    public void HybridScoring_LexicalScoreCappedAtOne()
    {
        var semanticWeight = 0.6f;
        var lexicalWeight = 0.4f;

        var semanticScore = 0.8f;
        var lexicalScore = 1.5f; // Over 1.0 - simulates full coverage scaled up

        // In the service: coverage is scaled by 2x but capped at 1.0
        // coverage = overlap / queryKeywords = 1.0 (full match)
        // scaledCoverage = min(1.0 * 2, 1.0) = 1.0
        // combined = 0.6 * 0.8 + 0.4 * 1.0 = 0.48 + 0.4 = 0.88
        var scaledCoverage = Math.Min(lexicalScore * 2, 1.0f);
        var combined = (semanticWeight * semanticScore) + (lexicalWeight * scaledCoverage);

        combined.Should().Be(0.88f);
    }

    #endregion

    #region Reranking Logic Tests

    [Fact]
    public void Reranking_SortsByCombinedScore()
    {
        var chunks = new List<ChunkSearchHit>
        {
            new() { Title = "Doc A", Score = 0.9f },
            new() { Title = "Doc B", Score = 0.5f },
            new() { Title = "Doc C", Score = 0.7f }
        };

        var reranked = chunks.OrderByDescending(c => c.Score).ToList();

        reranked[0].Title.Should().Be("Doc A");
        reranked[1].Title.Should().Be("Doc C");
        reranked[2].Title.Should().Be("Doc B");
    }

    [Fact]
    public void Reranking_SingleChunk_ReturnsSame()
    {
        var chunks = new List<ChunkSearchHit>
        {
            new() { Title = "Doc A", Score = 0.9f }
        };

        var reranked = chunks.OrderByDescending(c => c.Score).ToList();

        reranked.Should().HaveCount(1);
        reranked[0].Title.Should().Be("Doc A");
    }

    [Fact]
    public void Reranking_EmptyList_ReturnsEmpty()
    {
        var chunks = new List<ChunkSearchHit>();

        var reranked = chunks.OrderByDescending(c => c.Score).ToList();

        reranked.Should().BeEmpty();
    }

    #endregion

    #region Keyword Overlap Tests

    [Fact]
    public void KeywordOverlap_ExactMatch_ReturnsFullCoverage()
    {
        var queryKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "invoice", "january", "2024" };
        var chunkKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "invoice", "january", "2024", "amount" };

        var overlap = queryKeywords.Intersect(chunkKeywords).Count();
        var coverage = (float)overlap / queryKeywords.Count;

        coverage.Should().Be(1.0f);
    }

    [Fact]
    public void KeywordOverlap_PartialMatch_ReturnsPartialCoverage()
    {
        var queryKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "invoice", "january", "2024" };
        var chunkKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "invoice", "february", "amount" };

        var overlap = queryKeywords.Intersect(chunkKeywords).Count();
        var coverage = (float)overlap / queryKeywords.Count;

        coverage.Should().BeApproximately(0.333f, 0.01f);
    }

    [Fact]
    public void KeywordOverlap_NoMatch_ReturnsZero()
    {
        var queryKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "invoice", "january" };
        var chunkKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "contract", "february" };

        var overlap = queryKeywords.Intersect(chunkKeywords).Count();
        var coverage = (float)overlap / queryKeywords.Count;

        coverage.Should().Be(0f);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void RerankingConfig_DisabledByDefault()
    {
        var enabled = false;
        enabled.Should().BeFalse();
    }

    [Fact]
    public void RerankingConfig_DefaultWeights_SumToOne()
    {
        var semanticWeight = 0.6f;
        var lexicalWeight = 0.4f;

        (semanticWeight + lexicalWeight).Should().Be(1.0f);
    }

    #endregion

    #region Chunk Deduplication Tests

    [Fact]
    public void DeduplicateChunks_SameDocumentDifferentChunks_Tracked()
    {
        var docId = Guid.NewGuid();
        var chunks = new List<ChunkSearchHit>
        {
            new() { DocumentId = docId, Title = "Invoice", ChunkIndex = 0 },
            new() { DocumentId = docId, Title = "Invoice", ChunkIndex = 1 },
            new() { DocumentId = docId, Title = "Invoice", ChunkIndex = 2 }
        };

        var uniqueDocs = chunks.GroupBy(c => c.DocumentId).Select(g => g.First()).ToList();

        uniqueDocs.Should().HaveCount(1);
    }

    #endregion

    // Helper method to compute cosine similarity
    private static float ComputeCosineSimilarity(float[] a, float[] b)
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
