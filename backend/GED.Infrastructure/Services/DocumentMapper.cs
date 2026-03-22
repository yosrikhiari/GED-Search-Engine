using GED.Core.Models;
using GED.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GED.Infrastructure.Services;

/// <summary>
/// Centralized mapper for converting between domain models and database entities.
///
/// <para>
/// This service provides a single location for all document-related mappings,
/// reducing duplication and improving maintainability. It replaces scattered
/// inline mapping logic in controllers and services.
/// </para>
///
/// <example>
/// Usage in controller:
/// <code>
/// var document = await _mapper.ToDomainAsync(documentId);
/// </code>
/// </example>
/// </summary>
public interface IDocumentMapper
{
    Task<Document?> ToDomainAsync(Guid documentId, CancellationToken ct = default);
    Task<Document?> ToDomainAsync(DocumentEntity entity, CancellationToken ct = default);
    Task<List<Document>> ToDomainListAsync(IEnumerable<DocumentEntity> entities, CancellationToken ct = default);
    Task<List<Document>> ToDomainListAsync(IEnumerable<Guid> documentIds, CancellationToken ct = default);
    Document ToDomain(DocumentEntity entity, List<string>? tags = null, Dictionary<string, object>? metadata = null);
    Document ToDomainFromRow(DocumentRow row, List<string>? tags = null, Dictionary<string, object>? metadata = null);
}

public class DocumentMapper : IDocumentMapper
{
    private readonly GedDbContext _db;
    private readonly ILogger<DocumentMapper> _logger;

    public DocumentMapper(GedDbContext db, ILogger<DocumentMapper> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Maps a document entity to domain model by ID.
    /// </summary>
    public async Task<Document?> ToDomainAsync(Guid documentId, CancellationToken ct = default)
    {
        var entity = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (entity == null) return null;

        var tags = await GetTagsAsync(documentId, ct);
        var metadata = await GetMetadataAsync(documentId, ct);

        return ToDomain(entity, tags, metadata);
    }

    /// <summary>
    /// Maps a document entity to domain model.
    /// </summary>
    public async Task<Document?> ToDomainAsync(DocumentEntity entity, CancellationToken ct = default)
    {
        if (entity == null) return null;

        var tags = entity.Tags ?? new List<string>();
        var metadata = entity.Metadata ?? new Dictionary<string, object>();

        return ToDomain(entity, tags, metadata);
    }

    /// <summary>
    /// Maps multiple document entities to domain models.
    /// </summary>
    public async Task<List<Document>> ToDomainListAsync(
        IEnumerable<DocumentEntity> entities,
        CancellationToken ct = default)
    {
        var result = new List<Document>();

        foreach (var entity in entities)
        {
            var tags = entity.Tags ?? new List<string>();
            var metadata = entity.Metadata ?? new Dictionary<string, object>();
            result.Add(ToDomain(entity, tags, metadata));
        }

        return await Task.FromResult(result);
    }

    /// <summary>
    /// Maps multiple document IDs to domain models.
    /// </summary>
    public async Task<List<Document>> ToDomainListAsync(
        IEnumerable<Guid> documentIds,
        CancellationToken ct = default)
    {
        var ids = documentIds.ToList();
        
        if (!ids.Any()) return new List<Document>();

        var entities = await _db.Documents
            .Where(d => ids.Contains(d.Id))
            .ToListAsync(ct);

        var results = new List<Document>();

        foreach (var id in ids)
        {
            var entity = entities.FirstOrDefault(e => e.Id == id);
            if (entity != null)
            {
                var tags = await GetTagsAsync(id, ct);
                var metadata = await GetMetadataAsync(id, ct);
                results.Add(ToDomain(entity, tags, metadata));
            }
        }

        return results;
    }

    /// <summary>
    /// Core mapping logic from entity to domain model.
    /// </summary>
    public Document ToDomain(DocumentEntity entity, List<string>? tags = null, Dictionary<string, object>? metadata = null)
    {
        return new Document
        {
            Id = entity.Id,
            Title = entity.Title,
            Description = entity.Description,
            FileName = entity.FileName,
            FilePath = entity.FilePath,
            ContentType = entity.ContentType,
            FileSize = entity.FileSize,
            CreatedAt = entity.CreatedAt,
            DocumentDate = entity.DocumentDate,
            ModifiedAt = entity.ModifiedAt,
            Status = entity.Status,
            OcrText = entity.OcrText,
            ExtractedText = entity.ExtractedText,
            Tags = tags ?? entity.Tags ?? new List<string>(),
            Category = entity.Category,
            Metadata = metadata ?? entity.Metadata ?? new Dictionary<string, object>(),
            IsOcrProcessed = entity.IsOcrProcessed,
            CreatedBy = entity.CreatedBy
        };
    }

    /// <summary>
    /// Maps a database row (from raw SQL) to domain model.
    /// Used in controllers that fetch via raw SQL.
    /// </summary>
    public Document ToDomainFromRow(DocumentRow row, List<string>? tags = null, Dictionary<string, object>? metadata = null)
    {
        return new Document
        {
            Id = row.Id,
            Title = row.Title ?? string.Empty,
            Description = row.Description,
            FileName = row.FileName ?? string.Empty,
            FilePath = row.FilePath ?? string.Empty,
            ContentType = row.ContentType ?? string.Empty,
            FileSize = row.FileSize,
            CreatedAt = row.CreatedAt,
            DocumentDate = row.DocumentDate,
            ModifiedAt = row.ModifiedAt,
            Status = Enum.TryParse<DocumentStatus>(row.Status, out var s) ? s : DocumentStatus.Indexed,
            OcrText = row.OcrText,
            ExtractedText = row.ExtractedText,
            Tags = tags ?? new List<string>(),
            Category = row.Category,
            Metadata = metadata ?? new Dictionary<string, object>(),
            IsOcrProcessed = row.IsOcrProcessed
        };
    }

    private async Task<List<string>> GetTagsAsync(Guid documentId, CancellationToken ct)
    {
        try
        {
            var tagsJson = await _db.Database
                .SqlQueryRaw<string>("SELECT tags FROM documents WHERE id = {0}", documentId)
                .FirstOrDefaultAsync(ct);

            if (string.IsNullOrEmpty(tagsJson)) return new List<string>();

            return JsonSerializer.Deserialize<List<string>>(tagsJson) ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize tags for document {Id}", documentId);
            return new List<string>();
        }
    }

    private async Task<Dictionary<string, object>> GetMetadataAsync(Guid documentId, CancellationToken ct)
    {
        try
        {
            var metadataJson = await _db.Database
                .SqlQueryRaw<string>("SELECT metadata FROM documents WHERE id = {0}", documentId)
                .FirstOrDefaultAsync(ct);

            if (string.IsNullOrEmpty(metadataJson)) return new Dictionary<string, object>();

            return JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson)
                   ?? new Dictionary<string, object>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize metadata for document {Id}", documentId);
            return new Dictionary<string, object>();
        }
    }
}

/// <summary>
/// Extension methods for document mapping utilities.
/// </summary>
public static class DocumentMapperExtensions
{
    /// <summary>
    /// Safely deserializes a JSON string to a list of strings.
    /// </summary>
    public static List<string> DeserializeTagsOrDefault(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Safely deserializes a JSON string to a dictionary.
    /// </summary>
    public static Dictionary<string, object> DeserializeMetadataOrDefault(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                   ?? new Dictionary<string, object>();
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }
}

/// <summary>
/// Database row model for raw SQL queries in controllers.
/// This avoids the need for EF Core JSON column handling issues.
/// </summary>
public class DocumentRow
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    public string? ContentType { get; set; }
    public long FileSize { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DocumentDate { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public string? Status { get; set; }
    public bool IsOcrProcessed { get; set; }
    public string? OcrText { get; set; }
    public string? ExtractedText { get; set; }
    public string? Category { get; set; }
}
