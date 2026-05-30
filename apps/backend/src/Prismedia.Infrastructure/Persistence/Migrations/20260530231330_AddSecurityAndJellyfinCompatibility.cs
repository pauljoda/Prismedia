using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityAndJellyfinCompatibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_security",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    server_id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    default_profile_seeded = table.Column<bool>(type: "boolean", nullable: false),
                    api_key_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    api_key_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_security", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "jellyfin_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    normalized_username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    allow_nsfw = table.Column<bool>(type: "boolean", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jellyfin_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "jellyfin_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    client = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    device_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    device_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    application_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    invalidated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jellyfin_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_jellyfin_sessions_jellyfin_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "jellyfin_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_jellyfin_profiles_normalized_username",
                table: "jellyfin_profiles",
                column: "normalized_username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_jellyfin_sessions_profile_id_invalidated_at",
                table: "jellyfin_sessions",
                columns: new[] { "profile_id", "invalidated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_jellyfin_sessions_token_hash",
                table: "jellyfin_sessions",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_security");

            migrationBuilder.DropTable(
                name: "jellyfin_sessions");

            migrationBuilder.DropTable(
                name: "jellyfin_profiles");
        }
    }
}
