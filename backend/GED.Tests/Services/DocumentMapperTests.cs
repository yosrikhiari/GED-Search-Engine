using GED.Core.Interfaces;
using GED.Core.Models;
using GED.Infrastructure.Data;
using GED.Infrastructure.Services;
using Xunit;
using FluentAssertions;

namespace GED.Tests.Services;

[Trait("Category", "Unit")]
public class DocumentMapperTests
{
    private readonly DocumentMapper _mapper;

    public DocumentMapperTests()
    {
        _mapper = new DocumentMapper(null!, null!);
    }

    [Fact]
    public void ToDomain_WithValidEntity_MapsAllFields()
    {
        var entity = CreateTestEntity();
        var tags = new List<string> { "tag1", "tag2" };
        var metadata = new Dictionary<string, object> { { "key", "value" } };

        var result = _mapper.ToDomain(entity, tags, metadata);

        result.Should().NotBeNull();
        result.Id.Should().Be(entity.Id);
        result.Title.Should().Be(entity.Title);
        result.Description.Should().Be(entity.Description);
        result.FileName.Should().Be(entity.FileName);
        result.FilePath.Should().Be(entity.FilePath);
        result.ContentType.Should().Be(entity.ContentType);
        result.FileSize.Should().Be(entity.FileSize);
        result.CreatedAt.Should().Be(entity.CreatedAt);
        result.DocumentDate.Should().Be(entity.DocumentDate);
        result.ModifiedAt.Should().Be(entity.ModifiedAt);
        result.Status.Should().Be(entity.Status);
        result.OcrText.Should().Be(entity.OcrText);
        result.ExtractedText.Should().Be(entity.ExtractedText);
        result.Tags.Should().BeEquivalentTo(tags);
        result.Category.Should().Be(entity.Category);
        result.Metadata.Should().BeEquivalentTo(metadata);
        result.IsOcrProcessed.Should().Be(entity.IsOcrProcessed);
        result.CreatedBy.Should().Be(entity.CreatedBy);
    }

    [Fact]
    public void ToDomain_WithNullTags_UsesProvidedTags()
    {
        var entity = CreateTestEntity();
        var tags = new List<string> { "custom", "tags" };

        var result = _mapper.ToDomain(entity, tags, null);

        result.Tags.Should().BeEquivalentTo(tags);
    }

    [Fact]
    public void ToDomain_WithNullMetadata_UsesProvidedMetadata()
    {
        var entity = CreateTestEntity();
        var metadata = new Dictionary<string, object> { { "custom", "meta" } };

        var result = _mapper.ToDomain(entity, null, metadata);

        result.Metadata.Should().BeEquivalentTo(metadata);
    }

    [Fact]
    public void ToDomainFromRow_WithValidRow_MapsAllFields()
    {
        var row = CreateTestRow();
        var tags = new List<string> { "row", "tags" };
        var metadata = new Dictionary<string, object> { { "row", "data" } };

        var result = _mapper.ToDomainFromRow(row, tags, metadata);

        result.Should().NotBeNull();
        result.Id.Should().Be(row.Id);
        result.Title.Should().Be(row.Title);
        result.Description.Should().Be(row.Description);
        result.FileName.Should().Be(row.FileName);
        result.FilePath.Should().Be(row.FilePath);
        result.ContentType.Should().Be(row.ContentType);
        result.FileSize.Should().Be(row.FileSize);
        result.CreatedAt.Should().Be(row.CreatedAt);
        result.DocumentDate.Should().Be(row.DocumentDate);
        result.ModifiedAt.Should().Be(row.ModifiedAt);
        result.Status.Should().Be(DocumentStatus.Indexed);
        result.OcrText.Should().Be(row.OcrText);
        result.ExtractedText.Should().Be(row.ExtractedText);
        result.Tags.Should().BeEquivalentTo(tags);
        result.Category.Should().Be(row.Category);
        result.Metadata.Should().BeEquivalentTo(metadata);
        result.IsOcrProcessed.Should().Be(row.IsOcrProcessed);
    }

    [Fact]
    public void ToDomainFromRow_WithInvalidStatus_DefaultsToIndexed()
    {
        var row = new DocumentRow
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            Status = "InvalidStatusThatDoesNotExist"
        };

        var result = _mapper.ToDomainFromRow(row);

        result.Status.Should().Be(DocumentStatus.Indexed);
    }

    [Fact]
    public void ToDomainFromRow_WithNullOptionalFields_HandlesGracefully()
    {
        var row = new DocumentRow
        {
            Id = Guid.NewGuid(),
            Title = null,
            Description = null,
            FileName = null,
            Status = "Indexed"
        };

        var result = _mapper.ToDomainFromRow(row);

        result.Should().NotBeNull();
        result.Id.Should().Be(row.Id);
        result.Title.Should().BeEmpty();
        result.Description.Should().BeNull();
    }

    [Fact]
    public void ToDomainFromRow_WithoutTagsParameter_UsesEmptyList()
    {
        var row = CreateTestRow();

        var result = _mapper.ToDomainFromRow(row);

        result.Tags.Should().NotBeNull();
        result.Tags.Should().BeEmpty();
    }

    [Fact]
    public void ToDomainFromRow_WithoutMetadataParameter_UsesEmptyDictionary()
    {
        var row = CreateTestRow();

        var result = _mapper.ToDomainFromRow(row);

        result.Metadata.Should().NotBeNull();
        result.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void ToDomain_WithAllStatusValues_MapsCorrectly()
    {
        var statuses = new[] { DocumentStatus.Pending, DocumentStatus.Processing,
            DocumentStatus.Indexed, DocumentStatus.Failed, DocumentStatus.Deleted, DocumentStatus.Expired };

        foreach (var status in statuses)
        {
            var entity = CreateTestEntity();
            entity.Status = status;

            var result = _mapper.ToDomain(entity);

            result.Status.Should().Be(status, $"Status {status} should map correctly");
        }
    }

    private static DocumentEntity CreateTestEntity()
    {
        return new DocumentEntity
        {
            Id = Guid.NewGuid(),
            Title = "Test Document",
            Description = "Test Description",
            FileName = "test.pdf",
            FilePath = "/path/to/test.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            DocumentDate = DateTime.UtcNow.AddDays(-1),
            ModifiedAt = DateTime.UtcNow,
            Status = DocumentStatus.Indexed,
            OcrText = "OCR text content",
            ExtractedText = "Extracted text content",
            Tags = new List<string> { "test" },
            Category = "Invoice",
            Metadata = new Dictionary<string, object> { { "test", "value" } },
            IsOcrProcessed = true,
            CreatedBy = Guid.NewGuid().ToString()
        };
    }

    private static DocumentRow CreateTestRow()
    {
        return new DocumentRow
        {
            Id = Guid.NewGuid(),
            Title = "Test Row Document",
            Description = "Test Row Description",
            FileName = "row.pdf",
            FilePath = "/path/to/row.pdf",
            ContentType = "application/pdf",
            FileSize = 2048,
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            DocumentDate = DateTime.UtcNow.AddDays(-1),
            ModifiedAt = DateTime.UtcNow,
            Status = "Indexed",
            IsOcrProcessed = true,
            OcrText = "Row OCR text",
            ExtractedText = "Row extracted text",
            Category = "Contract"
        };
    }
}

[Trait("Category", "Unit")]
public class DocumentMapperExtensionsTests
{
    [Fact]
    public void DeserializeTagsOrDefault_WithValidJson_ReturnsTags()
    {
        var json = "[\"tag1\", \"tag2\", \"tag3\"]";

        var result = DocumentMapperExtensions.DeserializeTagsOrDefault(json);

        result.Should().BeEquivalentTo(new List<string> { "tag1", "tag2", "tag3" });
    }

    [Fact]
    public void DeserializeTagsOrDefault_WithNullOrEmpty_ReturnsEmptyList()
    {
        DocumentMapperExtensions.DeserializeTagsOrDefault(null).Should().BeEmpty();
        DocumentMapperExtensions.DeserializeTagsOrDefault("").Should().BeEmpty();
        DocumentMapperExtensions.DeserializeTagsOrDefault("   ").Should().BeEmpty();
    }

    [Fact]
    public void DeserializeTagsOrDefault_WithInvalidJson_ReturnsEmptyList()
    {
        var invalidJson = "{ invalid json }";

        var result = DocumentMapperExtensions.DeserializeTagsOrDefault(invalidJson);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DeserializeTagsOrDefault_WithEmptyArrayJson_ReturnsEmptyList()
    {
        var json = "[]";

        var result = DocumentMapperExtensions.DeserializeTagsOrDefault(json);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DeserializeMetadataOrDefault_WithValidJson_ReturnsMetadata()
    {
        var json = "{\"key1\": \"value1\", \"key2\": 123}";

        var result = DocumentMapperExtensions.DeserializeMetadataOrDefault(json);

        result.Should().ContainKey("key1");
        result.Should().ContainKey("key2");
    }

    [Fact]
    public void DeserializeMetadataOrDefault_WithNullOrEmpty_ReturnsEmptyDictionary()
    {
        DocumentMapperExtensions.DeserializeMetadataOrDefault(null).Should().BeEmpty();
        DocumentMapperExtensions.DeserializeMetadataOrDefault("").Should().BeEmpty();
    }

    [Fact]
    public void DeserializeMetadataOrDefault_WithInvalidJson_ReturnsEmptyDictionary()
    {
        var invalidJson = "[invalid json]";

        var result = DocumentMapperExtensions.DeserializeMetadataOrDefault(invalidJson);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DeserializeMetadataOrDefault_WithEmptyObjectJson_ReturnsEmptyDictionary()
    {
        var json = "{}";

        var result = DocumentMapperExtensions.DeserializeMetadataOrDefault(json);

        result.Should().BeEmpty();
    }
}

[Trait("Category", "Unit")]
public class IndexingJobMessageTests
{
    [Fact]
    public void IndexingJobMessage_DefaultValues_AreCorrect()
    {
        var message = new IndexingJobMessage();

        message.JobId.Should().NotBeEmpty();
        message.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        message.Metadata.Should().BeNull();
    }

    [Fact]
    public void IndexingJobMessage_WithValues_SetsCorrectly()
    {
        var docId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow.AddMinutes(-5);

        var message = new IndexingJobMessage
        {
            JobId = jobId,
            DocumentId = docId,
            Action = IndexingAction.Index,
            CreatedAt = createdAt,
            Metadata = new Dictionary<string, object> { { "source", "test" } }
        };

        message.JobId.Should().Be(jobId);
        message.DocumentId.Should().Be(docId);
        message.Action.Should().Be(IndexingAction.Index);
        message.CreatedAt.Should().Be(createdAt);
        message.Metadata.Should().ContainKey("source");
    }

    [Fact]
    public void IndexingAction_EnumValues_AreCorrect()
    {
        Enum.GetValues<IndexingAction>().Should().Contain(IndexingAction.Index);
        Enum.GetValues<IndexingAction>().Should().Contain(IndexingAction.Reindex);
        Enum.GetValues<IndexingAction>().Should().Contain(IndexingAction.Delete);
        Enum.GetValues<IndexingAction>().Should().Contain(IndexingAction.UpdateAcl);
    }
}
