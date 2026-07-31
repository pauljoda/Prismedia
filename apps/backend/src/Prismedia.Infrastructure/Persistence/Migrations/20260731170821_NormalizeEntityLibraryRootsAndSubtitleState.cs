using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeEntityLibraryRootsAndSubtitleState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_library_roots",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    library_root_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_library_roots", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_entity_library_roots_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_entity_library_roots_library_roots_library_root_id",
                        column: x => x.library_root_id,
                        principalTable: "library_roots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "entity_subtitle_states",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subtitles_extracted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    subtitle_sidecar_signature = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_subtitle_states", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_entity_subtitle_states_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_entity_library_roots_library_root_id",
                table: "entity_library_roots",
                column: "library_root_id");

            // Preserve every existing one-to-one detail attachment, including unrooted requested
            // placeholders. The disjoint kind tables make the upsert deterministic.
            migrationBuilder.Sql(
                """
                INSERT INTO entity_library_roots (entity_id, library_root_id)
                SELECT entity_id, library_root_id FROM video_details
                UNION ALL SELECT entity_id, library_root_id FROM gallery_details
                UNION ALL SELECT entity_id, library_root_id FROM book_details
                UNION ALL SELECT entity_id, library_root_id FROM music_artist_details
                UNION ALL SELECT entity_id, library_root_id FROM audio_library_details;

                INSERT INTO entity_subtitle_states (entity_id, subtitles_extracted_at, subtitle_sidecar_signature)
                SELECT entity_id, subtitles_extracted_at, subtitle_sidecar_signature
                FROM video_details;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_book_details_library_roots_library_root_id",
                table: "book_details");
            migrationBuilder.DropForeignKey(
                name: "FK_gallery_details_library_roots_library_root_id",
                table: "gallery_details");
            migrationBuilder.DropForeignKey(
                name: "FK_video_details_library_roots_library_root_id",
                table: "video_details");
            migrationBuilder.DropTable(name: "audio_library_details");
            migrationBuilder.DropTable(name: "music_artist_details");
            migrationBuilder.DropIndex(name: "IX_video_details_library_root_id", table: "video_details");
            migrationBuilder.DropIndex(name: "IX_gallery_details_library_root_id", table: "gallery_details");
            migrationBuilder.DropIndex(name: "IX_book_details_library_root_id", table: "book_details");
            migrationBuilder.DropColumn(name: "library_root_id", table: "video_details");
            migrationBuilder.DropColumn(name: "subtitle_sidecar_signature", table: "video_details");
            migrationBuilder.DropColumn(name: "subtitles_extracted_at", table: "video_details");
            migrationBuilder.DropColumn(name: "library_root_id", table: "gallery_details");
            migrationBuilder.DropColumn(name: "library_root_id", table: "book_details");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "library_root_id",
                table: "video_details",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subtitle_sidecar_signature",
                table: "video_details",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "subtitles_extracted_at",
                table: "video_details",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "library_root_id",
                table: "gallery_details",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "library_root_id",
                table: "book_details",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audio_library_details",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    library_root_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audio_library_details", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_audio_library_details_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_audio_library_details_library_roots_library_root_id",
                        column: x => x.library_root_id,
                        principalTable: "library_roots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

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

            // The rollback faithfully restores rows for the former direct-root kinds. Any future
            // kind added to the generic attachment before rolling back this migration is necessarily
            // lossy because the historical schema has no table for it.
            migrationBuilder.Sql(
                """
                UPDATE video_details target
                SET library_root_id = source.library_root_id
                FROM entity_library_roots source
                WHERE source.entity_id = target.entity_id;
                UPDATE video_details target
                SET subtitles_extracted_at = source.subtitles_extracted_at,
                    subtitle_sidecar_signature = source.subtitle_sidecar_signature
                FROM entity_subtitle_states source
                WHERE source.entity_id = target.entity_id;
                UPDATE gallery_details target
                SET library_root_id = source.library_root_id
                FROM entity_library_roots source
                WHERE source.entity_id = target.entity_id;
                UPDATE book_details target
                SET library_root_id = source.library_root_id
                FROM entity_library_roots source
                WHERE source.entity_id = target.entity_id;
                INSERT INTO music_artist_details (entity_id, library_root_id)
                SELECT root.entity_id, root.library_root_id
                FROM entity_library_roots root
                INNER JOIN entities entity ON entity.id = root.entity_id
                WHERE entity.kind_code = 'music-artist';
                INSERT INTO audio_library_details (entity_id, library_root_id)
                SELECT root.entity_id, root.library_root_id
                FROM entity_library_roots root
                INNER JOIN entities entity ON entity.id = root.entity_id
                WHERE entity.kind_code = 'audio-library';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_video_details_library_root_id",
                table: "video_details",
                column: "library_root_id");

            migrationBuilder.CreateIndex(
                name: "IX_gallery_details_library_root_id",
                table: "gallery_details",
                column: "library_root_id");

            migrationBuilder.CreateIndex(
                name: "IX_book_details_library_root_id",
                table: "book_details",
                column: "library_root_id");

            migrationBuilder.CreateIndex(
                name: "IX_audio_library_details_library_root_id",
                table: "audio_library_details",
                column: "library_root_id");

            migrationBuilder.CreateIndex(
                name: "IX_music_artist_details_library_root_id",
                table: "music_artist_details",
                column: "library_root_id");

            migrationBuilder.AddForeignKey(
                name: "FK_book_details_library_roots_library_root_id",
                table: "book_details",
                column: "library_root_id",
                principalTable: "library_roots",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.DropTable(name: "entity_library_roots");
            migrationBuilder.DropTable(name: "entity_subtitle_states");

            migrationBuilder.AddForeignKey(
                name: "FK_gallery_details_library_roots_library_root_id",
                table: "gallery_details",
                column: "library_root_id",
                principalTable: "library_roots",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_video_details_library_roots_library_root_id",
                table: "video_details",
                column: "library_root_id",
                principalTable: "library_roots",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
