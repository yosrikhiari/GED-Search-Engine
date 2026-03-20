using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GED.Core.Models;

[Table("document_versions")]
public class DocumentVersion
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid DocumentId { get; set; }

    public int VersionNumber { get; set; } = 1;

    [MaxLength(500)]
    public string? Title { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? FileName { get; set; }

    public long? FileSize { get; set; }

    [MaxLength(200)]
    public string? ContentType { get; set; }

    [MaxLength(200)]
    public string? Category { get; set; }

    public string? Tags { get; set; }

    public string? Metadata { get; set; }

    [MaxLength(500)]
    public string? ChangedBy { get; set; }

    [MaxLength(1000)]
    public string? ChangeReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? FilePath { get; set; }
}

public class VersionHistoryDto
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int VersionNumber { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? ContentType { get; set; }
    public string? Category { get; set; }
    public List<string>? Tags { get; set; }
    public string? ChangedBy { get; set; }
    public string? ChangeReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsCurrentVersion { get; set; }
}
