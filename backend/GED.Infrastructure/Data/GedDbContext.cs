using GED.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GED.Infrastructure.Data;


public class GedDbContext : DbContext
{
    public GedDbContext(DbContextOptions<GedDbContext> options) : base(options) { }

    public DbSet<DocumentEntity>         Documents             => Set<DocumentEntity>();
    public DbSet<OutboxMessage>          OutboxMessages        => Set<OutboxMessage>();
    public DbSet<DocumentAcl>            DocumentAcls          { get; set; }
    public DbSet<DocumentMetadataEntity> DocumentMetadata      => Set<DocumentMetadataEntity>();
    public DbSet<DocumentGroup>          DocumentGroups        { get; set; }
    public DbSet<DocumentGroupMember>    DocumentGroupMembers  { get; set; }
    public DbSet<AppUser> Users { get; set; }
    public DbSet<UserGroupAssignment>    UserGroupAssignments  { get; set; }
    public DbSet<DocumentVersion>        DocumentVersions      { get; set; }
    public DbSet<WebhookDelivery>        WebhookDeliveries     { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── DocumentAcl table ────────────────────────────────────────────────
        modelBuilder.Entity<DocumentAcl>(e => {
            e.ToTable("document_acls");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id").HasColumnType("uniqueidentifier");
            e.Property(a => a.DocumentId).HasColumnName("document_id").HasColumnType("uniqueidentifier");
            e.Property(a => a.UserId).HasColumnName("user_id").HasColumnType("uniqueidentifier");
            e.Property(a => a.Permission).HasColumnName("permission");
            e.Property(a => a.GrantedAt).HasColumnName("granted_at");
            e.Property(a => a.GrantedBy).HasColumnName("granted_by").HasColumnType("uniqueidentifier");
            e.Property(a => a.ExpiresAt).HasColumnName("expires_at");
            e.HasIndex(a => new { a.DocumentId, a.UserId }).HasDatabaseName("ix_acl_doc_user");
        });

        // ── Users table ───────────────────────────────────────────────
        modelBuilder.Entity<AppUser>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).HasColumnName("id");
            e.Property(u => u.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
            e.Property(u => u.PasswordHash).HasColumnName("password_hash").IsRequired();
            e.Property(u => u.FullName).HasColumnName("full_name").HasMaxLength(200);
            e.Property(u => u.Email).HasColumnName("email").HasMaxLength(200);
            e.Property(u => u.Role).HasColumnName("role").HasConversion<string>();
            e.Property(u => u.IsActive).HasColumnName("is_active");
            e.Property(u => u.CreatedAt).HasColumnName("created_at");
            e.Property(u => u.LastLoginAt).HasColumnName("last_login_at");
            e.Property(u => u.AllowedCategories)
            .HasColumnName("allowed_categories")
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null)
            );
            e.HasIndex(u => u.Username).IsUnique().HasDatabaseName("ix_users_username");
        });

        // ── DocumentGroup table ───────────────────────────────────────────────
        modelBuilder.Entity<DocumentGroup>(e =>
        {
            e.ToTable("document_groups");
            e.HasKey(g => g.Id);
            e.Property(g => g.Id).HasColumnName("id");
            e.Property(g => g.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(g => g.Description).HasColumnName("description").HasMaxLength(1000);
            e.Property(g => g.Color).HasColumnName("color").HasMaxLength(20);
            e.Property(g => g.Icon).HasColumnName("icon").HasMaxLength(10);
            e.Property(g => g.Category).HasColumnName("category").HasMaxLength(100);
            e.Property(g => g.CreatedBy).HasColumnName("created_by");
            e.Property(g => g.CreatedAt).HasColumnName("created_at");
            e.Property(g => g.UpdatedAt).HasColumnName("updated_at");
            e.Property(g => g.IsActive).HasColumnName("is_active");
            e.HasIndex(g => g.IsActive).HasDatabaseName("ix_document_groups_active");
        });

        // ── DocumentGroupMember table ─────────────────────────────────────────
        modelBuilder.Entity<DocumentGroupMember>(e =>
        {
            e.ToTable("document_group_members");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasColumnName("id");
            e.Property(m => m.GroupId).HasColumnName("group_id");
            e.Property(m => m.DocumentId).HasColumnName("document_id");
            e.Property(m => m.DefaultPermission).HasColumnName("default_permission");
            e.Property(m => m.AddedAt).HasColumnName("added_at");
            e.Property(m => m.AddedBy).HasColumnName("added_by");
            e.HasIndex(m => new { m.GroupId, m.DocumentId })
             .IsUnique()
             .HasDatabaseName("ix_group_members_group_doc");
        });

        // ── UserGroupAssignment table ─────────────────────────────────────────
        modelBuilder.Entity<UserGroupAssignment>(e =>
        {
            e.ToTable("user_group_assignments");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id").HasColumnType("uniqueidentifier");
            e.Property(a => a.GroupId).HasColumnName("group_id").HasColumnType("uniqueidentifier");
            e.Property(a => a.UserId).HasColumnName("user_id").HasColumnType("uniqueidentifier");
            e.Property(a => a.Permission).HasColumnName("permission");
            e.Property(a => a.AssignedAt).HasColumnName("assigned_at");
            e.Property(a => a.AssignedBy).HasColumnName("assigned_by").HasColumnType("uniqueidentifier");
            e.Property(a => a.ExpiresAt).HasColumnName("expires_at");
            e.HasIndex(a => new { a.GroupId, a.UserId })
             .IsUnique()
             .HasDatabaseName("ix_user_group_assignments_group_user");
            e.HasIndex(a => a.UserId).HasDatabaseName("ix_user_group_assignments_user");
        });

        // ── Documents table ───────────────────────────────────────────────────
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

            // Tags: SQL Server has no native array type.
            // Store as JSON in nvarchar(max) — EF Core serializes/deserializes automatically.
            e.Property(d => d.Tags)
             .HasColumnName("tags")
             .HasColumnType("nvarchar(max)")
             .HasConversion(
                v => v == null
                    ? null
                    : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => v == null
                    ? null
                    : System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null)
             );

            // Metadata: SQL Server has no jsonb type.
            // Store as JSON in nvarchar(max) — same semantics, slightly different storage.

            e.Property(d => d.Metadata)
             .HasColumnName("metadata")
             .HasColumnType("nvarchar(max)")
             .HasConversion(
                v => v == null
                    ? null
                    : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => v == null
                    ? null
                    : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(v, (System.Text.Json.JsonSerializerOptions?)null)
             );

            // Indexes
            e.HasIndex(d => d.CreatedAt).HasDatabaseName("ix_documents_created_at");
            e.HasIndex(d => d.DocumentDate).HasDatabaseName("ix_documents_document_date");
            e.HasIndex(d => d.Category).HasDatabaseName("ix_documents_category");
            e.HasIndex(d => d.Status).HasDatabaseName("ix_documents_status");
            e.HasIndex(d => d.ContentType).HasDatabaseName("ix_documents_content_type");

            e.HasMany(d => d.DocumentMetadata)
             .WithOne(m => m.Document)
             .HasForeignKey(m => m.DocumentId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── DocumentMetadata table ────────────────────────────────────────────
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

        // ── OutboxMessages table ──────────────────────────────────────────────
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

        // ── DocumentVersions table ──────────────────────────────────────────────
        modelBuilder.Entity<DocumentVersion>(e =>
        {
            e.ToTable("document_versions");
            e.HasKey(v => v.Id);
            e.Property(v => v.Id).HasColumnName("id");
            e.Property(v => v.DocumentId).HasColumnName("document_id");
            e.Property(v => v.VersionNumber).HasColumnName("version_number");
            e.Property(v => v.Title).HasColumnName("title").HasMaxLength(500);
            e.Property(v => v.Description).HasColumnName("description").HasMaxLength(2000);
            e.Property(v => v.FileName).HasColumnName("file_name").HasMaxLength(500);
            e.Property(v => v.FileSize).HasColumnName("file_size");
            e.Property(v => v.ContentType).HasColumnName("content_type").HasMaxLength(200);
            e.Property(v => v.Category).HasColumnName("category").HasMaxLength(200);
            e.Property(v => v.Tags).HasColumnName("tags").HasColumnType("nvarchar(max)");
            e.Property(v => v.Metadata).HasColumnName("metadata").HasColumnType("nvarchar(max)");
            e.Property(v => v.ChangedBy).HasColumnName("changed_by").HasMaxLength(500);
            e.Property(v => v.ChangeReason).HasColumnName("change_reason").HasMaxLength(1000);
            e.Property(v => v.CreatedAt).HasColumnName("created_at");
            e.Property(v => v.FilePath).HasColumnName("file_path");
            e.HasIndex(v => v.DocumentId).HasDatabaseName("ix_versions_document_id");
            e.HasIndex(v => new { v.DocumentId, v.VersionNumber })
              .IsUnique()
              .HasDatabaseName("ix_versions_doc_version");
        });

        // ── WebhookDeliveries table ─────────────────────────────────────────────
        modelBuilder.Entity<WebhookDelivery>(e =>
        {
            e.ToTable("webhook_deliveries");
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).HasColumnName("id").HasColumnType("uniqueidentifier");
            e.Property(d => d.WebhookConfigId).HasColumnName("webhook_config_id").HasColumnType("uniqueidentifier");
            e.Property(d => d.Event).HasColumnName("event").HasMaxLength(100).IsRequired();
            e.Property(d => d.Payload).HasColumnName("payload").HasColumnType("nvarchar(max)");
            e.Property(d => d.ResponseStatusCode).HasColumnName("response_status_code");
            e.Property(d => d.ResponseBody).HasColumnName("response_body").HasColumnType("nvarchar(max)");
            e.Property(d => d.AttemptNumber).HasColumnName("attempt_number");
            e.Property(d => d.Succeeded).HasColumnName("succeeded");
            e.Property(d => d.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
            e.Property(d => d.DurationMs).HasColumnName("duration_ms");
            e.Property(d => d.CreatedAt).HasColumnName("created_at");
            e.HasIndex(d => d.WebhookConfigId).HasDatabaseName("ix_webhook_deliveries_config_id");
            e.HasIndex(d => d.CreatedAt).HasDatabaseName("ix_webhook_deliveries_created_at");
            e.HasIndex(d => new { d.Event, d.Succeeded }).HasDatabaseName("ix_webhook_deliveries_event_status");
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

public class WebhookDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? WebhookConfigId { get; set; }
    public string Event { get; set; } = string.Empty;
    public string? Payload { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public int AttemptNumber { get; set; } = 1;
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}