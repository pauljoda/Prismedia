using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CentralizedSettingsRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "library_settings");

            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_settings", x => x.key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_settings");

            migrationBuilder.CreateTable(
                name: "library_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    audio_preferred_languages = table.Column<string>(type: "text", nullable: false),
                    auto_generate_fingerprints = table.Column<bool>(type: "boolean", nullable: false),
                    auto_generate_metadata = table.Column<bool>(type: "boolean", nullable: false),
                    auto_generate_preview = table.Column<bool>(type: "boolean", nullable: false),
                    auto_scan_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    background_worker_concurrency = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    default_playback_mode = table.Column<string>(type: "text", nullable: false),
                    generate_phash = table.Column<bool>(type: "boolean", nullable: false),
                    generate_trickplay = table.Column<bool>(type: "boolean", nullable: false),
                    hide_nsfw = table.Column<bool>(type: "boolean", nullable: false),
                    hls_ffmpeg_path = table.Column<string>(type: "text", nullable: false, defaultValue: "ffmpeg"),
                    hls_transcoder_profile = table.Column<string>(type: "text", nullable: false, defaultValue: "Software"),
                    hls_vaapi_device = table.Column<string>(type: "text", nullable: false, defaultValue: "/dev/dri/renderD128"),
                    metadata_storage_dedicated = table.Column<bool>(type: "boolean", nullable: false),
                    nsfw_lan_auto_enable = table.Column<bool>(type: "boolean", nullable: false),
                    preview_clip_duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    scan_interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    show_cast_controls = table.Column<bool>(type: "boolean", nullable: false),
                    subtitle_font_scale = table.Column<float>(type: "real", nullable: false),
                    subtitle_opacity = table.Column<float>(type: "real", nullable: false),
                    subtitle_position_percent = table.Column<float>(type: "real", nullable: false),
                    subtitle_style = table.Column<string>(type: "text", nullable: false),
                    subtitles_auto_enable = table.Column<bool>(type: "boolean", nullable: false),
                    subtitles_preferred_languages = table.Column<string>(type: "text", nullable: false),
                    thumbnail_quality = table.Column<int>(type: "integer", nullable: false),
                    trickplay_interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    trickplay_quality = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_settings", x => x.id);
                });
        }
    }
}
