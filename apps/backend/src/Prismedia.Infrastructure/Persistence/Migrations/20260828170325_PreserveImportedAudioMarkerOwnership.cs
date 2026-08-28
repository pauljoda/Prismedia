using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreserveImportedAudioMarkerOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "source_index",
                table: "entity_markers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_entity_markers_entity_id_source_index",
                table: "entity_markers",
                columns: new[] { "entity_id", "source_index" },
                unique: true,
                filter: "source_index IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_entity_markers_entity_id_source_index",
                table: "entity_markers");

            migrationBuilder.DropColumn(
                name: "source_index",
                table: "entity_markers");
        }
    }
}
