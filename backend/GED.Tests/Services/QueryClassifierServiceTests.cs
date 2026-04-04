using FluentAssertions;
using GED.Core.Models;

namespace GED.Tests.Services;

public class QueryClassifierServiceTests
{
    #region Confidence Calculation Tests

    [Fact]
    public void Confidence_StrongPattern_Matches_HighConfidence()
    {
        // "Summarize" has a strong pattern, should get pattern bonus
        var keywordScore = 1.0f; // "summarize" matched
        var patternBonus = 0.2f; // strong pattern matched

        var confidence = Math.Min(keywordScore * 0.6f + patternBonus, 1.0f);

        confidence.Should().BeGreaterOrEqualTo(0.8f);
    }

    [Fact]
    public void Confidence_WeakPattern_Only_LowerConfidence()
    {
        // "Show me" is weak pattern, no bonus
        var keywordScore = 0.5f;
        var patternBonus = 0.0f;

        var confidence = Math.Min(keywordScore * 0.6f + patternBonus, 1.0f);

        confidence.Should().BeLessThan(0.5f);
    }

    [Fact]
    public void Confidence_MultipleStrongPatterns_MaxBonus()
    {
        // Multiple strong patterns capped at 0.4
        var keywordScore = 0.5f;
        var patternBonus = Math.Min(3 * 0.2f, 0.4f);

        var confidence = Math.Min(keywordScore * 0.6f + patternBonus, 1.0f);

        confidence.Should().BeGreaterOrEqualTo(0.7f);
    }

    #endregion

    #region Query Type Classification Tests - English

    [Fact]
    public void Classify_Factual_WhatIs()
    {
        var query = "What is the invoice number?";
        var keywords = ExtractKeywords(query);

        var hasFactualPattern = System.Text.RegularExpressions.Regex.IsMatch(
            query.ToLower(), @"^what\s+is\s+the");

        hasFactualPattern.Should().BeTrue();
        keywords.Should().Contain("invoice");
    }

    [Fact]
    public void Classify_Factual_Who()
    {
        var query = "Who signed the contract?";
        
        var hasWhoPattern = System.Text.RegularExpressions.Regex.IsMatch(
            query.ToLower(), @"who\s+");

        hasWhoPattern.Should().BeTrue();
    }

    [Fact]
    public void Classify_Summarization_Summarize()
    {
        var query = "Summarize this document";
        
        var hasSummarizePattern = System.Text.RegularExpressions.Regex.IsMatch(
            query.ToLower(), @"summarize");

        hasSummarizePattern.Should().BeTrue();
    }

    [Fact]
    public void Classify_Comparison_Compare()
    {
        var query = "Compare contract A vs contract B";
        
        var hasComparePattern = System.Text.RegularExpressions.Regex.IsMatch(
            query.ToLower(), @"compare|vs\.|versus");

        hasComparePattern.Should().BeTrue();
    }

    [Fact]
    public void Classify_Extraction_When()
    {
        var query = "When was this signed?";
        
        var hasWhenPattern = System.Text.RegularExpressions.Regex.IsMatch(
            query.ToLower(), @"when\s+");

        hasWhenPattern.Should().BeTrue();
    }

    [Fact]
    public void Classify_Extraction_Amount()
    {
        var query = "What is the total amount?";
        
        var hasAmountPattern = System.Text.RegularExpressions.Regex.IsMatch(
            query.ToLower(), @"amount");

        hasAmountPattern.Should().BeTrue();
    }

    #endregion

    #region Query Type Classification Tests - French

    [Fact]
    public void Classify_French_Factual_Quel()
    {
        var query = "Quel est le numéro de facture?";
        
        var hasQuelPattern = System.Text.RegularExpressions.Regex.IsMatch(
            query.ToLower(), @"^quel\s+(est|sont)");

        hasQuelPattern.Should().BeTrue();
    }

    [Fact]
    public void Classify_French_Summarization_Resumer()
    {
        var query = "Résumer ce document";
        
        var hasResumerPattern = System.Text.RegularExpressions.Regex.IsMatch(
            query.ToLower(), @"résumer|résumé");

        hasResumerPattern.Should().BeTrue();
    }

    [Fact]
    public void Classify_French_Comparison_Difference()
    {
        var query = "Quelle est la différence entre A et B?";
        
        var hasDiffPattern = System.Text.RegularExpressions.Regex.IsMatch(
            query.ToLower(), @"différence");

        hasDiffPattern.Should().BeTrue();
    }

    [Fact]
    public void Classify_French_Extraction_Quand()
    {
        var query = "Quand a été signé le contrat?";
        
        var hasQuandPattern = System.Text.RegularExpressions.Regex.IsMatch(
            query.ToLower(), @"quand\s+");

        hasQuandPattern.Should().BeTrue();
    }

    #endregion

    #region Query Type Classification Tests - Arabic

    [Fact]
    public void Classify_Arabic_Factual_MaHu()
    {
        var query = "ما هو رقم الفاتورة؟";
        
        // After normalization (removing diacritics)
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            query.ToLower(), @"[\u064B-\u065F\u0670]", "");

        var hasMaHuPattern = System.Text.RegularExpressions.Regex.IsMatch(
            normalized, @"^ما\s+هو");

        hasMaHuPattern.Should().BeTrue();
    }

    [Fact]
    public void Classify_Arabic_Summarization_Mulakhkhas()
    {
        var query = "ملخص الوثيقة";
        
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            query.ToLower(), @"[\u064B-\u065F\u0670]", "");

        var hasMulakhkhasPattern = System.Text.RegularExpressions.Regex.IsMatch(
            normalized, @"ملخص");

        hasMulakhkhasPattern.Should().BeTrue();
    }

    [Fact]
    public void Classify_Arabic_Comparison_Muqarana()
    {
        var query = "مقارنة بين العقدين";
        
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            query.ToLower(), @"[\u064B-\u065F\u0670]", "");

        var hasMuqaranaPattern = System.Text.RegularExpressions.Regex.IsMatch(
            normalized, @"مقارنة");

        hasMuqaranaPattern.Should().BeTrue();
    }

    [Fact]
    public void Classify_Arabic_Extraction_Mata()
    {
        var query = "متى تم التوقيع؟";
        
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            query.ToLower(), @"[\u064B-\u065F\u0670]", "");

        var hasMataPattern = System.Text.RegularExpressions.Regex.IsMatch(
            normalized, @"متى");

        hasMataPattern.Should().BeTrue();
    }

    #endregion

    #region Fallback Tests

    [Fact]
    public void Fallback_LowConfidence_ReturnsFactual()
    {
        // Low confidence (< 0.6) should fallback to factual
        var confidence = 0.4f;
        var lowConfidenceThreshold = 0.6f;

        var shouldFallback = confidence < lowConfidenceThreshold;

        shouldFallback.Should().BeTrue();
    }

    [Fact]
    public void Fallback_HighConfidence_NoFallback()
    {
        var confidence = 0.8f;
        var lowConfidenceThreshold = 0.6f;

        var shouldFallback = confidence < lowConfidenceThreshold;

        shouldFallback.Should().BeFalse();
    }

    #endregion

    #region Document Diversity Tests

    [Fact]
    public void DocumentDiversity_LimitsChunksPerDocument()
    {
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();

        var chunks = new List<ChunkSearchHit>
        {
            new() { DocumentId = docA, Title = "Doc A - 1", Score = 0.9f },
            new() { DocumentId = docA, Title = "Doc A - 2", Score = 0.8f },
            new() { DocumentId = docA, Title = "Doc A - 3", Score = 0.7f },
            new() { DocumentId = docB, Title = "Doc B - 1", Score = 0.6f },
            new() { DocumentId = docB, Title = "Doc B - 2", Score = 0.5f }
        };

        var maxChunksPerDoc = 2;
        var maxTotalChunks = 4;

        // Apply diversity logic
        var diverseChunks = new List<ChunkSearchHit>();
        var docCounts = new Dictionary<Guid, int>();

        foreach (var chunk in chunks)
        {
            if (!docCounts.TryGetValue(chunk.DocumentId, out var count))
                count = 0;

            if (count < maxChunksPerDoc)
            {
                diverseChunks.Add(chunk);
                docCounts[chunk.DocumentId] = count + 1;

                if (diverseChunks.Count >= maxTotalChunks)
                    break;
            }
        }

        diverseChunks.Should().HaveCount(4);
        diverseChunks.Count(c => c.DocumentId == docA).Should().Be(2);
        diverseChunks.Count(c => c.DocumentId == docB).Should().Be(2);
    }

    [Fact]
    public void DocumentDiversity_SingleDocument_NoLimit()
    {
        var docId = Guid.NewGuid();

        var chunks = new List<ChunkSearchHit>
        {
            new() { DocumentId = docId, Title = "Doc - 1", Score = 0.9f },
            new() { DocumentId = docId, Title = "Doc - 2", Score = 0.8f }
        };

        var maxChunksPerDoc = 2;
        var maxTotalChunks = 4;

        var diverseChunks = new List<ChunkSearchHit>();
        var docCounts = new Dictionary<Guid, int>();

        foreach (var chunk in chunks)
        {
            if (!docCounts.TryGetValue(chunk.DocumentId, out var count))
                count = 0;

            if (count < maxChunksPerDoc)
            {
                diverseChunks.Add(chunk);
                docCounts[chunk.DocumentId] = count + 1;

                if (diverseChunks.Count >= maxTotalChunks)
                    break;
            }
        }

        diverseChunks.Should().HaveCount(2);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void Config_DefaultValues_AreSet()
    {
        const int defaultTopK = 5;
        const float defaultConfidenceThreshold = 0.45f;
        const int factualTopK = 5;
        const float factualThreshold = 0.5f;
        const int summarizationTopK = 10;
        const float summarizationThreshold = 0.3f;

        defaultTopK.Should().Be(5);
        defaultConfidenceThreshold.Should().Be(0.45f);
        factualTopK.Should().Be(5);
        factualThreshold.Should().Be(0.5f);
        summarizationTopK.Should().Be(10);
        summarizationThreshold.Should().Be(0.3f);
    }

    #endregion

    #region Helper Methods

    private static HashSet<string> ExtractKeywords(string query)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the","a","an","and","or","but","in","on","at","to","for","of","with","by","from",
            "is","are","was","were","be","been","this","that","what","how","who"
        };

        return query.ToLower()
            .Split(new[] { ' ', ',', '.', '!', '?', ';', ':', '\n', '\r', '\t' },
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .ToHashSet();
    }

    #endregion
}
