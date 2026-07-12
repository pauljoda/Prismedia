using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookRenditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_monitors_entity_id",
                table: "monitors");

            migrationBuilder.AddColumn<string>(
                name: "book_rendition",
                table: "monitors",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "book_rendition",
                table: "acquisitions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // Every legacy Book acquisition/monitor represented the established ebook path. Backfill before
            // replacing the one-monitor-per-Entity index so existing intent remains rendition-specific and an
            // audiobook monitor can be added alongside it without ambiguity.
            migrationBuilder.Sql("""
                UPDATE acquisitions
                SET book_rendition = 'ebook'
                WHERE kind = 'book' AND book_rendition IS NULL;

                UPDATE monitors
                SET book_rendition = 'ebook'
                WHERE kind = 'book' AND book_rendition IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_monitors_entity_id",
                table: "monitors",
                column: "entity_id",
                unique: true,
                filter: "book_rendition IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_monitors_entity_id_book_rendition",
                table: "monitors",
                columns: new[] { "entity_id", "book_rendition" },
                unique: true,
                filter: "book_rendition IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous schema permits only one monitor per Entity. A rollback must collapse parallel
            // rendition monitors before recreating that unique index; preserve ebook intent first, then the
            // oldest remaining monitor when no ebook row exists.
            migrationBuilder.Sql("""
                DELETE FROM monitors AS monitor
                USING (
                    SELECT id,
                           ROW_NUMBER() OVER (
                               PARTITION BY entity_id
                               ORDER BY CASE WHEN book_rendition = 'ebook' THEN 0 ELSE 1 END,
                                        created_at,
                                        id) AS ordinal
                    FROM monitors
                    WHERE entity_id IS NOT NULL
                ) AS ranked
                WHERE monitor.id = ranked.id AND ranked.ordinal > 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_monitors_entity_id",
                table: "monitors");

            migrationBuilder.DropIndex(
                name: "IX_monitors_entity_id_book_rendition",
                table: "monitors");

            migrationBuilder.DropColumn(
                name: "book_rendition",
                table: "monitors");

            migrationBuilder.DropColumn(
                name: "book_rendition",
                table: "acquisitions");

            migrationBuilder.CreateIndex(
                name: "IX_monitors_entity_id",
                table: "monitors",
                column: "entity_id",
                unique: true);
        }
    }
}
