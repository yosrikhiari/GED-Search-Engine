using FluentAssertions;
using GED.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using GED.Core.Models;

namespace GED.Tests.Services;

[Trait("Category", "Unit")]
public class NlpServiceTests
{
    private readonly NlpService _service;

    public NlpServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "NLP:Enabled", "true" },
                { "NLP:EmbedApiEndpoint", "http://localhost:11434/api/embed" },
                { "NLP:EmbedModel", "bge-m3" }
            })
            .Build();

        _service = new NlpService(
            new HttpClient(),
            NullLogger<NlpService>.Instance,
            config);
    }

    [Fact]
    public async Task UnderstandQueryAsync_WithNullQuery_ReturnsUnknownLanguage()
    {
        // Act
        var result = await _service.UnderstandQueryAsync(null!);

        // Assert
        result.DetectedLanguage.Should().Be("unknown");
        result.IsUnderstood.Should().BeFalse();
    }

    [Fact]
    public async Task UnderstandQueryAsync_WithEmptyQuery_ReturnsUnknownLanguage()
    {
        // Act
        var result = await _service.UnderstandQueryAsync("");

        // Assert
        result.DetectedLanguage.Should().Be("unknown");
        result.IsUnderstood.Should().BeFalse();
    }

    [Fact]
    public async Task UnderstandQueryAsync_WithFrenchQuery_DetectsFrench()
    {
        // Arrange
        var query = "montrez-moi les factures de janvier";

        // Act
        var result = await _service.UnderstandQueryAsync(query);

        // Assert
        result.DetectedLanguage.Should().Be("fr");
        result.Keywords.Should().NotBeEmpty();
    }

    [Fact]
    public async Task UnderstandQueryAsync_WithEnglishQuery_DetectsEnglish()
    {
        // Arrange
        var query = "show me the invoices from january";

        // Act
        var result = await _service.UnderstandQueryAsync(query);

        // Assert
        result.DetectedLanguage.Should().Be("en");
        result.Keywords.Should().NotBeEmpty();
    }

    [Fact]
    public async Task UnderstandQueryAsync_WithArabicQuery_DetectsArabic()
    {
        // Arrange
        var query = "أرني فواتير يناير";

        // Act
        var result = await _service.UnderstandQueryAsync(query);

        // Assert
        result.DetectedLanguage.Should().Be("ar");
    }

    [Fact]
    public async Task UnderstandQueryAsync_WithMixedQuery_DetectsPrimaryLanguage()
    {
        // Arrange
        var query = "factures de janvier 2024";

        // Act
        var result = await _service.UnderstandQueryAsync(query);

        // Assert
        result.DetectedLanguage.Should().BeOneOf("fr", "en", "multi");
    }

    [Fact]
    public async Task UnderstandQueryAsync_WithCategoryKeywords_ExtractsCategories()
    {
        // Arrange
        var query = "trouve les factures et contrats";

        // Act
        var result = await _service.UnderstandQueryAsync(query);

        // Assert
        result.Keywords.Should().Contain(k => k.Contains("facture") || k.Contains("contrat"));
    }

    [Fact]
    public async Task UnderstandQueryAsync_WithYear_ExtractsYearFilter()
    {
        // Arrange
        var query = "factures 2024";

        // Act
        var result = await _service.UnderstandQueryAsync(query);

        // Assert
        result.ExtractedFilters.Should().NotBeEmpty();
    }

    [Fact]
    public async Task UnderstandQueryAsync_WithDateRange_ExtractsDateFilters()
    {
        // Arrange
        var query = "factures de janvier à mars 2024";

        // Act
        var result = await _service.UnderstandQueryAsync(query);

        // Assert
        result.ExtractedFilters.Should().NotBeEmpty();
    }

    [Fact]
    public async Task UnderstandQueryAsync_WithQuestion_DeterminesIntent()
    {
        // Arrange
        var query = "où sont mes factures?";

        // Act
        var result = await _service.UnderstandQueryAsync(query);

        // Assert
        // Intent is an enum, check it's a valid value
        Enum.IsDefined(typeof(GED.Core.Models.QueryIntent), result.Intent).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractKeywordsAsync_WithValidText_ReturnsKeywords()
    {
        // Arrange
        var text = "Les factures de janvier 2024 pour l'entreprise ABC";

        // Act
        var result = await _service.ExtractKeywordsAsync(text);

        // Assert
        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExtractKeywordsAsync_WithStopWords_FiltersThem()
    {
        // Arrange
        var text = "the quick brown fox jumps over the lazy dog";

        // Act
        var result = await _service.ExtractKeywordsAsync(text, 10);

        // Assert
        result.Should().NotContain("the");
    }

    [Fact]
    public async Task ExtractKeywordsAsync_WithMaxKeywords_LimitsResults()
    {
        // Arrange
        var text = "un deux trois quatre cinq six sept huit neuf dix onze douze";

        // Act
        var result = await _service.ExtractKeywordsAsync(text, 5);

        // Assert
        result.Should().HaveCountLessOrEqualTo(5);
    }

    [Fact]
    public async Task ExtractEntitiesAsync_WithDates_ExtractsDateEntities()
    {
        // Arrange
        var text = "Document daté du 15 janvier 2024";

        // Act
        var result = await _service.ExtractEntitiesAsync(text);

        // Assert
        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CalculateSimilarityAsync_WithIdenticalTexts_ReturnsOne()
    {
        // Arrange
        var text1 = "hello world";
        var text2 = "hello world";

        // Act
        var result = await _service.CalculateSimilarityAsync(text1, text2);

        // Assert
        result.Should().Be(1.0f);
    }

    [Fact]
    public async Task CalculateSimilarityAsync_WithNoOverlap_ReturnsZero()
    {
        // Arrange
        var text1 = "hello";
        var text2 = "goodbye";

        // Act
        var result = await _service.CalculateSimilarityAsync(text1, text2);

        // Assert
        result.Should().Be(0.0f);
    }

    [Fact]
    public async Task CalculateSimilarityAsync_WithPartialOverlap_ReturnsRatio()
    {
        // Arrange
        var text1 = "hello world foo bar";
        var text2 = "hello world baz qux";

        // Act
        var result = await _service.CalculateSimilarityAsync(text1, text2);

        // Assert
        result.Should().BeGreaterThan(0.0f);
        result.Should().BeLessThan(1.0f);
    }

    [Fact]
    public async Task SummarizeTextAsync_WithShortText_ReturnsOriginal()
    {
        // Arrange
        var text = "Short text";

        // Act
        var result = await _service.SummarizeTextAsync(text, 200);

        // Assert
        result.Should().Be(text);
    }

    [Fact]
    public async Task SummarizeTextAsync_WithLongText_ReturnsTruncated()
    {
        // Arrange
        var text = string.Join(". ", Enumerable.Range(1, 100).Select(i => $"Sentence number {i} with some content"));

        // Act
        var result = await _service.SummarizeTextAsync(text, 50);

        // Assert
        result.Length.Should().BeLessOrEqualTo(55); // 50 + "..."
    }
}
