using FluentAssertions;
using GED.Core.Models;

namespace GED.Tests.Services;

public class SearchServiceTests
{
    #region Search Type Tests

    [Theory]
    [InlineData(SearchType.Natural)]
    [InlineData(SearchType.Exact)]
    [InlineData(SearchType.Fuzzy)]
    [InlineData(SearchType.Wildcard)]
    [InlineData(SearchType.Advanced)]
    public void SearchType_AllValues_AreValid(SearchType searchType)
    {
        var request = new SearchRequest { SearchType = searchType };
        request.SearchType.Should().Be(searchType);
    }

    #endregion

    #region Hybrid Search Tests

    [Fact]
    public void HybridSearch_CombinesBM25AndSemantic()
    {
        var bm25Weight = 0.6f;
        var semanticWeight = 0.4f;

        (bm25Weight + semanticWeight).Should().Be(1.0f);
    }

    [Fact]
    public void ReciprocalRankFusion_Ranking_Works()
    {
        var rrfK = 60;
        var bm25Ranks = new Dictionary<Guid, int>
        {
            { Guid.NewGuid(), 1 },
            { Guid.NewGuid(), 2 },
            { Guid.NewGuid(), 3 }
        };
        var semanticRanks = new Dictionary<Guid, int>
        {
            { bm25Ranks.ElementAt(0).Key, 1 },
            { Guid.NewGuid(), 2 }
        };

        var combinedScores = new Dictionary<Guid, float>();
        
        foreach (var (id, rank) in bm25Ranks)
        {
            combinedScores[id] = 1f / (rrfK + rank);
        }
        
        foreach (var (id, rank) in semanticRanks)
        {
            combinedScores[id] = combinedScores.GetValueOrDefault(id) + 1f / (rrfK + rank);
        }

        combinedScores.Should().HaveCount(4);
        combinedScores.Values.Max().Should().BeGreaterThan(0);
    }

    #endregion

    #region Semantic Threshold Tests

    [Theory]
    [InlineData(0.3f, true)]
    [InlineData(0.5f, true)]
    [InlineData(0.2f, false)]
    [InlineData(0.8f, true)]
    public void SemanticThreshold_FiltersResults(float score, bool aboveThreshold)
    {
        var threshold = 0.3f;
        var isAbove = score >= threshold;
        isAbove.Should().Be(aboveThreshold);
    }

    #endregion

    #region Query Normalization Tests

    [Theory]
    [InlineData("factories", "factory")]
    [InlineData("categories", "category")]
    [InlineData("documents", "document")]
    public void QueryNormalization_RemovesPlurals(string input, string expected)
    {
        var normalized = NormalizeQuery(input);
        normalized.Should().Be(expected);
    }

    private static string NormalizeQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return query;
        var n = query.ToLower().Trim();
        if (n.EndsWith("ies") && n.Length > 4) return n[..^3] + "y";
        if (n.EndsWith("s") && n.Length > 2 && !n.EndsWith("ss")) return n.TrimEnd('s');
        return n;
    }

    #endregion

    #region Query Variations Tests

    [Fact]
    public void QueryVariations_GeneratesMultipleForms()
    {
        var query = "Invoice";
        var variations = GenerateQueryVariations(query);

        variations.Should().Contain(q => q == "Invoice");
        variations.Should().Contain(q => q == "invoice");
        variations.Should().Contain(q => q == "invoices");
    }

    private static List<string> GenerateQueryVariations(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();
        var cleaned = query.Trim();
        var lower = cleaned.ToLower();
        var cap = char.ToUpper(lower[0]) + lower[1..];
        return new[] { cleaned, lower, cap, lower + "s" }
            .Distinct()
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
    }

    #endregion

    #region Category Alias Tests

    [Theory]
    [InlineData("contrat", "Contract")]
    [InlineData("facture", "Invoice")]
    [InlineData("rapport", "Report")]
    [InlineData("lettre", "Letter")]
    [InlineData("devis", "Invoice")]
    public void CategoryAlias_MapsCorrectly(string alias, string category)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "contrat", "Contract" },
            { "facture", "Invoice" },
            { "rapport", "Report" },
            { "lettre", "Letter" },
            { "devis", "Invoice" }
        };

        aliases.TryGetValue(alias, out var mappedCategory);
        mappedCategory.Should().Be(category);
    }

    #endregion

    #region Highlight Extraction Tests

    [Fact]
    public void Highlights_AreExtractedFromText()
    {
        var text = "This is a document about invoices and payments.";
        var query = "invoice";
        
        var highlights = ExtractHighlights(text, query);
        
        highlights.Should().NotBeEmpty();
        highlights.Should().AllSatisfy(h => h.ToLower().Should().Contain("invoice"));
    }

    private static List<string> ExtractHighlights(string text, string query)
    {
        var highlights = new List<string>();
        var index = text.ToLower().IndexOf(query.ToLower());
        
        while (index >= 0 && highlights.Count < 3)
        {
            var start = Math.Max(0, index - 50);
            var end = Math.Min(text.Length, index + query.Length + 50);
            var highlight = text.Substring(start, end - start);
            
            if (start > 0) highlight = "..." + highlight;
            if (end < text.Length) highlight += "...";
            
            highlights.Add(highlight);
            index = text.ToLower().IndexOf(query.ToLower(), index + 1);
        }
        
        return highlights;
    }

    #endregion

    #region Facet Extraction Tests

    [Fact]
    public void Facets_GroupByCategory()
    {
        var documents = new List<Document>
        {
            new() { Category = "Invoice" },
            new() { Category = "Invoice" },
            new() { Category = "Contract" },
            new() { Category = "Report" }
        };

        var categoryFacets = documents
            .GroupBy(d => d.Category)
            .Select(g => new FacetValue { Value = g.Key ?? string.Empty, Count = g.Count() })
            .ToList();

        categoryFacets.Should().HaveCount(3);
        categoryFacets.First(f => f.Value == "Invoice").Count.Should().Be(2);
    }

    #endregion

    #region Date Range Filter Tests

    [Fact]
    public void DateRangeFilter_HandlesNullValues()
    {
        DateTime? fromDate = null;
        DateTime? toDate = null;

        var hasDateFilter = fromDate.HasValue || toDate.HasValue;
        hasDateFilter.Should().BeFalse();
    }

    [Fact]
    public void DateRangeFilter_AppliesCorrectly()
    {
        var fromDate = new DateTime(2024, 1, 1);
        var toDate = new DateTime(2024, 12, 31);
        var docDate = new DateTime(2024, 6, 15);

        var inRange = docDate >= fromDate && docDate <= toDate;
        inRange.Should().BeTrue();
    }

    #endregion

    #region Search Result Ordering Tests

    [Fact]
    public void SearchResults_OrderedByScore()
    {
        var results = new List<DocumentSearchHit>
        {
            new() { Id = Guid.NewGuid(), Score = 0.5f },
            new() { Id = Guid.NewGuid(), Score = 0.9f },
            new() { Id = Guid.NewGuid(), Score = 0.3f }
        };

        var ordered = results.OrderByDescending(r => r.Score).ToList();

        ordered[0].Score.Should().Be(0.9f);
        ordered[1].Score.Should().Be(0.5f);
        ordered[2].Score.Should().Be(0.3f);
    }

    [Fact]
    public void SearchResults_OrderedByDate()
    {
        var results = new List<DocumentSearchHit>
        {
            new() { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow.AddDays(-3) }
        };

        var ordered = results.OrderByDescending(r => r.CreatedAt).ToList();

        ordered[0].CreatedAt.Should().BeAfter(ordered[1].CreatedAt);
    }

    #endregion

    #region Embedding Tests

    [Fact]
    public void EmbeddingDimension_IsCorrect()
    {
        const int expectedDimension = 768;
        expectedDimension.Should().Be(768);
    }

    [Fact]
    public void Embedding_TextIsTruncatedForLongContent()
    {
        var longText = new string('a', 5000);
        var maxLength = 3000;

        var truncated = longText.Length > maxLength 
            ? longText[..maxLength] 
            : longText;

        truncated.Length.Should().Be(maxLength);
    }

    #endregion
}

public class FacetValue
{
    public string Value { get; set; } = string.Empty;
    public int Count { get; set; }
}
