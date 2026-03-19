using FluentAssertions;
using GED.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;

namespace GED.Tests.Services;

public class DocumentChunkingServiceTests
{
    private readonly DocumentChunkingService _service;

    public DocumentChunkingServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "RAG:ChunkSize", "500" },
                { "RAG:ChunkOverlap", "100" },
                { "RAG:MinChunkLen", "50" }
            })
            .Build();

        _service = new DocumentChunkingService(
            NullLogger<DocumentChunkingService>.Instance,
            config);
    }

    [Fact]
    public void ChunkText_WithNullText_ReturnsEmptyList()
    {
        _service.ChunkText(Guid.NewGuid(), null).Should().BeEmpty();
    }

    [Fact]
    public void ChunkText_WithEmptyText_ReturnsEmptyList()
    {
        _service.ChunkText(Guid.NewGuid(), "").Should().BeEmpty();
    }

    [Fact]
    public void ChunkText_WithWhitespaceText_ReturnsEmptyList()
    {
        _service.ChunkText(Guid.NewGuid(), "   ").Should().BeEmpty();
    }

    [Fact]
    public void ChunkText_WithShortText_ReturnsSingleChunk()
    {
        var docId = Guid.NewGuid();
        var result = _service.ChunkText(docId, "This is a short document with less than chunk size.");

        result.Should().HaveCount(1);
        result[0].DocumentId.Should().Be(docId);
        result[0].ChunkIndex.Should().Be(0);
    }

    [Fact]
    public void ChunkText_WithParagraphs_UsesParagraphStrategy()
    {
        var docId = Guid.NewGuid();
        var text = "Para one with sufficient content here.\n\nPara two with sufficient content here.\n\nPara three here.";

        var result = _service.ChunkText(docId, text);

        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(c => c.DocumentId.Should().Be(docId));
    }

    [Fact]
    public void ChunkText_WithLongText_CreatesMultipleChunks()
    {
        var docId = Guid.NewGuid();
        var text = string.Join(" ", Enumerable.Repeat("Lorem ipsum dolor sit amet consectetur adipiscing elit. ", 50));

        var result = _service.ChunkText(docId, text);

        result.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void ChunkText_WithFlatText_UsesSlidingWindowStrategy()
    {
        var docId = Guid.NewGuid();
        var flatText = string.Join(" ", Enumerable.Repeat("Word", 200));

        var result = _service.ChunkText(docId, flatText);

        result.Should().NotBeEmpty();
        result.Select(c => c.ChunkIndex).Should().BeInAscendingOrder();
    }

    [Fact]
    public void ChunkText_PreservesChunkOrdering()
    {
        var docId = Guid.NewGuid();
        var text = string.Join("\n\n", new[]
        {
            "First paragraph with enough content to be a chunk.",
            "Second paragraph with enough content to be a chunk.",
            "Third paragraph with enough content to be a chunk.",
            "Fourth paragraph with enough content to be a chunk.",
            "Fifth paragraph with enough content to be a chunk."
        });

        var result = _service.ChunkText(docId, text);

        var indices = result.Select(c => c.ChunkIndex).ToList();
        indices.Should().Equal(indices.OrderBy(i => i));
    }

    [Fact]
    public void ChunkText_EachChunkHasUniqueIndex()
    {
        var docId = Guid.NewGuid();
        var text = string.Join("\n\n", new[]
        {
            "Paragraph one with enough content to be chunked properly and pass min length.",
            "Paragraph two with enough content to be chunked properly and pass min length.",
            "Paragraph three with enough content to be chunked properly and pass min length.",
            "Paragraph four with enough content to be chunked properly and pass min length.",
            "Paragraph five with enough content to be chunked properly and pass min length."
        });

        var result = _service.ChunkText(docId, text);

        var uniqueIndices = result.Select(c => c.ChunkIndex).Distinct().Count();
        uniqueIndices.Should().Be(result.Count);
    }

    [Fact]
    public void ChunkText_ChunksContainExpectedFormat()
    {
        var docId = Guid.NewGuid();
        var text = "This is test content for chunk validation. This is test content for chunk validation. This is test content.";

        var result = _service.ChunkText(docId, text);

        result.Should().AllSatisfy(chunk =>
        {
            chunk.ChunkId.Should().NotBeEmpty();
            chunk.DocumentId.Should().Be(docId);
            chunk.Text.Should().NotBeEmpty();
        });
    }

    [Fact]
    public void ChunkText_SlidingWindow_NoOverlap_CoversAllContent()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "RAG:ChunkSize", "100" },
                { "RAG:ChunkOverlap", "0" },
                { "RAG:MinChunkLen", "10" }
            })
            .Build();

        var service = new DocumentChunkingService(
            NullLogger<DocumentChunkingService>.Instance,
            config);

        var docId = Guid.NewGuid();
        var text = new string('X', 300);

        var result = service.ChunkText(docId, text);

        var combinedLength = result.Sum(c => c.Text.Length);
        combinedLength.Should().Be(300);
    }

    [Fact]
    public void ChunkText_SlidingWindow_WithStep_AdvancesCorrectly()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "RAG:ChunkSize", "200" },
                { "RAG:ChunkOverlap", "50" },
                { "RAG:MinChunkLen", "20" }
            })
            .Build();

        var service = new DocumentChunkingService(
            NullLogger<DocumentChunkingService>.Instance,
            config);

        var docId = Guid.NewGuid();
        var text = new string('A', 600);

        var result = service.ChunkText(docId, text);

        result.Should().NotBeEmpty();

        var combinedLength = result.Sum(c => c.Text.Length);
        combinedLength.Should().BeGreaterThan(600, "overlapping chunks should cover all text");
    }

    [Fact]
    public void ChunkText_ParagraphStrategy_LastParagraphOverlapWorks()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "RAG:ChunkSize", "100" },
                { "RAG:ChunkOverlap", "20" },
                { "RAG:MinChunkLen", "20" }
            })
            .Build();

        var service = new DocumentChunkingService(
            NullLogger<DocumentChunkingService>.Instance,
            config);

        var docId = Guid.NewGuid();
        var text = string.Join("\n\n", new[]
        {
            "AAAAA AAAAA AAAAA AAAAA AAAAA AAAAA AAAAA",
            "BBBBB BBBBB BBBBB BBBBB BBBBB BBBBB BBBBB",
            "CCCCC CCCCC CCCCC CCCCC CCCCC CCCCC CCCCC",
            "DDDDD DDDDD DDDDD DDDDD DDDDD DDDDD DDDDD",
            "EEEEE EEEEE EEEEE EEEEE EEEEE EEEEE EEEEE"
        });

        var result = service.ChunkText(docId, text);

        result.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public void ChunkText_ParagraphBoundaryConditions_NotSkipped()
    {
        var docId = Guid.NewGuid();
        var paras = Enumerable.Range(1, 5)
            .Select(i => new string((char)('A' + i - 1), 100))
            .ToArray();
        var text = string.Join("\n\n", paras);

        var result = _service.ChunkText(docId, text);

        var allText = string.Join("", result.Select(c => c.Text));
        allText.Should().Contain("AAAA");
        allText.Should().Contain("EEEEE");
    }
}
