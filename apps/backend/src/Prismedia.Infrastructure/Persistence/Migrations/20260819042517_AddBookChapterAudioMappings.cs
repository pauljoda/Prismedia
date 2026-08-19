using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookChapterAudioMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "book_chapter_audio_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    book_id = table.Column<Guid>(type: "uuid", nullable: false),
                    readable_chapter_key = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    audio_track_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_book_chapter_audio_mappings", x => x.id);
                    table.ForeignKey(
                        name: "FK_book_chapter_audio_mappings_entities_audio_track_entity_id",
                        column: x => x.audio_track_entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_book_chapter_audio_mappings_entities_book_id",
                        column: x => x.book_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_book_chapter_audio_mappings_audio_track_entity_id",
                table: "book_chapter_audio_mappings",
                column: "audio_track_entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_book_chapter_audio_mappings_book_id_audio_track_entity_id",
                table: "book_chapter_audio_mappings",
                columns: new[] { "book_id", "audio_track_entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_book_chapter_audio_mappings_book_id_readable_chapter_key",
                table: "book_chapter_audio_mappings",
                columns: new[] { "book_id", "readable_chapter_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "book_chapter_audio_mappings");
        }
    }
}
