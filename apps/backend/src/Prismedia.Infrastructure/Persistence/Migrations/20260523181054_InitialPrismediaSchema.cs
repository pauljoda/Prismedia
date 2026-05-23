using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPrismediaSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "database_backups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    backup_path = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_database_backups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entity_kinds",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    storage_shape = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_kinds", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "job_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    progress = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "text", nullable: true),
                    target_entity_kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    target_entity_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    target_label = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    locked_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_runs", x => x.id);
                    table.CheckConstraint("ck_job_runs_attempts", "attempts >= 0 AND max_attempts > 0");
                    table.CheckConstraint("ck_job_runs_progress", "progress >= 0 AND progress <= 100");
                });

            migrationBuilder.CreateTable(
                name: "library_roots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    recursive = table.Column<bool>(type: "boolean", nullable: false),
                    scan_videos = table.Column<bool>(type: "boolean", nullable: false),
                    scan_images = table.Column<bool>(type: "boolean", nullable: false),
                    scan_audio = table.Column<bool>(type: "boolean", nullable: false),
                    scan_books = table.Column<bool>(type: "boolean", nullable: false),
                    is_nsfw = table.Column<bool>(type: "boolean", nullable: false),
                    last_scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_roots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "library_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    auto_scan_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    scan_interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    auto_generate_metadata = table.Column<bool>(type: "boolean", nullable: false),
                    auto_generate_fingerprints = table.Column<bool>(type: "boolean", nullable: false),
                    generate_phash = table.Column<bool>(type: "boolean", nullable: false),
                    auto_generate_preview = table.Column<bool>(type: "boolean", nullable: false),
                    generate_trickplay = table.Column<bool>(type: "boolean", nullable: false),
                    trickplay_interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    preview_clip_duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    thumbnail_quality = table.Column<int>(type: "integer", nullable: false),
                    trickplay_quality = table.Column<int>(type: "integer", nullable: false),
                    background_worker_concurrency = table.Column<int>(type: "integer", nullable: false),
                    nsfw_lan_auto_enable = table.Column<bool>(type: "boolean", nullable: false),
                    hide_nsfw = table.Column<bool>(type: "boolean", nullable: false),
                    metadata_storage_dedicated = table.Column<bool>(type: "boolean", nullable: false),
                    subtitles_auto_enable = table.Column<bool>(type: "boolean", nullable: false),
                    subtitles_preferred_languages = table.Column<string>(type: "text", nullable: false),
                    audio_preferred_languages = table.Column<string>(type: "text", nullable: false),
                    subtitle_style = table.Column<string>(type: "text", nullable: false),
                    subtitle_font_scale = table.Column<float>(type: "real", nullable: false),
                    subtitle_position_percent = table.Column<float>(type: "real", nullable: false),
                    subtitle_opacity = table.Column<float>(type: "real", nullable: false),
                    default_playback_mode = table.Column<string>(type: "text", nullable: false),
                    show_cast_controls = table.Column<bool>(type: "boolean", nullable: false),
                    hls_transcoder_profile = table.Column<string>(type: "text", nullable: false, defaultValue: "Software"),
                    hls_ffmpeg_path = table.Column<string>(type: "text", nullable: false, defaultValue: "ffmpeg"),
                    hls_vaapi_device = table.Column<string>(type: "text", nullable: false, defaultValue: "/dev/dri/renderD128"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "media_file_ignores",
                columns: table => new
                {
                    path = table.Column<string>(type: "text", nullable: false),
                    entity_kind_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_file_ignores", x => x.path);
                });

            migrationBuilder.CreateTable(
                name: "provider_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    provider_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    settings_json = table.Column<string>(type: "jsonb", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_nsfw = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ui_prefs",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    value_json = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ui_prefs", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "entities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    parent_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entities", x => x.id);
                    table.ForeignKey(
                        name: "FK_entities_entities_parent_entity_id",
                        column: x => x.parent_entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_entities_entity_kinds_kind_code",
                        column: x => x.kind_code,
                        principalTable: "entity_kinds",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "provider_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    encrypted_value = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_credentials", x => x.id);
                    table.ForeignKey(
                        name: "FK_provider_credentials_provider_configs_provider_config_id",
                        column: x => x.provider_config_id,
                        principalTable: "provider_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "audio_track_details",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    embedded_artist = table.Column<string>(type: "text", nullable: true),
                    embedded_album = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audio_track_details", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_audio_track_details_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "book_chapter_details",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cover_page_entity_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_book_chapter_details", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_book_chapter_details_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "book_details",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    book_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    cover_page_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    library_root_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_book_details", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_book_details_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_book_details_library_roots_library_root_id",
                        column: x => x.library_root_id,
                        principalTable: "library_roots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "collection_details",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    rule_tree_json = table.Column<string>(type: "jsonb", nullable: true),
                    cover_mode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    cover_item_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    slideshow_duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    slideshow_auto_advance = table.Column<bool>(type: "boolean", nullable: false),
                    last_refreshed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_details", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_collection_details_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "collection_item_details",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_item_details", x => x.id);
                    table.ForeignKey(
                        name: "FK_collection_item_details_entities_collection_entity_id",
                        column: x => x.collection_entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_collection_item_details_entities_item_entity_id",
                        column: x => x.item_entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_classifications",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    system = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_classifications", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_entity_classifications_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_dates",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    sortable_value = table.Column<DateOnly>(type: "date", nullable: true),
                    precision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_dates", x => new { x.entity_id, x.code });
                    table.ForeignKey(
                        name: "FK_entity_dates_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_descriptions",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_descriptions", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_entity_descriptions_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_external_ids",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_external_ids", x => x.id);
                    table.ForeignKey(
                        name: "FK_entity_external_ids_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    mime_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "scan"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_files", x => x.id);
                    table.ForeignKey(
                        name: "FK_entity_files_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_flags",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_favorite = table.Column<bool>(type: "boolean", nullable: false),
                    is_nsfw = table.Column<bool>(type: "boolean", nullable: false),
                    is_organized = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_flags", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_entity_flags_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_lifetimes",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    start_value = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    start_sortable_value = table.Column<DateOnly>(type: "date", nullable: true),
                    start_precision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    end_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    end_value = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    end_sortable_value = table.Column<DateOnly>(type: "date", nullable: true),
                    end_precision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_lifetimes", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_entity_lifetimes_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_markers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    seconds = table.Column<double>(type: "double precision", nullable: false),
                    end_seconds = table.Column<double>(type: "double precision", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_markers", x => x.id);
                    table.ForeignKey(
                        name: "FK_entity_markers_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_playback",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    play_count = table.Column<int>(type: "integer", nullable: false),
                    play_duration_seconds = table.Column<double>(type: "double precision", nullable: false),
                    resume_seconds = table.Column<double>(type: "double precision", nullable: false),
                    last_played_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_playback", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_entity_playback_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_positions",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_positions", x => new { x.entity_id, x.code });
                    table.ForeignKey(
                        name: "FK_entity_positions_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_progress",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<int>(type: "integer", nullable: false),
                    mode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_progress", x => x.entity_id);
                    table.CheckConstraint("ck_entity_progress_bounds", "index >= 0 AND total >= 0");
                    table.ForeignKey(
                        name: "FK_entity_progress_entities_current_entity_id",
                        column: x => x.current_entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_entity_progress_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_ratings",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_ratings", x => x.entity_id);
                    table.CheckConstraint("ck_entity_ratings_value", "value >= 0 AND value <= 5");
                    table.ForeignKey(
                        name: "FK_entity_ratings_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_relationship_links",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relationship_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    target_kind_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_relationship_links", x => new { x.entity_id, x.relationship_code, x.target_entity_id });
                    table.ForeignKey(
                        name: "FK_entity_relationship_links_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_entity_relationship_links_entities_target_entity_id",
                        column: x => x.target_entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_entity_relationship_links_entity_kinds_target_kind_code",
                        column: x => x.target_kind_code,
                        principalTable: "entity_kinds",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "entity_sources",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_sources", x => new { x.entity_id, x.code });
                    table.ForeignKey(
                        name: "FK_entity_sources_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_stats",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_stats", x => new { x.entity_id, x.code });
                    table.CheckConstraint("ck_entity_stats_value", "value >= 0");
                    table.ForeignKey(
                        name: "FK_entity_stats_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_subtitles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    label = table.Column<string>(type: "text", nullable: true),
                    format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    storage_path = table.Column<string>(type: "text", nullable: false),
                    source_format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_path = table.Column<string>(type: "text", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_subtitles", x => x.id);
                    table.ForeignKey(
                        name: "FK_entity_subtitles_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_technical",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    duration_seconds = table.Column<double>(type: "double precision", nullable: true),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    frame_rate = table.Column<double>(type: "double precision", nullable: true),
                    bit_rate = table.Column<int>(type: "integer", nullable: true),
                    sample_rate = table.Column<int>(type: "integer", nullable: true),
                    channels = table.Column<int>(type: "integer", nullable: true),
                    codec = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    container = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    format = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_technical", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_entity_technical_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_urls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_urls", x => x.id);
                    table.ForeignKey(
                        name: "FK_entity_urls_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fingerprint_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_config_id = table.Column<Guid>(type: "uuid", nullable: true),
                    algorithm = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hash = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fingerprint_submissions", x => x.id);
                    table.ForeignKey(
                        name: "FK_fingerprint_submissions_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_fingerprint_submissions_provider_configs_provider_config_id",
                        column: x => x.provider_config_id,
                        principalTable: "provider_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "gallery_details",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gallery_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    cover_image_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    library_root_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gallery_details", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_gallery_details_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_gallery_details_library_roots_library_root_id",
                        column: x => x.library_root_id,
                        principalTable: "library_roots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "identify_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_config_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    match_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    raw_result_json = table.Column<string>(type: "jsonb", nullable: true),
                    proposed_result_json = table.Column<string>(type: "jsonb", nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identify_results", x => x.id);
                    table.ForeignKey(
                        name: "FK_identify_results_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_identify_results_provider_configs_provider_config_id",
                        column: x => x.provider_config_id,
                        principalTable: "provider_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "person_details",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    disambiguation = table.Column<string>(type: "text", nullable: true),
                    gender = table.Column<string>(type: "text", nullable: true),
                    country = table.Column<string>(type: "text", nullable: true),
                    ethnicity = table.Column<string>(type: "text", nullable: true),
                    eye_color = table.Column<string>(type: "text", nullable: true),
                    hair_color = table.Column<string>(type: "text", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    weight = table.Column<int>(type: "integer", nullable: true),
                    measurements = table.Column<string>(type: "text", nullable: true),
                    tattoos = table.Column<string>(type: "text", nullable: true),
                    piercings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_person_details", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_person_details_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tag_details",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ignore_auto_tag = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag_details", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_tag_details_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trickplay_infos",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    tile_width = table.Column<int>(type: "integer", nullable: false),
                    tile_height = table.Column<int>(type: "integer", nullable: false),
                    thumbnail_count = table.Column<int>(type: "integer", nullable: false),
                    interval_seconds = table.Column<double>(type: "double precision", nullable: false),
                    bandwidth = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trickplay_infos", x => new { x.entity_id, x.width });
                    table.CheckConstraint("ck_trickplay_infos_height", "height > 0");
                    table.CheckConstraint("ck_trickplay_infos_interval", "interval_seconds > 0");
                    table.CheckConstraint("ck_trickplay_infos_thumbnail_count", "thumbnail_count >= 0");
                    table.CheckConstraint("ck_trickplay_infos_tiles", "tile_width > 0 AND tile_height > 0");
                    table.CheckConstraint("ck_trickplay_infos_width", "width > 0");
                    table.ForeignKey(
                        name: "FK_trickplay_infos_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "video_details",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    library_root_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subtitles_extracted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_details", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_video_details_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_video_details_library_roots_library_root_id",
                        column: x => x.library_root_id,
                        principalTable: "library_roots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "video_series_details",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_series_details", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_video_series_details_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_file_fingerprints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    algorithm = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_file_fingerprints", x => x.id);
                    table.ForeignKey(
                        name: "FK_entity_file_fingerprints_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_entity_file_fingerprints_entity_files_entity_file_id",
                        column: x => x.entity_file_id,
                        principalTable: "entity_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "media_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    path = table.Column<string>(type: "text", nullable: false),
                    protocol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    container = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    duration_seconds = table.Column<double>(type: "double precision", nullable: true),
                    bit_rate = table.Column<int>(type: "integer", nullable: true),
                    video_codec = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    audio_codec = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    frame_rate = table.Column<double>(type: "double precision", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_sources", x => x.id);
                    table.ForeignKey(
                        name: "FK_media_sources_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_media_sources_entity_files_entity_file_id",
                        column: x => x.entity_file_id,
                        principalTable: "entity_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "media_streams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stream_index = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    codec = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    frame_rate = table.Column<double>(type: "double precision", nullable: true),
                    bit_rate = table.Column<int>(type: "integer", nullable: true),
                    sample_rate = table.Column<int>(type: "integer", nullable: true),
                    channels = table.Column<int>(type: "integer", nullable: true),
                    pixel_format = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    bit_depth = table.Column<int>(type: "integer", nullable: true),
                    color_range = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    color_space = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    color_transfer = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    color_primaries = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    dv_profile = table.Column<int>(type: "integer", nullable: true),
                    dv_level = table.Column<int>(type: "integer", nullable: true),
                    rpu_present_flag = table.Column<bool>(type: "boolean", nullable: true),
                    el_present_flag = table.Column<bool>(type: "boolean", nullable: true),
                    bl_present_flag = table.Column<bool>(type: "boolean", nullable: true),
                    dv_bl_signal_compatibility_id = table.Column<int>(type: "integer", nullable: true),
                    hdr10_plus_present_flag = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_forced = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_streams", x => x.id);
                    table.ForeignKey(
                        name: "FK_media_streams_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_media_streams_media_sources_media_source_id",
                        column: x => x.media_source_id,
                        principalTable: "media_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "entity_kinds",
                columns: new[] { "code", "category", "display_name", "storage_shape" },
                values: new object[,]
                {
                    { "audio", "Media", "Audio", "file" },
                    { "audio-library", "Media", "Audio Library", "folder" },
                    { "audio-track", "Media", "Audio Track", "file" },
                    { "book", "Media", "Book", "archive" },
                    { "book-chapter", "Media", "Book Chapter", "none" },
                    { "book-page", "Media", "Book Page", "archive-entry" },
                    { "book-volume", "Media", "Book Volume", "none" },
                    { "collection", "Collection", "Collection", "none" },
                    { "gallery", "Media", "Gallery", "folder" },
                    { "image", "Media", "Image", "file" },
                    { "person", "Taxonomy", "Person", "none" },
                    { "studio", "Taxonomy", "Studio", "none" },
                    { "tag", "Taxonomy", "Tag", "none" },
                    { "video", "Media", "Video", "file" },
                    { "video-season", "Media", "Video Season", "folder" },
                    { "video-series", "Media", "Video Series", "folder" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_audio_library_details_library_root_id",
                table: "audio_library_details",
                column: "library_root_id");

            migrationBuilder.CreateIndex(
                name: "IX_book_details_library_root_id",
                table: "book_details",
                column: "library_root_id");

            migrationBuilder.CreateIndex(
                name: "IX_collection_item_details_collection_entity_id_item_entity_id",
                table: "collection_item_details",
                columns: new[] { "collection_entity_id", "item_entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_item_details_item_entity_id",
                table: "collection_item_details",
                column: "item_entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_entities_kind_code_title",
                table: "entities",
                columns: new[] { "kind_code", "title" });

            migrationBuilder.CreateIndex(
                name: "IX_entities_parent_entity_id",
                table: "entities",
                column: "parent_entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_entity_external_ids_entity_id_provider",
                table: "entity_external_ids",
                columns: new[] { "entity_id", "provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_entity_external_ids_provider",
                table: "entity_external_ids",
                column: "provider");

            migrationBuilder.CreateIndex(
                name: "IX_entity_file_fingerprints_entity_file_id",
                table: "entity_file_fingerprints",
                column: "entity_file_id");

            migrationBuilder.CreateIndex(
                name: "IX_entity_file_fingerprints_entity_id_algorithm",
                table: "entity_file_fingerprints",
                columns: new[] { "entity_id", "algorithm" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_entity_files_entity_id_role",
                table: "entity_files",
                columns: new[] { "entity_id", "role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_entity_markers_entity_id_seconds",
                table: "entity_markers",
                columns: new[] { "entity_id", "seconds" });

            migrationBuilder.CreateIndex(
                name: "IX_entity_progress_current_entity_id",
                table: "entity_progress",
                column: "current_entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_entity_relationship_links_entity_id_relationship_code_sort_~",
                table: "entity_relationship_links",
                columns: new[] { "entity_id", "relationship_code", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_entity_relationship_links_target_entity_id",
                table: "entity_relationship_links",
                column: "target_entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_entity_relationship_links_target_kind_code_target_entity_id",
                table: "entity_relationship_links",
                columns: new[] { "target_kind_code", "target_entity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_entity_subtitles_entity_id_language_source",
                table: "entity_subtitles",
                columns: new[] { "entity_id", "language", "source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_entity_urls_entity_id_sort_order",
                table: "entity_urls",
                columns: new[] { "entity_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_entity_urls_entity_id_url",
                table: "entity_urls",
                columns: new[] { "entity_id", "url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fingerprint_submissions_entity_id_algorithm_hash",
                table: "fingerprint_submissions",
                columns: new[] { "entity_id", "algorithm", "hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fingerprint_submissions_provider_config_id",
                table: "fingerprint_submissions",
                column: "provider_config_id");

            migrationBuilder.CreateIndex(
                name: "IX_gallery_details_library_root_id",
                table: "gallery_details",
                column: "library_root_id");

            migrationBuilder.CreateIndex(
                name: "IX_identify_results_entity_id_status",
                table: "identify_results",
                columns: new[] { "entity_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_identify_results_provider_config_id",
                table: "identify_results",
                column: "provider_config_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_runs_dedup",
                table: "job_runs",
                columns: new[] { "type", "target_entity_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_job_runs_status_available_at_priority",
                table: "job_runs",
                columns: new[] { "status", "available_at", "priority" });

            migrationBuilder.CreateIndex(
                name: "IX_library_roots_path",
                table: "library_roots",
                column: "path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_media_file_ignores_entity_kind_code",
                table: "media_file_ignores",
                column: "entity_kind_code");

            migrationBuilder.CreateIndex(
                name: "IX_media_sources_entity_file_id",
                table: "media_sources",
                column: "entity_file_id");

            migrationBuilder.CreateIndex(
                name: "IX_media_sources_entity_id_path",
                table: "media_sources",
                columns: new[] { "entity_id", "path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_media_streams_entity_id",
                table: "media_streams",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_media_streams_media_source_id_stream_index",
                table: "media_streams",
                columns: new[] { "media_source_id", "stream_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provider_configs_provider_code",
                table: "provider_configs",
                column: "provider_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provider_credentials_provider_config_id_credential_key",
                table: "provider_credentials",
                columns: new[] { "provider_config_id", "credential_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_video_details_library_root_id",
                table: "video_details",
                column: "library_root_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audio_library_details");

            migrationBuilder.DropTable(
                name: "audio_track_details");

            migrationBuilder.DropTable(
                name: "book_chapter_details");

            migrationBuilder.DropTable(
                name: "book_details");

            migrationBuilder.DropTable(
                name: "collection_details");

            migrationBuilder.DropTable(
                name: "collection_item_details");

            migrationBuilder.DropTable(
                name: "database_backups");

            migrationBuilder.DropTable(
                name: "entity_classifications");

            migrationBuilder.DropTable(
                name: "entity_dates");

            migrationBuilder.DropTable(
                name: "entity_descriptions");

            migrationBuilder.DropTable(
                name: "entity_external_ids");

            migrationBuilder.DropTable(
                name: "entity_file_fingerprints");

            migrationBuilder.DropTable(
                name: "entity_flags");

            migrationBuilder.DropTable(
                name: "entity_lifetimes");

            migrationBuilder.DropTable(
                name: "entity_markers");

            migrationBuilder.DropTable(
                name: "entity_playback");

            migrationBuilder.DropTable(
                name: "entity_positions");

            migrationBuilder.DropTable(
                name: "entity_progress");

            migrationBuilder.DropTable(
                name: "entity_ratings");

            migrationBuilder.DropTable(
                name: "entity_relationship_links");

            migrationBuilder.DropTable(
                name: "entity_sources");

            migrationBuilder.DropTable(
                name: "entity_stats");

            migrationBuilder.DropTable(
                name: "entity_subtitles");

            migrationBuilder.DropTable(
                name: "entity_technical");

            migrationBuilder.DropTable(
                name: "entity_urls");

            migrationBuilder.DropTable(
                name: "fingerprint_submissions");

            migrationBuilder.DropTable(
                name: "gallery_details");

            migrationBuilder.DropTable(
                name: "identify_results");

            migrationBuilder.DropTable(
                name: "job_runs");

            migrationBuilder.DropTable(
                name: "library_settings");

            migrationBuilder.DropTable(
                name: "media_file_ignores");

            migrationBuilder.DropTable(
                name: "media_streams");

            migrationBuilder.DropTable(
                name: "person_details");

            migrationBuilder.DropTable(
                name: "provider_credentials");

            migrationBuilder.DropTable(
                name: "tag_details");

            migrationBuilder.DropTable(
                name: "trickplay_infos");

            migrationBuilder.DropTable(
                name: "ui_prefs");

            migrationBuilder.DropTable(
                name: "video_details");

            migrationBuilder.DropTable(
                name: "video_series_details");

            migrationBuilder.DropTable(
                name: "media_sources");

            migrationBuilder.DropTable(
                name: "provider_configs");

            migrationBuilder.DropTable(
                name: "library_roots");

            migrationBuilder.DropTable(
                name: "entity_files");

            migrationBuilder.DropTable(
                name: "entities");

            migrationBuilder.DropTable(
                name: "entity_kinds");
        }
    }
}
