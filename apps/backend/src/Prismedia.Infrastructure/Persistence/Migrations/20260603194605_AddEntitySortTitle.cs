using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEntitySortTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sort_title",
                table: "entities",
                type: "text",
                nullable: false,
                computedColumnSql: "trim(CASE\n    WHEN lower(title) LIKE 'the %' THEN substr(title, 5)\n    WHEN lower(title) LIKE 'an %' THEN substr(title, 4)\n    WHEN lower(title) LIKE 'a %' THEN substr(title, 3)\n    ELSE title\nEND)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_entities_kind_code_sort_title",
                table: "entities",
                columns: new[] { "kind_code", "sort_title" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_entities_kind_code_sort_title",
                table: "entities");

            migrationBuilder.DropColumn(
                name: "sort_title",
                table: "entities");
        }
    }
}
