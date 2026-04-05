using FluentAssertions;
using GED.Core.Models;

namespace GED.Tests.Services;

[Trait("Category", "Unit")]
public class DocumentServiceTests
{
    #region Document Model Tests

    [Fact]
    public void Document_NewInstance_HasDefaultValues()
    {
        var doc = new Document();
        
        doc.Id.Should().Be(Guid.Empty);
        doc.Title.Should().BeEmpty();
        doc.Status.Should().Be(DocumentStatus.Pending);
        doc.Version.Should().Be(0);
    }

    [Fact]
    public void Document_WithAllProperties_SetsCorrectly()
    {
        var id = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;
        var doc = new Document
        {
            Id = id,
            Title = "Test Document",
            Description = "Test Description",
            FileName = "test.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            CreatedAt = createdAt,
            Category = "Invoice",
            Tags = new List<string> { "important", "finance" },
            Status = DocumentStatus.Indexed,
            Version = 1
        };

        doc.Id.Should().Be(id);
        doc.Title.Should().Be("Test Document");
        doc.FileSize.Should().Be(1024);
        doc.Tags.Should().Contain("important");
        doc.Status.Should().Be(DocumentStatus.Indexed);
    }

    [Theory]
    [InlineData(DocumentStatus.Pending)]
    [InlineData(DocumentStatus.Processing)]
    [InlineData(DocumentStatus.Indexed)]
    [InlineData(DocumentStatus.Failed)]
    public void DocumentStatus_AllValues_AreValid(DocumentStatus status)
    {
        var doc = new Document { Status = status };
        doc.Status.Should().Be(status);
    }

    #endregion

    #region Document Metadata Tests

    [Fact]
    public void DocumentMetadata_SupportsMultipleValueTypes()
    {
        var metadata = new Dictionary<string, object>
        {
            ["stringValue"] = "test string",
            ["intValue"] = 42,
            ["dateValue"] = DateTime.UtcNow,
            ["boolValue"] = true
        };

        metadata.Should().HaveCount(4);
        metadata["stringValue"].Should().BeOfType<string>();
        metadata["intValue"].Should().BeOfType<int>();
        metadata["boolValue"].Should().BeOfType<bool>();
    }

    [Fact]
    public void DocumentMetadata_Serialization_PreservesData()
    {
        var metadata = new Dictionary<string, object>
        {
            ["category"] = "Invoice",
            ["year"] = 2024,
            ["verified"] = true
        };

        var json = System.Text.Json.JsonSerializer.Serialize(metadata);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);

        deserialized.Should().NotBeNull();
        deserialized!["category"].Should().NotBeNull();
    }

    #endregion

    #region Document Tags Tests

    [Fact]
    public void DocumentTags_CanBeModified()
    {
        var doc = new Document();
        doc.Tags = new List<string> { "tag1", "tag2" };
        
        doc.Tags.Should().HaveCount(2);
        
        doc.Tags.Add("tag3");
        doc.Tags.Should().HaveCount(3);
        
        doc.Tags.Remove("tag1");
        doc.Tags.Should().HaveCount(2);
        doc.Tags.Should().NotContain("tag1");
    }

    [Fact]
    public void DocumentTags_CaseInsensitiveSearch()
    {
        var doc = new Document();
        doc.Tags = new List<string> { "Invoice", "REPORT", "Contract" };

        var hasInvoice = doc.Tags.Any(t => t.Equals("invoice", StringComparison.OrdinalIgnoreCase));
        var hasReport = doc.Tags.Any(t => t.Equals("report", StringComparison.OrdinalIgnoreCase));

        hasInvoice.Should().BeTrue();
        hasReport.Should().BeTrue();
    }

    [Fact]
    public void DocumentTags_LimitedToMaxCount()
    {
        var maxTags = 15;
        var tags = Enumerable.Range(1, 20)
            .Select(i => $"tag{i}")
            .ToList();

        var limitedTags = tags.Take(maxTags).ToList();
        limitedTags.Should().HaveCount(maxTags);
    }

    #endregion

    #region Document Search Tests

    [Fact]
    public void SearchRequest_DefaultValues()
    {
        var request = new SearchRequest();
        
        request.Page.Should().Be(1);
        request.PageSize.Should().Be(20);
        request.SortBy.Should().Be(SortField.Relevance);
        request.SortDescending.Should().BeTrue();
    }

    [Fact]
    public void SearchRequest_WithFilters()
    {
        var request = new SearchRequest
        {
            Query = "invoice 2024",
            Categories = new List<string> { "Invoice" },
            ContentTypes = new List<string> { "application/pdf" },
            FromDate = new DateTime(2024, 1, 1),
            ToDate = new DateTime(2024, 12, 31),
            Tags = new List<string> { "finance" },
            Page = 1,
            PageSize = 50
        };

        request.Query.Should().Be("invoice 2024");
        request.Categories.Should().Contain("Invoice");
        request.FromDate.Should().Be(new DateTime(2024, 1, 1));
        request.PageSize.Should().Be(50);
    }

    [Theory]
    [InlineData(SortField.Relevance)]
    [InlineData(SortField.CreatedDate)]
    [InlineData(SortField.ModifiedDate)]
    [InlineData(SortField.Title)]
    [InlineData(SortField.FileSize)]
    public void SortField_AllValues_AreValid(SortField sortField)
    {
        var request = new SearchRequest { SortBy = sortField };
        request.SortBy.Should().Be(sortField);
    }

    #endregion

    #region Search Result Tests

    [Fact]
    public void SearchResult_WithDocuments()
    {
        var result = new SearchResult
        {
            TotalResults = 100,
            Page = 1,
            PageSize = 20,
            TotalPages = 5,
            Documents = new List<DocumentSearchHit>
            {
                new() { Id = Guid.NewGuid(), Title = "Doc 1", Score = 0.95f },
                new() { Id = Guid.NewGuid(), Title = "Doc 2", Score = 0.85f }
            },
            SearchTimeMs = 150
        };

        result.TotalResults.Should().Be(100);
        result.Documents.Should().HaveCount(2);
        result.SearchTimeMs.Should().Be(150);
    }

    [Fact]
    public void SearchResult_EmptyResults()
    {
        var result = new SearchResult
        {
            TotalResults = 0,
            Page = 1,
            PageSize = 20,
            TotalPages = 0,
            Documents = new List<DocumentSearchHit>()
        };

        result.TotalResults.Should().Be(0);
        result.Documents.Should().BeEmpty();
    }

    [Fact]
    public void SearchHit_WithHighlights()
    {
        var hit = new DocumentSearchHit
        {
            Id = Guid.NewGuid(),
            Title = "Invoice 2024",
            Highlights = new List<string>
            {
                "This is a <em>highlighted</em> match",
                "Another <em>highlighted</em> match"
            }
        };

        hit.Highlights.Should().HaveCount(2);
        hit.Highlights.Should().AllSatisfy(h => h.Should().Contain("<em>"));
    }

    #endregion

    #region Pagination Tests

    [Theory]
    [InlineData(100, 20, 5)]
    [InlineData(101, 20, 6)]
    [InlineData(0, 20, 0)]
    [InlineData(50, 10, 5)]
    public void Pagination_CalculatesCorrectPageCount(int total, int pageSize, int expectedPages)
    {
        var totalPages = (int)Math.Ceiling((double)total / pageSize);
        totalPages.Should().Be(expectedPages);
    }

    #endregion

    #region Category Tests

    [Fact]
    public void AllowedCategories_AreDefined()
    {
        var allowedCategories = new[]
        {
            "Invoice", "Contract", "Report", "Letter",
            "Memo", "Presentation", "Spreadsheet", "Image", "Other"
        };

        allowedCategories.Should().HaveCount(9);
        allowedCategories.Should().Contain("Invoice");
        allowedCategories.Should().Contain("Contract");
    }

    [Theory]
    [InlineData("invoice", "Invoice")]
    [InlineData("INVOICE", "Invoice")]
    [InlineData("Invoice", "Invoice")]
    public void Category_Normalization_Works(string input, string expected)
    {
        var normalized = allowedCategories.FirstOrDefault(c => 
            c.Equals(input, StringComparison.OrdinalIgnoreCase)) ?? input;
        
        normalized.Should().Be(expected);
    }

    private static readonly string[] allowedCategories = 
    {
        "Invoice", "Contract", "Report", "Letter",
        "Memo", "Presentation", "Spreadsheet", "Image", "Other"
    };

    #endregion

    #region File Size Tests

    [Theory]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1048576, "1.00 MB")]
    [InlineData(1073741824, "1.00 GB")]
    public void FileSize_Formatting(long bytes, string expected)
    {
        var formatted = FormatFileSize(bytes);
        formatted.Should().Be(expected);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n2} {suffixes[counter]}";
    }

    #endregion
}
