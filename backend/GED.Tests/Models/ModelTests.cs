using FluentAssertions;
using GED.Core.Models;

namespace GED.Tests.Models;

public class DocumentAclTests
{
    [Fact]
    public void DocumentAcl_DefaultValues_AreCorrect()
    {
        // Act
        var acl = new DocumentAcl();

        // Assert
        acl.Id.Should().Be(Guid.Empty);
        acl.DocumentId.Should().Be(Guid.Empty);
        acl.UserId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void DocumentAcl_WithValidData_SetsProperties()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Act
        var acl = new DocumentAcl
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            UserId = userId,
            GrantedAt = now
        };

        // Assert
        acl.DocumentId.Should().Be(docId);
        acl.UserId.Should().Be(userId);
        acl.GrantedAt.Should().Be(now);
    }

    [Fact]
    public void DocumentAcl_WithExpiration_CanBeChecked()
    {
        // Arrange
        var acl = new DocumentAcl
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ExpiresAt = DateTime.UtcNow.AddDays(-1) // Expired yesterday
        };

        // Act & Assert
        acl.ExpiresAt.HasValue.Should().BeTrue();
        (acl.ExpiresAt < DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void DocumentAcl_NotExpired_WhenExpirationInFuture()
    {
        // Arrange
        var acl = new DocumentAcl
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ExpiresAt = DateTime.UtcNow.AddDays(7) // Expires next week
        };

        // Act & Assert
        acl.ExpiresAt.HasValue.Should().BeTrue();
        (acl.ExpiresAt > DateTime.UtcNow).Should().BeTrue();
    }
}

public class SearchRequestTests
{
    [Fact]
    public void SearchRequest_DefaultValues_AreCorrect()
    {
        // Act
        var request = new SearchRequest();

        // Assert
        request.Query.Should().BeEmpty();
        request.Page.Should().Be(1);
        request.PageSize.Should().Be(20);
        request.UserId.Should().BeNull();
        request.UserRole.Should().BeNull();
    }

    [Fact]
    public void SearchRequest_WithAclData_SetsProperties()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();

        // Act
        var request = new SearchRequest
        {
            Query = "test query",
            Page = 2,
            PageSize = 20,
            UserId = userId,
            UserRole = "Admin",
            Categories = new List<string> { "Invoice", "Contract" }
        };

        // Assert
        request.Query.Should().Be("test query");
        request.Page.Should().Be(2);
        request.PageSize.Should().Be(20);
        request.UserId.Should().Be(userId);
        request.UserRole.Should().Be("Admin");
        request.Categories.Should().Contain("Invoice");
    }
}

public class NaturalLanguageQueryTests
{
    [Fact]
    public void NaturalLanguageQuery_DefaultValues_AreCorrect()
    {
        // Act
        var query = new NaturalLanguageQuery();

        // Assert
        query.OriginalQuery.Should().BeEmpty();
        query.ProcessedQuery.Should().BeEmpty();
        query.DetectedLanguage.Should().Be("unknown");
        query.IsUnderstood.Should().BeTrue();
    }

    [Fact]
    public void NaturalLanguageQuery_WithFullData_SetsAllProperties()
    {
        // Act
        var query = new NaturalLanguageQuery
        {
            OriginalQuery = "show me invoices",
            ProcessedQuery = "invoices",
            Keywords = new List<string> { "invoices" },
            Entities = new List<string>(),
            DetectedLanguage = "en",
            IsUnderstood = true
        };

        // Assert
        query.OriginalQuery.Should().Be("show me invoices");
        query.ProcessedQuery.Should().Be("invoices");
        query.Keywords.Should().Contain("invoices");
    }
}

public class RagRequestModelTests
{
    [Fact]
    public void RagRequest_DefaultValues_AreCorrect()
    {
        // Act
        var request = new RagRequest();

        // Assert
        request.Query.Should().BeNullOrEmpty();
        request.Username.Should().BeNull();
        request.DocumentIds.Should().BeNull();
    }

    [Fact]
    public void RagRequest_WithDocumentIds_SetsProperties()
    {
        // Arrange
        var docIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        // Act
        var request = new RagRequest
        {
            Query = "summarize these documents",
            Username = "testuser",
            DocumentIds = docIds
        };

        // Assert
        request.Query.Should().Be("summarize these documents");
        request.Username.Should().Be("testuser");
        request.DocumentIds.Should().HaveCount(2);
    }
}
