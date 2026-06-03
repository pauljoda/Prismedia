using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityRelationshipReferenceCountIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_entity_relationship_links_target_entity_id",
                table: "entity_relationship_links");

            migrationBuilder.CreateIndex(
                name: "IX_entity_relationship_links_target_entity_id_entity_id",
                table: "entity_relationship_links",
                columns: new[] { "target_entity_id", "entity_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_entity_relationship_links_target_entity_id_entity_id",
                table: "entity_relationship_links");

            migrationBuilder.CreateIndex(
                name: "IX_entity_relationship_links_target_entity_id",
                table: "entity_relationship_links",
                column: "target_entity_id");
        }
    }
}
