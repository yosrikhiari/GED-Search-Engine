using FluentAssertions;
using GED.API.Controllers;

namespace GED.Tests.Models;

public class TaxonomyModelTests
{
    [Fact]
    public void TaxonomyCategory_DefaultValues_AreCorrect()
    {
        var category = new TaxonomyCategory();

        category.Id.Should().Be(Guid.Empty);
        category.Name.Should().BeEmpty();
        category.Description.Should().BeNull();
        category.Icon.Should().Be("📁");
        category.Color.Should().Be("#6366f1");
        category.SortOrder.Should().Be(0);
        category.IsActive.Should().BeTrue();
        category.IsSystem.Should().BeFalse();
        category.CreatedAt.Should().Be(default);
        category.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void TaxonomyCategory_WithFullData_SetsAllProperties()
    {
        var now = DateTime.UtcNow;
        var category = new TaxonomyCategory
        {
            Id = Guid.NewGuid(),
            Name = "Invoice",
            Description = "Factures",
            Icon = "📄",
            Color = "#3b82f6",
            SortOrder = 1,
            IsActive = true,
            IsSystem = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        category.Name.Should().Be("Invoice");
        category.IsSystem.Should().BeTrue();
    }

    [Fact]
    public void TaxonomyTag_DefaultValues_AreCorrect()
    {
        var tag = new TaxonomyTag();

        tag.Id.Should().Be(Guid.Empty);
        tag.Name.Should().BeEmpty();
        tag.Description.Should().BeNull();
        tag.Category.Should().BeNull();
        tag.Color.Should().Be("#64748b");
        tag.UsageCount.Should().Be(0);
        tag.IsSystem.Should().BeFalse();
        tag.IsActive.Should().BeTrue();
    }

    [Fact]
    public void TaxonomyTag_WithFullData_SetsAllProperties()
    {
        var tag = new TaxonomyTag
        {
            Id = Guid.NewGuid(),
            Name = "urgent",
            Description = "Urgent document",
            Category = "Invoice",
            Color = "#ef4444",
            UsageCount = 10,
            IsSystem = true,
            IsActive = true
        };

        tag.Name.Should().Be("urgent");
        tag.UsageCount.Should().Be(10);
    }

    [Fact]
    public void TaxonomyTagList_DefaultValues_AreCorrect()
    {
        var list = new TaxonomyTagList();

        list.Tags.Should().BeEmpty();
        list.Total.Should().Be(0);
        list.Page.Should().Be(0);
        list.PageSize.Should().Be(0);
        list.TotalPages.Should().Be(0);
    }

    [Fact]
    public void TaxonomyTagList_WithPaging_SetsProperties()
    {
        var list = new TaxonomyTagList
        {
            Tags = new List<TaxonomyTag> { new() { Name = "tag1" } },
            Total = 100,
            Page = 2,
            PageSize = 10,
            TotalPages = 10
        };

        list.Total.Should().Be(100);
        list.Page.Should().Be(2);
        list.TotalPages.Should().Be(10);
    }

    [Fact]
    public void TaxonomyData_DefaultValues_AreCorrect()
    {
        var data = new TaxonomyData();

        data.Categories.Should().BeEmpty();
        data.Tags.Should().BeEmpty();
        data.UpdatedAt.Should().Be(default);
    }

    [Fact]
    public void CreateCategoryRequest_WithData_SetsProperties()
    {
        var request = new CreateCategoryRequest
        {
            Name = "Invoice",
            Description = "Factures",
            Icon = "📄",
            Color = "#3b82f6"
        };

        request.Name.Should().Be("Invoice");
        request.Description.Should().Be("Factures");
    }

    [Fact]
    public void UpdateCategoryRequest_WithData_SetsProperties()
    {
        var request = new UpdateCategoryRequest
        {
            Name = "Updated Invoice",
            Description = "Updated description",
            SortOrder = 5,
            IsActive = false
        };

        request.Name.Should().Be("Updated Invoice");
        request.SortOrder.Should().Be(5);
    }

    [Fact]
    public void CreateTagRequest_WithData_SetsProperties()
    {
        var request = new CreateTagRequest
        {
            Name = "urgent",
            Description = "Urgent",
            Category = "Invoice",
            Color = "#ef4444"
        };

        request.Name.Should().Be("urgent");
    }

    [Fact]
    public void UpdateTagRequest_WithData_SetsProperties()
    {
        var request = new UpdateTagRequest
        {
            Name = "new-name",
            IsActive = false
        };

        request.Name.Should().Be("new-name");
    }

    [Fact]
    public void CreateBulkTagsRequest_WithData_SetsProperties()
    {
        var request = new CreateBulkTagsRequest
        {
            Names = new List<string> { "tag1", "tag2", "tag3" },
            Description = "Bulk tags",
            Category = "Invoice"
        };

        request.Names.Should().HaveCount(3);
        request.Names.Should().Contain("tag1");
    }

    [Fact]
    public void TrackTagUsageRequest_WithData_SetsProperties()
    {
        var request = new TrackTagUsageRequest
        {
            TagNames = new List<string> { "urgent", "reviewed" }
        };

        request.TagNames.Should().HaveCount(2);
    }
}