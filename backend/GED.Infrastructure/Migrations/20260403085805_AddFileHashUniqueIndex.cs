using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GED.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFileHashUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_documents_file_hash",
                table: "documents",
                column: "file_hash",
                unique: true,
                filter: "[file_hash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_documents_file_hash",
                table: "documents");
        }
    }
}
