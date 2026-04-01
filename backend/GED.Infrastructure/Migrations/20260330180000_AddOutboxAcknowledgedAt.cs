using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GED.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxAcknowledgedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "acknowledged_at",
                table: "outbox_messages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_acknowledged",
                table: "outbox_messages",
                columns: new[] { "ProcessedAt", "AcknowledgedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_acknowledged",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "acknowledged_at",
                table: "outbox_messages");
        }
    }
}
