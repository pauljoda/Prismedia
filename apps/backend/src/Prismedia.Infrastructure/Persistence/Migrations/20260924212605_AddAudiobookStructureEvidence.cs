using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAudiobookStructureEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "untitled",
                table: "entity_markers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "embedded_title",
                table: "audio_track_details",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "embedded_track_number",
                table: "audio_track_details",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "tags_recorded_at",
                table: "audio_track_details",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "audiobook_shape",
                table: "acquisitions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // Earlier probes stored an invented "Chapter N" title for untitled source chapters. Those
            // placeholders cannot be told apart from a real "Chapter N" title, so treat every such
            // source marker as untitled until its track is probed again (tags_recorded_at is null
            // for all tracks, so the startup backfill re-probes them and restores exact titles).
            migrationBuilder.Sql("""
                UPDATE entity_markers
                SET untitled = TRUE
                WHERE source_index IS NOT NULL
                  AND title = 'Chapter ' || (source_index + 1)::text;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "untitled",
                table: "entity_markers");

            migrationBuilder.DropColumn(
                name: "embedded_title",
                table: "audio_track_details");

            migrationBuilder.DropColumn(
                name: "embedded_track_number",
                table: "audio_track_details");

            migrationBuilder.DropColumn(
                name: "tags_recorded_at",
                table: "audio_track_details");

            migrationBuilder.DropColumn(
                name: "audiobook_shape",
                table: "acquisitions");
        }
    }
}
