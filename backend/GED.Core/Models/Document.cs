using System.ComponentModel.DataAnnotations;

namespace GED.Core.Models;

public class Document
{
    public Guid Id { get; set; }
    
    [Required]
    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;
    
    [MaxLength(2000)]
    public string? Description { get; set; }
    
    [Required]
    public string FileName { get; set; } = string.Empty;
    
    [Required]
    public string FilePath { get; set; } = string.Empty;
    
    [Required]
    public string ContentType { get; set; } = string.Empty;
    
    public long FileSize { get; set; }
    
    public string? FileHash { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime? ModifiedAt { get; set; }
    
    public string? CreatedBy { get; set; }
    
    public string? ModifiedBy { get; set; }
    
    public DocumentStatus Status { get; set; }
    
    public bool IsOcrProcessed { get; set; }
    
    public string? OcrText { get; set; }
    
    public string? ExtractedText { get; set; }
    
    public Dictionary<string, object>? Metadata { get; set; }
    
    public List<string>? Tags { get; set; }
    
    public string? Category { get; set; }
    
    public int Version { get; set; }
    
    public Guid? ParentDocumentId { get; set; }
    
    // Navigation properties
    public virtual ICollection<DocumentMetadata>? DocumentMetadata { get; set; }
}

public enum DocumentStatus
{
    Pending,
    Processing,
    Indexed,
    Failed,
    Deleted
}

public class DocumentMetadata
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    
    [Required]
    [MaxLength(200)]
    public string Key { get; set; } = string.Empty;
    
    public string? Value { get; set; }
    
    public MetadataType Type { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    // Navigation
    public virtual Document? Document { get; set; }
}

public enum MetadataType
{
    String,
    Number,
    Date,
    Boolean,
    Json
}
