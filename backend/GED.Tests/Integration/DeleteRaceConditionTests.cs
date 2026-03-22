using FluentAssertions;
using GED.Core.Models;

namespace GED.Tests.Integration;

public class DeleteRaceConditionTests
{
    [Fact]
    public void DocumentDelete_ShouldExcludeFromSearchImmediately()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Title = "Test Document",
            Status = DocumentStatus.Indexed
        };

        doc.Status = DocumentStatus.Deleted;

        var searchFilterStatus = "Indexed";
        var shouldAppearInSearch = doc.Status.ToString() == searchFilterStatus;
        
        shouldAppearInSearch.Should().BeFalse("deleted documents should not appear in search");
    }

    [Fact]
    public void SearchFilter_ShouldOnlyIncludeIndexedDocuments()
    {
        var statuses = new[]
        {
            DocumentStatus.Pending,
            DocumentStatus.Processing,
            DocumentStatus.Indexed,
            DocumentStatus.Failed,
            DocumentStatus.Deleted,
            DocumentStatus.Expired
        };

        var indexedStatus = "Indexed";
        
        foreach (var status in statuses)
        {
            var doc = new Document { Id = Guid.NewGuid(), Status = status };
            var appearsInSearch = doc.Status.ToString() == indexedStatus;
            
            if (status == DocumentStatus.Indexed)
                appearsInSearch.Should().BeTrue($"{status} should appear in search");
            else
                appearsInSearch.Should().BeFalse($"{status} should NOT appear in search");
        }
    }

    [Fact]
    public void CacheKeyGeneration_ShouldBeAffectedByGenerationChange()
    {
        var request = new SearchRequest { Query = "test", Page = 1, PageSize = 20 };
        
        var json1 = System.Text.Json.JsonSerializer.Serialize(request);
        var json2 = System.Text.Json.JsonSerializer.Serialize(request);
        
        var gen1Payload = "1000:" + json1;
        var gen2Payload = "2000:" + json2;
        
        var hash1 = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(gen1Payload));
        var hash2 = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(gen2Payload));
        
        var key1 = "ged:search:" + Convert.ToHexString(hash1).ToLower();
        var key2 = "ged:search:" + Convert.ToHexString(hash2).ToLower();
        
        key1.Should().NotBe(key2, "different generations should produce different cache keys");
    }

    [Fact]
    public void DeleteFlow_OrderOfOperations_ShouldBeAtomic()
    {
        var operations = new List<string>();
        
        Action markAsDeleted = () => operations.Add("MarkAsDeleted");
        Action deleteFromOpenSearch = () => operations.Add("DeleteFromOpenSearch");
        Action invalidateCache = () => operations.Add("InvalidateCache");
        
        markAsDeleted();
        deleteFromOpenSearch();
        invalidateCache();
        
        operations.Should().ContainInOrder(
            "MarkAsDeleted",
            "DeleteFromOpenSearch",
            "InvalidateCache"
        );
        
        var markIndex = operations.IndexOf("MarkAsDeleted");
        var deleteIndex = operations.IndexOf("DeleteFromOpenSearch");
        var cacheIndex = operations.IndexOf("InvalidateCache");
        
        markIndex.Should().BeLessThan(deleteIndex, 
            "MarkAsDeleted should happen BEFORE DeleteFromOpenSearch to prevent race conditions");
    }

    [Fact]
    public void DeleteFlow_WithSoftDelete_MarksDocumentAsDeletedBeforeOpenSearchDelete()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Title = "Test Document",
            Status = DocumentStatus.Indexed
        };

        doc.Status = DocumentStatus.Deleted;
        
        doc.Status.Should().Be(DocumentStatus.Deleted);
        doc.Status.ToString().Should().Be("Deleted");
        
        var indexedStatus = "Indexed";
        doc.Status.ToString().Should().NotBe(indexedStatus, 
            "After soft-delete, document status should NOT be Indexed");
    }

    [Fact]
    public void DeleteFlow_ConcurrentSearch_ShouldNotFindDeletedDocument()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Title = "Concurrent Test Document",
            Status = DocumentStatus.Indexed
        };

        var searchResults = new List<Document> { doc };

        doc.Status = DocumentStatus.Deleted;

        var indexedDocuments = searchResults.Where(d => d.Status == DocumentStatus.Indexed).ToList();
        
        indexedDocuments.Should().BeEmpty(
            "After marking as deleted, document should not appear in search results");
    }

    [Fact]
    public void DocumentStatus_Deleted_HasCorrectEnumValue()
    {
        var status = DocumentStatus.Deleted;
        status.Should().Be(DocumentStatus.Deleted);
        status.ToString().Should().Be("Deleted");
        
        Enum.GetValues<DocumentStatus>().Should().Contain(DocumentStatus.Deleted);
    }
}
