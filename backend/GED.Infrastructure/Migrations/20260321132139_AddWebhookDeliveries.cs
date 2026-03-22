using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GED.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_number = table.Column<int>(type: "int", nullable: false),
                    title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    file_name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    file_size = table.Column<long>(type: "bigint", nullable: true),
                    content_type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    category = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    tags = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    metadata = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    changed_by = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    change_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    file_path = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    full_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    last_login_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    allowed_categories = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    webhook_config_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    @event = table.Column<string>(name: "event", type: "nvarchar(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    response_status_code = table.Column<int>(type: "int", nullable: true),
                    response_body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    attempt_number = table.Column<int>(type: "int", nullable: false),
                    succeeded = table.Column<bool>(type: "bit", nullable: false),
                    error_message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_deliveries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_versions_doc_version",
                table: "document_versions",
                columns: new[] { "document_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_versions_document_id",
                table: "document_versions",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                table: "users",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_created_at",
                table: "webhook_deliveries",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_event_status",
                table: "webhook_deliveries",
                columns: new[] { "event", "succeeded" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_versions");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "webhook_deliveries");
        }
    }
}
