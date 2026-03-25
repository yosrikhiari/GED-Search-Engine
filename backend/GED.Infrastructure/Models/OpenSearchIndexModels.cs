namespace GED.Infrastructure.Models;

/// <summary>
/// OpenSearch index document model for ged-documents index.
/// </summary>
public class DocumentIndexModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DocumentDate { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public string Status { get; set; } = "Indexed";
    public bool IsOcrProcessed { get; set; }
    public bool IsFullyProcessed { get; set; }
    public string? ExtractedText { get; set; }
    public string? OcrText { get; set; }
    public List<string>? Tags { get; set; }
    public string? Category { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public float[]? Embedding { get; set; }

    // ACL fields
    public string? AccessLevel { get; set; }
    public List<string>? AllowedUserIds { get; set; }
    public string? CreatedByUserId { get; set; }
}

/// <summary>
/// OpenSearch index model for ged-chunks index.
/// </summary>
public class ChunkIndexModel
{
    public Guid DocumentId { get; set; }
    public string ChunkId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Category { get; set; }
    public DateTime? DocumentDate { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public List<string>? Tags { get; set; }
    public float[]? Embedding { get; set; }

    // ACL fields
    public string? AccessLevel { get; set; }
    public List<string>? AllowedUserIds { get; set; }
    public string? CreatedByUserId { get; set; }
}
