using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMusicArtistAndTrackSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "section_label",
                table: "audio_track_details",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "section_order",
                table: "audio_track_details",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "music_artist_details",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    library_root_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_music_artist_details", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_music_artist_details_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_music_artist_details_library_roots_library_root_id",
                        column: x => x.library_root_id,
                        principalTable: "library_roots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "entity_kinds",
                columns: new[] { "code", "category", "display_name", "storage_shape" },
                values: new object[] { "music-artist", "Media", "Music Artist", "folder" });

            migrationBuilder.CreateIndex(
                name: "IX_music_artist_details_library_root_id",
                table: "music_artist_details",
                column: "library_root_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "music_artist_details");

            migrationBuilder.DeleteData(
                table: "entity_kinds",
                keyColumn: "code",
                keyValue: "music-artist");

            migrationBuilder.DropColumn(
                name: "section_label",
                table: "audio_track_details");

            migrationBuilder.DropColumn(
                name: "section_order",
                table: "audio_track_details");
        }
    }
}
