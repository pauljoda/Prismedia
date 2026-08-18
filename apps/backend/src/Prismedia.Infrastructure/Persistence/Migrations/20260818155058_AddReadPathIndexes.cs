using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReadPathIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_entity_sources_code_value",
                table: "entity_sources",
                columns: new[] { "code", "value" });

            migrationBuilder.CreateIndex(
                name: "IX_entity_files_role_path",
                table: "entity_files",
                columns: new[] { "role", "path" });

            // Title search translates to lower(title) LIKE '%q%', which no btree index can serve.
            // A trigram GIN index makes substring search index-assisted with no query change.
            // pg_trgm is a trusted extension, so database-owner privileges suffice.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_entities_title_trgm\" " +
                "ON entities USING gin (lower(title) gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The pg_trgm extension is left installed: it is shared infrastructure other objects
            // may depend on, and dropping extensions in a rollback is not safe.
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_entities_title_trgm\";");

            migrationBuilder.DropIndex(
                name: "IX_entity_sources_code_value",
                table: "entity_sources");

            migrationBuilder.DropIndex(
                name: "IX_entity_files_role_path",
                table: "entity_files");
        }
    }
}
