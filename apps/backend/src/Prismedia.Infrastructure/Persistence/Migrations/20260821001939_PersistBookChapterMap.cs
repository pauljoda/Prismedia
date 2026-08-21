using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistBookChapterMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "book_chapter_audio_mappings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.CreateTable(
                name: "book_content_states",
                columns: table => new
                {
                    book_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_signature = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    mapping_signature = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    refreshed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_book_content_states", x => x.book_id);
                    table.ForeignKey(
                        name: "FK_book_content_states_entities_book_id",
                        column: x => x.book_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "book_reading_chapters",
                columns: table => new
                {
                    book_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_key = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    section_index = table.Column<int>(type: "integer", nullable: true),
                    start_fraction = table.Column<double>(type: "double precision", nullable: true),
                    end_fraction = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_book_reading_chapters", x => new { x.book_id, x.chapter_key });
                    table.ForeignKey(
                        name: "FK_book_reading_chapters_entities_book_id",
                        column: x => x.book_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_book_reading_chapters_book_id_display_order",
                table: "book_reading_chapters",
                columns: new[] { "book_id", "display_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "book_content_states");

            migrationBuilder.DropTable(
                name: "book_reading_chapters");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "book_chapter_audio_mappings");
        }
    }
}
