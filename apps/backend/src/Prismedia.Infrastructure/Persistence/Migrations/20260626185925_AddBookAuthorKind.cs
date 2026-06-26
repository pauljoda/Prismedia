using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookAuthorKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "entity_kinds",
                columns: new[] { "code", "category", "display_name", "storage_shape" },
                values: new object[] { "book-author", "Media", "Book Author", "folder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "entity_kinds",
                keyColumn: "code",
                keyValue: "book-author");
        }
    }
}
