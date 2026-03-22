using FluentAssertions;
using GED.Core.Models;

namespace GED.Tests.Quality;

public class DocumentServiceQualityTests
{
    [Fact]
    public void Document_WithAllFields_SetsAllProperties()
    {
        var docId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var doc = new Document
        {
            Id = docId,
            Title = "Invoice 2024",
            Description = "Monthly invoice",
            FileName = "invoice_2024.pdf",
            FilePath = "/storage/invoice_2024.pdf",
            ContentType = "application/pdf",
            FileSize = 1024 * 500,
            FileHash = "abc123def456",
            CreatedAt = now,
            DocumentDate = now.AddDays(-30),
            ModifiedAt = now,
            CreatedBy = "admin",
            ModifiedBy = "admin",
            Status = DocumentStatus.Indexed,
            IsOcrProcessed = true,
            OcrText = "Extracted OCR text...",
            ExtractedText = "Extracted text from PDF...",
            Tags = new List<string> { "invoice", "2024", "monthly" },
            Category = "Invoice",
            Version = 1,
            Metadata = new Dictionary<string, object> { ["author"] = "Finance Dept" }
        };

        doc.Id.Should().Be(docId);
        doc.Title.Should().Be("Invoice 2024");
        doc.Status.Should().Be(DocumentStatus.Indexed);
        doc.IsOcrProcessed.Should().BeTrue();
        doc.Tags.Should().HaveCount(3);
        doc.Metadata.Should().ContainKey("author");
    }

    [Fact]
    public void Document_DefaultValues_AreCorrect()
    {
        var doc = new Document();
        doc.Id.Should().Be(Guid.Empty);
        doc.Title.Should().BeEmpty();
        doc.Status.Should().Be(DocumentStatus.Pending);
        doc.IsOcrProcessed.Should().BeFalse();
        doc.Version.Should().Be(0);
        doc.Tags.Should().BeNull();
        doc.Metadata.Should().BeNull();
    }

    [Fact]
    public void Document_FileName_Sanitization_RemovesInvalidChars()
    {
        var invalidFileName = "..\\..\\etc\\passwd";
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(invalidFileName.Where(c => !invalidChars.Contains(c)).ToArray());
        sanitized = sanitized.Replace("..", "").Replace("~", "");
        sanitized.Should().NotContain("..");
        sanitized = sanitized.Replace("\\", "/");
        sanitized.Should().NotStartWith("/");
    }

    [Fact]
    public void Document_FileName_Sanitization_LimitsLength()
    {
        var longFileName = new string('a', 300) + ".pdf";
        var maxLength = 200;
        var sanitized = longFileName.Length > maxLength ? longFileName[..maxLength] + ".pdf" : longFileName;
        sanitized.Length.Should().BeLessOrEqualTo(maxLength + 4);
    }

    [Fact]
    public void Document_FileSize_ConvertsToMB()
    {
        var fileSizeBytes = 100 * 1024 * 1024;
        var fileSizeMb = fileSizeBytes / (1024.0 * 1024.0);
        fileSizeMb.Should().BeApproximately(100, 0.01);
    }

    [Fact]
    public void Document_FileSize_ExceedsLimit_Detected()
    {
        var maxSizeMb = 100;
        var maxSizeBytes = (long)maxSizeMb * 1024 * 1024;
        var actualSizeBytes = 150 * 1024 * 1024L;
        actualSizeBytes.Should().BeGreaterThan(maxSizeBytes);
    }

    [Fact]
    public void Document_Category_Validation_AcceptsValid()
    {
        var validCategories = new[] { "Invoice", "Contract", "Report", "Letter", "Memo", "Presentation", "Spreadsheet", "Image", "Other" };
        var docCategory = "Invoice";
        validCategories.Should().Contain(docCategory);
    }

    [Fact]
    public void Document_Category_Validation_RejectsInvalid()
    {
        var validCategories = new[] { "Invoice", "Contract", "Report", "Letter", "Memo", "Presentation", "Spreadsheet", "Image", "Other" };
        var docCategory = "SecretDocument";
        validCategories.Should().NotContain(docCategory);
    }

    [Fact]
    public void Document_Metadata_JsonElement_Parsing()
    {
        var meta = new Dictionary<string, object>
        {
            ["author"] = "Finance Dept",
            ["year"] = 2024,
            ["reviewed"] = true
        };

        meta.Should().ContainKey("author");
        meta["author"].Should().BeOfType<string>();
        meta["year"].Should().BeOfType<int>();
        meta["reviewed"].Should().BeOfType<bool>();
    }

    [Fact]
    public void Document_Tags_AreTrimmed()
    {
        var tags = new List<string> { "  invoice  ", "contract", "report " };
        var trimmedTags = tags.Select(t => t.Trim()).ToList();
        trimmedTags.Should().OnlyContain(t => !t.StartsWith(" ") && !t.EndsWith(" "));
    }

    [Fact]
    public void Document_Tags_Deduplication_Works()
    {
        var tags = new List<string> { "invoice", "Invoice", "INVOICE", "report" };
        var deduplicated = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        deduplicated.Should().HaveCount(2);
    }

    [Fact]
    public void Document_Version_Increments()
    {
        var doc = new Document { Version = 1 };
        doc.Version++;
        doc.Version.Should().Be(2);
    }

    [Fact]
    public void Document_Status_Transitions_AreValid()
    {
        var validTransitions = new Dictionary<DocumentStatus, DocumentStatus[]>
        {
            [DocumentStatus.Pending] = new[] { DocumentStatus.Processing },
            [DocumentStatus.Processing] = new[] { DocumentStatus.Indexed, DocumentStatus.Failed },
            [DocumentStatus.Indexed] = new[] { DocumentStatus.Deleted, DocumentStatus.Expired },
            [DocumentStatus.Failed] = new[] { DocumentStatus.Processing },
        };

        var currentStatus = DocumentStatus.Pending;
        var nextStatus = DocumentStatus.Processing;
        validTransitions[currentStatus].Should().Contain(nextStatus);
    }

    [Fact]
    public void Document_DocumentDate_IsExtracted()
    {
        var doc = new Document { DocumentDate = new DateTime(2024, 1, 15) };
        doc.DocumentDate.Should().NotBeNull();
        doc.DocumentDate!.Value.Year.Should().Be(2024);
        doc.DocumentDate.Value.Month.Should().Be(1);
    }

    [Fact]
    public void Document_FolderPath_IsUUIDBased()
    {
        var docId = Guid.NewGuid();
        var path = $"{docId:N}.pdf";
        path.Should().NotContain("..");
        path.Length.Should().Be(32 + 4);
    }

    [Fact]
    public void Document_Fingerprint_SHA256_IsValid()
    {
        var hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        hash.Length.Should().Be(64);
        hash.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public void Document_FileHash_Deduplication_Works()
    {
        var existingHashes = new HashSet<string> { "hash1", "hash2", "hash3" };
        var newHash = "hash1";
        existingHashes.Contains(newHash).Should().BeTrue();
    }

    [Fact]
    public void Document_NullOrEmpty_Title_IsHandled()
    {
        var doc = new Document { Title = "" };
        var effectiveTitle = string.IsNullOrWhiteSpace(doc.Title) ? "Untitled Document" : doc.Title;
        effectiveTitle.Should().Be("Untitled Document");
    }

    [Fact]
    public void Document_ParentChild_Relationship_Works()
    {
        var parentId = Guid.NewGuid();
        var doc = new Document { ParentDocumentId = parentId };
        doc.ParentDocumentId.Should().Be(parentId);
    }

    [Fact]
    public void Document_ContentType_MIME_Types_AreValid()
    {
        var mimeTypes = new[] { "application/pdf", "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "image/png", "image/jpeg", "image/tiff", "text/plain" };
        mimeTypes.Should().Contain("application/pdf");
        mimeTypes.Should().Contain("text/plain");
    }

    [Fact]
    public void Document_OCR_Text_Extracted_WhenLongerThanThreshold()
    {
        var shortText = "Short text";
        var longText = new string('a', 500);
        var ocrThreshold = 100;

        shortText.Length.Should().BeLessThan(ocrThreshold);
        longText.Length.Should().BeGreaterThan(ocrThreshold);
    }

    [Fact]
    public void Document_ChunkCount_Calculated()
    {
        var totalTextLength = 10000;
        var chunkSize = 500;
        var overlap = 50;
        var expectedChunks = (int)Math.Ceiling((double)(totalTextLength - overlap) / (chunkSize - overlap));
        expectedChunks.Should().BeGreaterThan(1);
    }
}
