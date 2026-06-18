using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Prismedia.Application.Playback;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrowserSessionAudioPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "app_settings",
                keyColumn: "key",
                keyValue: BrowserSessionConstants.LegacyGlobalMusicPlayerStateKey);

            migrationBuilder.CreateTable(
                name: "browser_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_browser_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "browser_session_settings",
                columns: table => new
                {
                    browser_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value_json = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_browser_session_settings", x => new { x.browser_session_id, x.key });
                    table.ForeignKey(
                        name: "FK_browser_session_settings_browser_sessions_browser_session_id",
                        column: x => x.browser_session_id,
                        principalTable: "browser_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_browser_sessions_last_seen_at",
                table: "browser_sessions",
                column: "last_seen_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "browser_session_settings");

            migrationBuilder.DropTable(
                name: "browser_sessions");
        }
    }
}
