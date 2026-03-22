using GED.Core.Models;
using GED.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Document = GED.Core.Models.Document;
using DocumentStatus = GED.Core.Models.DocumentStatus;

namespace GED.Infrastructure.Services;

public interface IVersionHistoryService
{
    Task<List<VersionHistoryDto>> GetVersionHistoryAsync(Guid documentId);
    Task<VersionHistoryDto?> GetVersionAsync(Guid documentId, int versionNumber);
    Task<DocumentVersion> CreateVersionAsync(Document doc, string? changedBy, string? reason);
    Task<Document?> RestoreVersionAsync(Guid documentId, int versionNumber, string restoredBy);
}

public class VersionHistoryService : IVersionHistoryService
{
    private readonly GedDbContext _db;
    private readonly ILogger<VersionHistoryService> _logger;

    public VersionHistoryService(GedDbContext db, ILogger<VersionHistoryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<VersionHistoryDto>> GetVersionHistoryAsync(Guid documentId)
    {
        var versions = await _db.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync();

        var latestVersion = versions.FirstOrDefault()?.VersionNumber ?? 0;

        return versions.Select(v => new VersionHistoryDto
        {
            Id = v.Id,
            DocumentId = v.DocumentId,
            VersionNumber = v.VersionNumber,
            Title = v.Title,
            Description = v.Description,
            FileName = v.FileName,
            FileSize = v.FileSize,
            ContentType = v.ContentType,
            Category = v.Category,
            Tags = ParseTags(v.Tags),
            ChangedBy = v.ChangedBy,
            ChangeReason = v.ChangeReason,
            CreatedAt = v.CreatedAt,
            IsCurrentVersion = v.VersionNumber == latestVersion
        }).ToList();
    }

    public async Task<VersionHistoryDto?> GetVersionAsync(Guid documentId, int versionNumber)
    {
        var v = await _db.DocumentVersions
            .FirstOrDefaultAsync(x => x.DocumentId == documentId && x.VersionNumber == versionNumber);

        if (v == null) return null;

        var latestVersion = await _db.DocumentVersions
            .Where(x => x.DocumentId == documentId)
            .MaxAsync(x => (int?)x.VersionNumber) ?? 0;

        return new VersionHistoryDto
        {
            Id = v.Id,
            DocumentId = v.DocumentId,
            VersionNumber = v.VersionNumber,
            Title = v.Title,
            Description = v.Description,
            FileName = v.FileName,
            FileSize = v.FileSize,
            ContentType = v.ContentType,
            Category = v.Category,
            Tags = ParseTags(v.Tags),
            ChangedBy = v.ChangedBy,
            ChangeReason = v.ChangeReason,
            CreatedAt = v.CreatedAt,
            IsCurrentVersion = v.VersionNumber == latestVersion
        };
    }

    public async Task<DocumentVersion> CreateVersionAsync(Document doc, string? changedBy, string? reason)
    {
        var nextVersion = await _db.DocumentVersions
            .Where(v => v.DocumentId == doc.Id)
            .MaxAsync(v => (int?)v.VersionNumber) ?? 0;

        var version = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            VersionNumber = nextVersion + 1,
            Title = doc.Title,
            Description = doc.Description,
            FileName = doc.FileName,
            FileSize = doc.FileSize,
            ContentType = doc.ContentType,
            Category = doc.Category,
            Tags = doc.Tags != null ? string.Join(",", doc.Tags) : null,
            Metadata = doc.Metadata != null
                ? System.Text.Json.JsonSerializer.Serialize(doc.Metadata)
                : null,
            ChangedBy = changedBy,
            ChangeReason = reason,
            CreatedAt = DateTime.UtcNow,
            FilePath = doc.FilePath
        };

        _db.DocumentVersions.Add(version);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Created version {Version} of document {DocId} (reason: {Reason})",
            version.VersionNumber, doc.Id, reason ?? "N/A");

        return version;
    }

    public async Task<Document?> RestoreVersionAsync(Guid documentId, int versionNumber, string restoredBy)
    {
        var version = await _db.DocumentVersions
            .FirstOrDefaultAsync(v => v.DocumentId == documentId && v.VersionNumber == versionNumber);

        if (version == null) return null;

        var current = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId);
        if (current == null) return null;

        // Save current state as a version before restoring
        if (current.Version < version.VersionNumber)
        {
            await CreateVersionAsync(MapToDocument(current), restoredBy, "Pre-restore snapshot");
        }

        // Restore from version
        current.Title = version.Title ?? current.Title;
        current.Description = version.Description ?? current.Description;
        current.Category = version.Category ?? current.Category;
        current.FileName = version.FileName ?? current.FileName;
        current.ContentType = version.ContentType ?? current.ContentType;
        current.ModifiedBy = restoredBy;
        current.ModifiedAt = DateTime.UtcNow;
        current.Version = version.VersionNumber;

        if (!string.IsNullOrEmpty(version.Tags))
            current.Tags = version.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Restored document {DocId} to version {Version} by {User}",
            documentId, versionNumber, restoredBy);

        return MapToDocument(current);
    }

    private static Document MapToDocument(DocumentEntity entity) => new()
    {
        Id = entity.Id,
        Title = entity.Title,
        Description = entity.Description,
        FileName = entity.FileName,
        FilePath = entity.FilePath,
        ContentType = entity.ContentType,
        FileSize = entity.FileSize,
        FileHash = entity.FileHash,
        CreatedAt = entity.CreatedAt,
        DocumentDate = entity.DocumentDate,
        ModifiedAt = entity.ModifiedAt,
        CreatedBy = entity.CreatedBy,
        ModifiedBy = entity.ModifiedBy,
        Status = entity.Status,
        IsOcrProcessed = entity.IsOcrProcessed,
        OcrText = entity.OcrText,
        ExtractedText = entity.ExtractedText,
        Tags = entity.Tags,
        Category = entity.Category,
        Metadata = entity.Metadata,
        Version = entity.Version,
        ParentDocumentId = entity.ParentDocumentId
    };

    private static List<string>? ParseTags(string? tags)
        => string.IsNullOrWhiteSpace(tags)
            ? null
            : tags.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
}
