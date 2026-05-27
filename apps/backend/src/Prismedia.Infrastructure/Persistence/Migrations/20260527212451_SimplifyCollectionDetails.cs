using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyCollectionDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "slideshow_auto_advance",
                table: "collection_details");

            migrationBuilder.DropColumn(
                name: "slideshow_duration_seconds",
                table: "collection_details");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "slideshow_auto_advance",
                table: "collection_details",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "slideshow_duration_seconds",
                table: "collection_details",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
