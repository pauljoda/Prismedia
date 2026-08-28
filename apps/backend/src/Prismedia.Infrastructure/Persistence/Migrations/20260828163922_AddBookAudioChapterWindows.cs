using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookAudioChapterWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_book_chapter_audio_mappings_book_id_audio_track_entity_id",
                table: "book_chapter_audio_mappings");

            migrationBuilder.AddColumn<Guid>(
                name: "audio_marker_id",
                table: "book_chapter_audio_mappings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_book_chapter_audio_mappings_audio_marker_id",
                table: "book_chapter_audio_mappings",
                column: "audio_marker_id");

            migrationBuilder.CreateIndex(
                name: "UX_book_chapter_audio_mappings_marker",
                table: "book_chapter_audio_mappings",
                columns: new[] { "book_id", "audio_track_entity_id", "audio_marker_id" },
                unique: true,
                filter: "audio_marker_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_book_chapter_audio_mappings_whole_track",
                table: "book_chapter_audio_mappings",
                columns: new[] { "book_id", "audio_track_entity_id" },
                unique: true,
                filter: "audio_marker_id IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_book_chapter_audio_mappings_entity_markers_audio_marker_id",
                table: "book_chapter_audio_mappings",
                column: "audio_marker_id",
                principalTable: "entity_markers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_book_chapter_audio_mappings_entity_markers_audio_marker_id",
                table: "book_chapter_audio_mappings");

            migrationBuilder.DropIndex(
                name: "IX_book_chapter_audio_mappings_audio_marker_id",
                table: "book_chapter_audio_mappings");

            migrationBuilder.DropIndex(
                name: "UX_book_chapter_audio_mappings_marker",
                table: "book_chapter_audio_mappings");

            migrationBuilder.DropIndex(
                name: "UX_book_chapter_audio_mappings_whole_track",
                table: "book_chapter_audio_mappings");

            migrationBuilder.DropColumn(
                name: "audio_marker_id",
                table: "book_chapter_audio_mappings");

            migrationBuilder.CreateIndex(
                name: "IX_book_chapter_audio_mappings_book_id_audio_track_entity_id",
                table: "book_chapter_audio_mappings",
                columns: new[] { "book_id", "audio_track_entity_id" },
                unique: true);
        }
    }
}
