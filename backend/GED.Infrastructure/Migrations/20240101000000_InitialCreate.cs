using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GED.Infrastructure.Migrations;

/// <summary>
/// Initial migration — creates the documents and document_metadata tables.
/// Run with: dotnet ef migrations add InitialCreate --project GED.Infrastructure --startup-project GED.API
/// Apply with: dotnet ef database update (or auto-applied on startup via db.Database.MigrateAsync())
/// </summary>
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── documents ────────────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "documents",
            columns: table => new
            {
                id                  = table.Column<Guid>(nullable: false),
                title               = table.Column<string>(maxLength: 500, nullable: false),
                description         = table.Column<string>(maxLength: 2000, nullable: true),
                file_name           = table.Column<string>(nullable: false),
                file_path           = table.Column<string>(nullable: false),
                content_type        = table.Column<string>(nullable: false),
                file_size           = table.Column<long>(nullable: false),
                file_hash           = table.Column<string>(nullable: true),
                created_at          = table.Column<DateTime>(nullable: false),
                document_date       = table.Column<DateTime>(nullable: true),
                modified_at         = table.Column<DateTime>(nullable: true),
                created_by          = table.Column<string>(nullable: true),
                modified_by         = table.Column<string>(nullable: true),
                status              = table.Column<string>(nullable: false),
                is_ocr_processed    = table.Column<bool>(nullable: false),
                ocr_text            = table.Column<string>(nullable: true),
                extracted_text      = table.Column<string>(nullable: true),
                tags                = table.Column<string[]>(type: "text[]", nullable: true),
                category            = table.Column<string>(nullable: true),
                metadata            = table.Column<string>(type: "jsonb", nullable: true),
                version             = table.Column<int>(nullable: false),
                parent_document_id  = table.Column<Guid>(nullable: true)
            },
            constraints: table => table.PrimaryKey("pk_documents", x => x.id)
        );

        // ── Indexes on documents ─────────────────────────────────────────────
        migrationBuilder.CreateIndex("ix_documents_created_at",    "documents", "created_at");
        migrationBuilder.CreateIndex("ix_documents_document_date", "documents", "document_date");
        migrationBuilder.CreateIndex("ix_documents_category",      "documents", "category");
        migrationBuilder.CreateIndex("ix_documents_status",        "documents", "status");
        migrationBuilder.CreateIndex("ix_documents_content_type",  "documents", "content_type");

        // ── document_metadata ────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "document_metadata",
            columns: table => new
            {
                id          = table.Column<Guid>(nullable: false),
                document_id = table.Column<Guid>(nullable: false),
                key         = table.Column<string>(maxLength: 200, nullable: false),
                value       = table.Column<string>(nullable: true),
                type        = table.Column<string>(nullable: false),
                created_at  = table.Column<DateTime>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_document_metadata", x => x.id);
                table.ForeignKey(
                    name:              "fk_document_metadata_documents",
                    column:            x => x.document_id,
                    principalTable:    "documents",
                    principalColumn:   "id",
                    onDelete:          ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateIndex("ix_document_metadata_document_id", "document_metadata", "document_id");
        migrationBuilder.CreateIndex(
            "ix_document_metadata_doc_key",
            "document_metadata",
            new[] { "document_id", "key" }
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("document_metadata");
        migrationBuilder.DropTable("documents");
    }
}