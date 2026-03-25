using FluentAssertions;
using GED.Core.Interfaces;
using GED.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GED.Infrastructure.Services;

namespace GED.Tests.Services;

public class QueryClassifierSpotChecks
{
    [Fact]
    public async Task SpotCheck_Summarize_English()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RAG:EnableQueryClassification"] = "true",
            ["RAG:QueryClassification:FallbackToFactual"] = "true",
            ["RAG:QueryClassification:LowConfidenceThreshold"] = "0.6",
            ["RAG:QueryClassification:ComparisonMaxChunksPerDoc"] = "2",
            ["RAG:TopK"] = "5",
            ["RAG:ConfidenceThreshold"] = "0.45",
            // Type configs - use GetValue directly
            ["RAG:QueryClassification:TypeConfigs:summarization:topK"] = "10",
            ["RAG:QueryClassification:TypeConfigs:summarization:confidenceThreshold"] = "0.3"
        }).Build();

        var classifier = new QueryClassifierService(config, NullLogger<QueryClassifierService>.Instance);
        var result = await classifier.ClassifyAsync("Summarize this document");

        result.QueryType.Should().Be("summarization");
        result.Confidence.Should().BeGreaterThan(0.6f);
    }

    [Fact]
    public async Task SpotCheck_Factual_Arabic()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RAG:EnableQueryClassification"] = "true",
            ["RAG:QueryClassification:FallbackToFactual"] = "true",
            ["RAG:QueryClassification:LowConfidenceThreshold"] = "0.6",
            ["RAG:TopK"] = "5",
            ["RAG:ConfidenceThreshold"] = "0.45"
        }).Build();

        var classifier = new QueryClassifierService(config, NullLogger<QueryClassifierService>.Instance);
        var result = await classifier.ClassifyAsync("ما هو رقم الفاتورة");

        result.QueryType.Should().Be("factual");
    }

    [Fact]
    public async Task SpotCheck_Comparison_French()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RAG:EnableQueryClassification"] = "true",
            ["RAG:QueryClassification:FallbackToFactual"] = "true",
            ["RAG:QueryClassification:LowConfidenceThreshold"] = "0.6",
            ["RAG:TopK"] = "5",
            ["RAG:ConfidenceThreshold"] = "0.45",
            ["RAG:QueryClassification:TypeConfigs:comparison:topK"] = "8",
            ["RAG:QueryClassification:TypeConfigs:comparison:confidenceThreshold"] = "0.35"
        }).Build();

        var classifier = new QueryClassifierService(config, NullLogger<QueryClassifierService>.Instance);
        var result = await classifier.ClassifyAsync("Comparer le contrat A et le contrat B");

        result.QueryType.Should().Be("comparison");
    }

    [Fact]
    public async Task SpotCheck_Fallback_ShowMe()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RAG:EnableQueryClassification"] = "true",
            ["RAG:QueryClassification:FallbackToFactual"] = "true",
            ["RAG:QueryClassification:LowConfidenceThreshold"] = "0.6",
            ["RAG:TopK"] = "5",
            ["RAG:ConfidenceThreshold"] = "0.45"
        }).Build();

        var classifier = new QueryClassifierService(config, NullLogger<QueryClassifierService>.Instance);
        var result = await classifier.ClassifyAsync("Show me something");

        // "show me" is a weak pattern with no strong keywords, should fall back to factual
        result.QueryType.Should().Be("factual");
    }
}
