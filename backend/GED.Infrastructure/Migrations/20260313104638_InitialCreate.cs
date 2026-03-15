using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GED.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_acls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    permission = table.Column<int>(type: "int", nullable: false),
                    granted_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    granted_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_acls", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_group_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    default_permission = table.Column<int>(type: "int", nullable: false),
                    added_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    added_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_group_members", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    icon = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    file_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    file_path = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    file_hash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    document_date = table.Column<DateTime>(type: "datetime2", nullable: true),
                    modified_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    modified_by = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    is_ocr_processed = table.Column<bool>(type: "bit", nullable: false),
                    ocr_text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    extracted_text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    tags = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    category = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    metadata = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    parent_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    processed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    retry_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_group_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    permission = table.Column<int>(type: "int", nullable: false),
                    assigned_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_group_assignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_metadata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_metadata", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_metadata_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acl_doc_user",
                table: "document_acls",
                columns: new[] { "document_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_group_members_group_doc",
                table: "document_group_members",
                columns: new[] { "group_id", "document_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_groups_active",
                table: "document_groups",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_document_metadata_doc_key",
                table: "document_metadata",
                columns: new[] { "document_id", "key" });

            migrationBuilder.CreateIndex(
                name: "ix_document_metadata_document_id",
                table: "document_metadata",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_category",
                table: "documents",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_documents_content_type",
                table: "documents",
                column: "content_type");

            migrationBuilder.CreateIndex(
                name: "ix_documents_created_at",
                table: "documents",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_documents_document_date",
                table: "documents",
                column: "document_date");

            migrationBuilder.CreateIndex(
                name: "ix_documents_status",
                table: "documents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_unprocessed",
                table: "outbox_messages",
                column: "processed_at");

            migrationBuilder.CreateIndex(
                name: "ix_user_group_assignments_group_user",
                table: "user_group_assignments",
                columns: new[] { "group_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_group_assignments_user",
                table: "user_group_assignments",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_acls");

            migrationBuilder.DropTable(
                name: "document_group_members");

            migrationBuilder.DropTable(
                name: "document_groups");

            migrationBuilder.DropTable(
                name: "document_metadata");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "user_group_assignments");

            migrationBuilder.DropTable(
                name: "documents");
        }
    }
}
