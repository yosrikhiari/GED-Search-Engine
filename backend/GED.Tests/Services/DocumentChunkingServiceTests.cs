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
        // Act
        var result = _service.ChunkText(Guid.NewGuid(), null);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ChunkText_WithEmptyText_ReturnsEmptyList()
    {
        // Act
        var result = _service.ChunkText(Guid.NewGuid(), "");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ChunkText_WithWhitespaceText_ReturnsEmptyList()
    {
        // Act
        var result = _service.ChunkText(Guid.NewGuid(), "   ");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ChunkText_WithShortText_ReturnsSingleChunk()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var shortText = "This is a short document with less than chunk size.";

        // Act
        var result = _service.ChunkText(docId, shortText);

        // Assert
        result.Should().HaveCount(1);
        result[0].DocumentId.Should().Be(docId);
        result[0].ChunkIndex.Should().Be(0);
        result[0].Text.Should().Contain("short document");
    }

    [Fact]
    public void ChunkText_WithParagraphs_UsesParagraphStrategy()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var text = @"This is the first paragraph with meaningful content.

This is the second paragraph with more content.

This is the third paragraph to test chunking.";

        // Act
        var result = _service.ChunkText(docId, text);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(chunk =>
        {
            chunk.DocumentId.Should().Be(docId);
            chunk.ChunkId.Should().StartWith(docId.ToString());
        });
    }

    [Fact]
    public void ChunkText_WithLongText_CreatesMultipleChunks()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var text = string.Join(" ", Enumerable.Repeat("Lorem ipsum dolor sit amet consectetur adipiscing elit. ", 50));

        // Act
        var result = _service.ChunkText(docId, text);

        // Assert
        result.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void ChunkText_WithFlatText_UsesSlidingWindowStrategy()
    {
        // Arrange - text without paragraph breaks (like OCR output)
        var docId = Guid.NewGuid();
        var flatText = string.Join(" ", Enumerable.Repeat("Word", 200));

        // Act
        var result = _service.ChunkText(docId, flatText);

        // Assert
        result.Should().NotBeEmpty();
        // Verify chunks have indices
        result.Select(c => c.ChunkIndex).Should().BeInAscendingOrder();
    }

    [Fact]
    public void ChunkText_PreservesChunkOrdering()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var text = string.Join("\n\n", new[]
        {
            "First paragraph with enough content to be a chunk.",
            "Second paragraph with enough content to be a chunk.",
            "Third paragraph with enough content to be a chunk.",
            "Fourth paragraph with enough content to be a chunk.",
            "Fifth paragraph with enough content to be a chunk."
        });

        // Act
        var result = _service.ChunkText(docId, text);

        // Assert
        var indices = result.Select(c => c.ChunkIndex).ToList();
        indices.Should().Equal(indices.OrderBy(i => i));
    }

    [Fact]
    public void ChunkText_EachChunkHasUniqueIndex()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var text = string.Join("\n\n", new[]
        {
            "Paragraph one is here with lots of content to ensure it gets chunked properly.",
            "Paragraph two is here with lots of content to ensure it gets chunked properly.",
            "Paragraph three is here with lots of content to ensure it gets chunked properly.",
            "Paragraph four is here with lots of content to ensure it gets chunked properly.",
            "Paragraph five is here with lots of content to ensure it gets chunked properly."
        });

        // Act
        var result = _service.ChunkText(docId, text);

        // Assert
        var uniqueIndices = result.Select(c => c.ChunkIndex).Distinct().Count();
        uniqueIndices.Should().Be(result.Count);
    }

    [Fact]
    public void ChunkText_ChunksContainExpectedFormat()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var text = "This is test content for chunk validation. This is test content for chunk validation. This is test content.";

        // Act
        var result = _service.ChunkText(docId, text);

        // Assert
        result.Should().AllSatisfy(chunk =>
        {
            chunk.ChunkId.Should().NotBeEmpty();
            chunk.DocumentId.Should().Be(docId);
            chunk.Text.Should().NotBeEmpty();
        });
    }
}
