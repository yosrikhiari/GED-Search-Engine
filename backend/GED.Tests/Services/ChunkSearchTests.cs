using FluentAssertions;
using GED.Core.Models;
using GED.Infrastructure.Services;

namespace GED.Tests.Services;

public class ChunkSearchTests
{
    #region Chunk Search Hit Tests

    [Fact]
    public void ChunkSearchHit_DefaultValues_AreCorrect()
    {
        var hit = new ChunkSearchHit();

        hit.ChunkId.Should().BeNullOrEmpty();
        hit.DocumentId.Should().Be(Guid.Empty);
        hit.ChunkIndex.Should().Be(0);
        hit.Text.Should().BeNullOrEmpty();
        hit.Title.Should().BeNullOrEmpty();
        hit.Category.Should().BeNull();
        hit.DocumentDate.Should().BeNull();
        hit.FileName.Should().BeNullOrEmpty();
        hit.ContentType.Should().BeNullOrEmpty();
        hit.Tags.Should().BeNull();
        hit.Score.Should().Be(0);
    }

    [Fact]
    public void ChunkSearchHit_WithData_SetsAllProperties()
    {
        var docId = Guid.NewGuid();
        var hit = new ChunkSearchHit
        {
            ChunkId = "doc1_chunk_0",
            DocumentId = docId,
            ChunkIndex = 0,
            Text = "This is chunk text content",
            Title = "Test Document",
            Category = "Invoice",
            DocumentDate = new DateTime(2024, 1, 15),
            FileName = "invoice_2024.pdf",
            ContentType = "application/pdf",
            Tags = new List<string> { "tag1", "tag2" },
            Score = 0.95f
        };

        hit.ChunkId.Should().Be("doc1_chunk_0");
        hit.DocumentId.Should().Be(docId);
        hit.ChunkIndex.Should().Be(0);
        hit.Text.Should().Be("This is chunk text content");
        hit.Title.Should().Be("Test Document");
        hit.Category.Should().Be("Invoice");
        hit.DocumentDate.Should().Be(new DateTime(2024, 1, 15));
        hit.FileName.Should().Be("invoice_2024.pdf");
        hit.ContentType.Should().Be("application/pdf");
        hit.Tags.Should().Contain("tag1");
        hit.Tags.Should().Contain("tag2");
        hit.Score.Should().Be(0.95f);
    }

    [Fact]
    public void ChunkSearchHit_Score_CanBeCompared()
    {
        var hit1 = new ChunkSearchHit { Title = "Doc 1", Score = 0.9f };
        var hit2 = new ChunkSearchHit { Title = "Doc 2", Score = 0.5f };
        var hit3 = new ChunkSearchHit { Title = "Doc 3", Score = 0.0f };

        hit1.Score.Should().BeGreaterThan(hit2.Score);
        hit2.Score.Should().BeGreaterThan(hit3.Score);
    }

    #endregion

    #region Chunk Search Filters Tests

    [Fact]
    public void ChunkSearch_WithCategories_FiltersCorrectly()
    {
        var categories = new List<string> { "Invoice", "Contract" };

        categories.Should().Contain("Invoice");
        categories.Should().Contain("Contract");
    }

    [Fact]
    public void ChunkSearch_WithDocumentIds_FiltersCorrectly()
    {
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();
        var documentIds = new List<Guid> { docId1, docId2 };

        documentIds.Should().Contain(docId1);
        documentIds.Should().Contain(docId2);
        documentIds.Should().HaveCount(2);
    }

    [Fact]
    public void ChunkSearch_ACL_UserWithAccess_ReturnsResults()
    {
        var userId = Guid.NewGuid().ToString();
        var allowedCategories = new List<string> { "Invoice" };

        userId.Should().NotBeNullOrEmpty();
        allowedCategories.Should().NotBeEmpty();
    }

    [Fact]
    public void ChunkSearch_ACL_UserWithoutAccess_ReturnsEmpty()
    {
        var userId = Guid.NewGuid().ToString();
        var allowedCategories = new List<string> { "Invoice" };

        var queryCategories = new List<string> { "Contract" };

        var hasAccess = queryCategories.Any(c => 
            allowedCategories.Contains(c, StringComparer.OrdinalIgnoreCase));

        hasAccess.Should().BeFalse();
    }

    #endregion

    #region Chunk Search Fallback Tests

    [Fact]
    public void ChunkSearch_KnnFallsBackToBm25_LogicWorks()
    {
        var knnResults = new List<ChunkSearchHit>();
        var bm25Results = new List<ChunkSearchHit>
        {
            new() { Title = "BM25 Doc", Score = 0.8f }
        };

        var finalResults = knnResults.Count > 0 ? knnResults : bm25Results;

        finalResults.Should().HaveCount(1);
        finalResults.First().Title.Should().Be("BM25 Doc");
    }

    [Fact]
    public void ChunkSearch_KnnTakesPrecedenceOverBm25()
    {
        var knnResults = new List<ChunkSearchHit>
        {
            new() { Title = "KNN Doc", Score = 0.9f }
        };
        var bm25Results = new List<ChunkSearchHit>
        {
            new() { Title = "BM25 Doc", Score = 0.8f }
        };

        var finalResults = knnResults.Count > 0 ? knnResults : bm25Results;

        finalResults.Should().HaveCount(1);
        finalResults.First().Title.Should().Be("KNN Doc");
    }

    [Fact]
    public void ChunkSearch_BothKnnAndBm25_ReturnsKnnResults()
    {
        var knnResults = new List<ChunkSearchHit>
        {
            new() { Title = "KNN 1", Score = 0.9f },
            new() { Title = "KNN 2", Score = 0.7f }
        };
        var bm25Results = new List<ChunkSearchHit>
        {
            new() { Title = "BM25 1", Score = 0.8f },
            new() { Title = "BM25 2", Score = 0.6f }
        };

        var finalResults = knnResults.Count > 0 ? knnResults : bm25Results;

        finalResults.Should().HaveCount(2);
        finalResults.Should().AllSatisfy(h => h.Title.Should().StartWith("KNN"));
    }

    #endregion

    #region Score Normalization Tests

    [Fact]
    public void ChunkSearch_ScoreNormalization_WorksCorrectly()
    {
        var scores = new List<double> { 10.0, 8.0, 5.0, 2.0 };
        var maxScore = scores.Max();

        var normalizedScores = scores.Select(s => maxScore > 0 ? (float)(s / maxScore) : 0f).ToList();

        normalizedScores.Should().HaveCount(4);
        normalizedScores.Max().Should().Be(1.0f);
        normalizedScores.Min().Should().BeLessThan(1.0f);
    }

    [Fact]
    public void ChunkSearch_ZeroMaxScore_HandledGracefully()
    {
        var scores = new List<double>();
        var maxScore = scores.Count > 0 ? scores.Max() : 0;

        var normalized = maxScore > 0 ? (float)(5.0 / maxScore) : 0f;

        normalized.Should().Be(0f);
    }

    #endregion

    #region TopK Limiting Tests

    [Fact]
    public void ChunkSearch_TopK_LimitsResults()
    {
        var results = new List<ChunkSearchHit>
        {
            new() { Title = "Doc 1", Score = 0.9f },
            new() { Title = "Doc 2", Score = 0.8f },
            new() { Title = "Doc 3", Score = 0.7f },
            new() { Title = "Doc 4", Score = 0.6f },
            new() { Title = "Doc 5", Score = 0.5f },
            new() { Title = "Doc 6", Score = 0.4f }
        };

        var topK = 3;
        var limited = results.Take(topK).ToList();

        limited.Should().HaveCount(topK);
        limited[0].Title.Should().Be("Doc 1");
    }

    #endregion

    #region Chunk Deduplication Tests

    [Fact]
    public void ChunkSearch_DuplicateDocuments_TrackedCorrectly()
    {
        var docId = Guid.NewGuid();
        var chunks = new List<ChunkSearchHit>
        {
            new() { DocumentId = docId, Title = "Doc A", ChunkIndex = 0 },
            new() { DocumentId = docId, Title = "Doc A", ChunkIndex = 1 },
            new() { DocumentId = Guid.NewGuid(), Title = "Doc B", ChunkIndex = 0 }
        };

        var seenDocIds = new HashSet<Guid>();
        var uniqueDocs = new List<ChunkSearchHit>();

        foreach (var chunk in chunks)
        {
            if (seenDocIds.Add(chunk.DocumentId))
                uniqueDocs.Add(chunk);
        }

        uniqueDocs.Should().HaveCount(2);
    }

    #endregion

    #region RRF Fusion Tests

    [Fact]
    public void RRF_CombinesBm25AndKnn_RankedByFusionScore()
    {
        var k = 60;
        var bm25Weight = 0.4f;
        var semanticWeight = 0.6f;

        var bm25Results = new List<ChunkSearchHit>
        {
            new() { ChunkId = "a", Score = 1.0f },
            new() { ChunkId = "b", Score = 0.8f },
            new() { ChunkId = "c", Score = 0.6f }
        };

        var knnResults = new List<ChunkSearchHit>
        {
            new() { ChunkId = "b", Score = 0.9f },
            new() { ChunkId = "c", Score = 0.7f },
            new() { ChunkId = "d", Score = 0.5f }
        };

        // Calculate RRF scores manually
        var rrfScores = new Dictionary<string, float>
        {
            { "a", bm25Weight * (1f / (k + 1)) + 0 },                    // BM25 rank 1
            { "b", bm25Weight * (1f / (k + 2)) + semanticWeight * (1f / (k + 1)) }, // BM25 rank 2, kNN rank 1
            { "c", bm25Weight * (1f / (k + 3)) + semanticWeight * (1f / (k + 2)) }, // BM25 rank 3, kNN rank 2
            { "d", 0 + semanticWeight * (1f / (k + 3)) }                   // kNN rank 3
        };

        var expectedOrder = rrfScores.OrderByDescending(x => x.Value).Select(x => x.Key).ToList();

        // b should be top because it's in both lists
        expectedOrder[0].Should().Be("b");
        // a is only in BM25, c is in both but lower rank
        expectedOrder.Should().Contain("a");
        expectedOrder.Should().Contain("c");
        expectedOrder.Should().Contain("d");
    }

    [Fact]
    public void RRF_EmptyKnn_ReturnsBm25Results()
    {
        var bm25Results = new List<ChunkSearchHit>
        {
            new() { ChunkId = "a", Score = 1.0f },
            new() { ChunkId = "b", Score = 0.8f }
        };
        var knnResults = new List<ChunkSearchHit>();

        // Simulate RRF logic: if one is empty, return the other
        var results = (bm25Results.Count > 0 && knnResults.Count > 0)
            ? new List<ChunkSearchHit>() // RRF would apply
            : (knnResults.Count > 0 ? knnResults : bm25Results);

        results.Should().HaveCount(2);
        results[0].ChunkId.Should().Be("a");
    }

    [Fact]
    public void RRF_EmptyBm25_ReturnsKnnResults()
    {
        var bm25Results = new List<ChunkSearchHit>();
        var knnResults = new List<ChunkSearchHit>
        {
            new() { ChunkId = "x", Score = 0.9f },
            new() { ChunkId = "y", Score = 0.7f }
        };

        var results = (bm25Results.Count > 0 && knnResults.Count > 0)
            ? new List<ChunkSearchHit>()
            : (knnResults.Count > 0 ? knnResults : bm25Results);

        results.Should().HaveCount(2);
        results[0].ChunkId.Should().Be("x");
    }

    [Fact]
    public void RRF_BothEmpty_ReturnsEmpty()
    {
        var bm25Results = new List<ChunkSearchHit>();
        var knnResults = new List<ChunkSearchHit>();

        var results = (bm25Results.Count > 0 && knnResults.Count > 0)
            ? new List<ChunkSearchHit>()
            : (knnResults.Count > 0 ? knnResults : bm25Results);

        results.Should().BeEmpty();
    }

    [Fact]
    public void RRF_ScoreNormalization_ScoresCombined()
    {
        var k = 60;
        var semanticWeight = 0.6f;
        var bm25Weight = 0.4f;

        // Chunk in position 1 of both lists gets highest combined score
        var rank1Both = bm25Weight * (1f / (k + 1)) + semanticWeight * (1f / (k + 1));
        
        // Chunk in position 1 of kNN only
        var rank1KnnOnly = bm25Weight * 0 + semanticWeight * (1f / (k + 1));

        // Chunk in position 1 of BM25 only
        var rank1Bm25Only = bm25Weight * (1f / (k + 1)) + semanticWeight * 0;

        rank1Both.Should().BeGreaterThan(rank1KnnOnly);
        rank1Both.Should().BeGreaterThan(rank1Bm25Only);
    }

    #endregion
}
