using GED.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GED.Infrastructure.Data;

/// <summary>
/// Entity Framework Core DbContext for PostgreSQL persistence.
/// Replaces the flat JSON file approach in DocumentService.
/// </summary>
public class GedDbContext : DbContext
{
    public GedDbContext(DbContextOptions<GedDbContext> options) : base(options) { }

    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<DocumentAcl> DocumentAcls { get; set; }
    public DbSet<DocumentMetadataEntity> DocumentMetadata => Set<DocumentMetadataEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── DocumentAcl table ────────────────────────────────────────────────
        modelBuilder.Entity<DocumentAcl>(e => {
            e.ToTable("document_acls");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.DocumentId).HasColumnName("document_id");
            e.Property(a => a.UserId).HasColumnName("user_id");
            e.Property(a => a.Permission).HasColumnName("permission");
            e.Property(a => a.GrantedAt).HasColumnName("granted_at");
            e.Property(a => a.GrantedBy).HasColumnName("granted_by");
            e.Property(a => a.ExpiresAt).HasColumnName("expires_at");  // ← NEW
            e.HasIndex(a => new { a.DocumentId, a.UserId }).HasDatabaseName("ix_acl_doc_user");
        });
    
        // ── Documents table ──────────────────────────────────────────────────
        modelBuilder.Entity<DocumentEntity>(e =>
        {
            e.ToTable("documents");
            e.HasKey(d => d.Id);

            e.Property(d => d.Id).HasColumnName("id");
            e.Property(d => d.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
            e.Property(d => d.Description).HasColumnName("description").HasMaxLength(2000);
            e.Property(d => d.FileName).HasColumnName("file_name").IsRequired();
            e.Property(d => d.FilePath).HasColumnName("file_path").IsRequired();
            e.Property(d => d.ContentType).HasColumnName("content_type").IsRequired();
            e.Property(d => d.FileSize).HasColumnName("file_size");
            e.Property(d => d.FileHash).HasColumnName("file_hash");
            e.Property(d => d.CreatedAt).HasColumnName("created_at");
            e.Property(d => d.DocumentDate).HasColumnName("document_date");
            e.Property(d => d.ModifiedAt).HasColumnName("modified_at");
            e.Property(d => d.CreatedBy).HasColumnName("created_by");
            e.Property(d => d.ModifiedBy).HasColumnName("modified_by");
            e.Property(d => d.Status).HasColumnName("status").HasConversion<string>();
            e.Property(d => d.IsOcrProcessed).HasColumnName("is_ocr_processed");
            e.Property(d => d.OcrText).HasColumnName("ocr_text");
            e.Property(d => d.ExtractedText).HasColumnName("extracted_text");
            e.Property(d => d.Category).HasColumnName("category");
            e.Property(d => d.Version).HasColumnName("version");
            e.Property(d => d.ParentDocumentId).HasColumnName("parent_document_id");

            // Store Tags as a PostgreSQL text array
            e.Property(d => d.Tags)
             .HasColumnName("tags")
             .HasColumnType("text[]");

            // Store Metadata dict as JSONB for flexible querying
            e.Property(d => d.Metadata)
             .HasColumnName("metadata")
             .HasColumnType("jsonb")
             .HasConversion(
                v => v == null
                    ? null
                    : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => v == null
                    ? null
                    : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(v, (System.Text.Json.JsonSerializerOptions?)null)
            );

            // Indexes for common queries
            e.HasIndex(d => d.CreatedAt).HasDatabaseName("ix_documents_created_at");
            e.HasIndex(d => d.DocumentDate).HasDatabaseName("ix_documents_document_date");
            e.HasIndex(d => d.Category).HasDatabaseName("ix_documents_category");
            e.HasIndex(d => d.Status).HasDatabaseName("ix_documents_status");
            e.HasIndex(d => d.ContentType).HasDatabaseName("ix_documents_content_type");

            // Relationship to metadata
            e.HasMany(d => d.DocumentMetadata)
             .WithOne(m => m.Document)
             .HasForeignKey(m => m.DocumentId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── DocumentMetadata table ───────────────────────────────────────────
        modelBuilder.Entity<DocumentMetadataEntity>(e =>
        {
            e.ToTable("document_metadata");
            e.HasKey(m => m.Id);

            e.Property(m => m.Id).HasColumnName("id");
            e.Property(m => m.DocumentId).HasColumnName("document_id");
            e.Property(m => m.Key).HasColumnName("key").HasMaxLength(200).IsRequired();
            e.Property(m => m.Value).HasColumnName("value");
            e.Property(m => m.Type).HasColumnName("type").HasConversion<string>();
            e.Property(m => m.CreatedAt).HasColumnName("created_at");

            e.HasIndex(m => m.DocumentId).HasDatabaseName("ix_document_metadata_document_id");
            e.HasIndex(m => new { m.DocumentId, m.Key }).HasDatabaseName("ix_document_metadata_doc_key");
        });

        // ── OutboxMessages table (for reliable RabbitMQ integration) ─────────
        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_messages");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasColumnName("id");
            e.Property(m => m.Type).HasColumnName("type").HasMaxLength(100).IsRequired();
            e.Property(m => m.Payload).HasColumnName("payload").IsRequired();
            e.Property(m => m.CreatedAt).HasColumnName("created_at");
            e.Property(m => m.ProcessedAt).HasColumnName("processed_at");
            e.Property(m => m.Error).HasColumnName("error");
            e.Property(m => m.RetryCount).HasColumnName("retry_count");
            e.HasIndex(m => m.ProcessedAt).HasDatabaseName("ix_outbox_unprocessed");
        });
    
    
    
    }
}

// ── EF entity classes (separate from domain models to avoid coupling) ────────

public class DocumentEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string? FileHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DocumentDate { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
    public DocumentStatus Status { get; set; }
    public bool IsOcrProcessed { get; set; }
    public string? OcrText { get; set; }
    public string? ExtractedText { get; set; }
    public List<string>? Tags { get; set; }
    public string? Category { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public int Version { get; set; }
    public Guid? ParentDocumentId { get; set; }

    public virtual ICollection<DocumentMetadataEntity> DocumentMetadata { get; set; } = new List<DocumentMetadataEntity>();
}

public class DocumentMetadataEntity
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public MetadataType Type { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual DocumentEntity? Document { get; set; }
}