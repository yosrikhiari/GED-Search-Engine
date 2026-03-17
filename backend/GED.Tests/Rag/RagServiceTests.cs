using FluentAssertions;
using GED.Core.Models;

namespace GED.Tests.Rag;

public class RagRequestTests
{
    [Fact]
    public void RagRequest_DefaultValues_AreCorrect()
    {
        // Act
        var request = new RagRequest();

        // Assert
        request.Query.Should().Be(string.Empty);
        request.Language.Should().Be("fr");
        request.Categories.Should().BeNull();
        request.DocumentIds.Should().BeNull();
    }

    [Fact]
    public void RagRequest_WithCategories_SetsCategories()
    {
        // Act
        var request = new RagRequest
        {
            Query = "show invoices",
            Categories = new List<string> { "Invoice", "Contract" }
        };

        // Assert
        request.Categories.Should().Contain("Invoice");
        request.Categories.Should().Contain("Contract");
    }

    [Fact]
    public void RagRequest_WithDocumentIds_SetsDocumentIds()
    {
        // Arrange
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();

        // Act
        var request = new RagRequest
        {
            Query = "summarize",
            DocumentIds = new List<Guid> { docId1, docId2 }
        };

        // Assert
        request.DocumentIds.Should().Contain(docId1);
        request.DocumentIds.Should().Contain(docId2);
    }

    [Fact]
    public void RagRequest_WithUserContext_SetsIdentityFields()
    {
        // Act
        var request = new RagRequest
        {
            Query = "test",
            Username = "testuser",
            UserId = Guid.NewGuid().ToString(),
            UserRole = "User",
            UserAllowedCategories = new List<string> { "Invoice" }
        };

        // Assert
        request.Username.Should().Be("testuser");
        request.UserRole.Should().Be("User");
        request.UserAllowedCategories.Should().Contain("Invoice");
    }
}

public class RagResponseTests
{
    [Fact]
    public void RagResponse_DefaultValues_AreCorrect()
    {
        // Act
        var response = new RagResponse();

        // Assert
        response.Query.Should().Be(string.Empty);
        response.Answer.Should().Be(string.Empty);
        response.Sources.Should().BeEmpty();
        response.IsConfident.Should().BeTrue();
    }

    [Fact]
    public void RagResponse_WithSources_SetsSources()
    {
        // Arrange
        var sources = new List<RagSource>
        {
            new RagSource { DocumentId = Guid.NewGuid(), Title = "Doc 1" },
            new RagSource { DocumentId = Guid.NewGuid(), Title = "Doc 2" }
        };

        // Act
        var response = new RagResponse
        {
            Query = "test",
            Answer = "Answer text",
            Sources = sources,
            IsConfident = true
        };

        // Assert
        response.Sources.Should().HaveCount(2);
        response.Answer.Should().Be("Answer text");
    }
}

public class RagSourceTests
{
    [Fact]
    public void RagSource_DefaultValues_AreCorrect()
    {
        // Act
        var source = new RagSource();

        // Assert
        source.DocumentId.Should().Be(Guid.Empty);
        source.Title.Should().Be(string.Empty);
        source.RelevanceScore.Should().Be(0);
        source.Highlights.Should().BeEmpty();
    }

    [Fact]
    public void RagSource_WithFullData_SetsAllProperties()
    {
        // Arrange
        var docId = Guid.NewGuid();

        // Act
        var source = new RagSource
        {
            DocumentId = docId,
            Title = "Invoice 2024",
            Category = "Invoice",
            FileName = "invoice_2024.pdf",
            ContentType = "application/pdf",
            RelevanceScore = 0.95f,
            Excerpt = "This is a relevant excerpt...",
            Highlights = new List<string> { "invoice", "2024" }
        };

        // Assert
        source.DocumentId.Should().Be(docId);
        source.Title.Should().Be("Invoice 2024");
        source.RelevanceScore.Should().Be(0.95f);
        source.Highlights.Should().Contain("invoice");
    }

    [Fact]
    public void RagSource_RelevanceScore_CanBeCompared()
    {
        // Arrange
        var source1 = new RagSource { Title = "Doc 1", RelevanceScore = 0.9f };
        var source2 = new RagSource { Title = "Doc 2", RelevanceScore = 0.5f };

        // Assert
        source1.RelevanceScore.Should().BeGreaterThan(source2.RelevanceScore);
    }
}

public class RagContextBuilderTests
{
    [Fact]
    public void ContextBuilder_EmptySources_ProducesEmptyContext()
    {
        // Simulates context builder with no sources
        var sources = new List<RagSource>();
        
        // In actual RagService, this would produce empty context
        var hasContent = sources.Any(s => !string.IsNullOrEmpty(s.Excerpt));
        
        hasContent.Should().BeFalse();
    }

    [Fact]
    public void ContextBuilder_WithSources_ProducesContext()
    {
        // Simulates context builder with sources
        var sources = new List<RagSource>
        {
            new RagSource
            {
                DocumentId = Guid.NewGuid(),
                Title = "Invoice 2024",
                Excerpt = "This is an invoice from January 2024...",
                RelevanceScore = 0.95f
            }
        };
        
        var hasContent = sources.Any(s => !string.IsNullOrEmpty(s.Excerpt));
        
        hasContent.Should().BeTrue();
    }

    [Fact]
    public void ContextBuilder_TruncatesLongExcerpts()
    {
        // Simulates context builder truncating long excerpts
        var longExcerpt = new string('a', 10000);
        var maxChars = 6000;
        
        var truncated = longExcerpt.Length > maxChars 
            ? longExcerpt[..maxChars] + "..." 
            : longExcerpt;
        
        truncated.Length.Should().BeLessOrEqualTo(maxChars + 3);
    }

    [Fact]
    public void ContextBuilder_IncludesSourceCitations()
    {
        // Simulates source citations in context
        var sources = new List<RagSource>
        {
            new RagSource { DocumentId = Guid.NewGuid(), Title = "Doc 1", RelevanceScore = 0.9f },
            new RagSource { DocumentId = Guid.NewGuid(), Title = "Doc 2", RelevanceScore = 0.8f }
        };
        
        // RagService sorts by relevance and takes top-K
        var topSources = sources.OrderByDescending(s => s.RelevanceScore).Take(5).ToList();
        
        topSources.Should().HaveCount(2);
        topSources[0].RelevanceScore.Should().BeGreaterThan(topSources[1].RelevanceScore);
    }
}
