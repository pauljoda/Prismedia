using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyAppSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing preview rows persisted the former compatibility URL rather than a filesystem
            // path. Rewrite those rows before the compatibility routes disappear so upgraded libraries
            // keep their generated filmstrips without regenerating media assets.
            migrationBuilder.Sql(
                """
                UPDATE entity_files
                SET path = regexp_replace(
                    path,
                    '^/Videos/([0-9a-f-]{36})/Trickplay/([0-9]+)/tiles\.m3u8$',
                    '/api/playback/videos/\1/trickplay/\2/tiles.m3u8',
                    'i')
                WHERE path ~* '^/Videos/[0-9a-f-]{36}/Trickplay/[0-9]+/tiles\.m3u8$';
                """);

            migrationBuilder.DropTable(
                name: "app_security");

            migrationBuilder.DropColumn(
                name: "allow_sfw",
                table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE entity_files
                SET path = regexp_replace(
                    path,
                    '^/api/playback/videos/([0-9a-f-]{36})/trickplay/([0-9]+)/tiles\.m3u8$',
                    '/Videos/\1/Trickplay/\2/tiles.m3u8',
                    'i')
                WHERE path ~* '^/api/playback/videos/[0-9a-f-]{36}/trickplay/[0-9]+/tiles\.m3u8$';
                """);

            migrationBuilder.AddColumn<bool>(
                name: "allow_sfw",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("""ALTER TABLE users ALTER COLUMN allow_sfw DROP DEFAULT;""");

            migrationBuilder.CreateTable(
                name: "app_security",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    legacy_api_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    server_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_security", x => x.id);
                });
        }
    }
}
