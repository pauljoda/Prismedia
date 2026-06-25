using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookAcquisition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "book_acquisition_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    target_library_root_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path_template = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    import_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "move"),
                    allowed_formats = table.Column<string[]>(type: "text[]", nullable: false),
                    language = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    min_seeders = table.Column<int>(type: "integer", nullable: false),
                    min_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    max_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    required_terms = table.Column<string[]>(type: "text[]", nullable: false),
                    ignored_terms = table.Column<string[]>(type: "text[]", nullable: false),
                    auto_pick = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_book_acquisition_profiles", x => x.id);
                    table.ForeignKey(
                        name: "FK_book_acquisition_profiles_library_roots_target_library_root~",
                        column: x => x.target_library_root_id,
                        principalTable: "library_roots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "download_client_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    category = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_download_client_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "indexer_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    categories = table.Column<int[]>(type: "integer[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "acquisitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_history_id = table.Column<Guid>(type: "uuid", nullable: true),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "pending"),
                    status_message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    author = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    series = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    year = table.Column<int>(type: "integer", nullable: true),
                    poster_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    plugin_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    plugin_item_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    external_ids_json = table.Column<string>(type: "jsonb", nullable: false),
                    source_urls_json = table.Column<string>(type: "jsonb", nullable: false),
                    selected_release_json = table.Column<string>(type: "jsonb", nullable: true),
                    final_source_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_acquisitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_acquisitions_book_acquisition_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "book_acquisition_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_acquisitions_request_history_request_history_id",
                        column: x => x.request_history_id,
                        principalTable: "request_history",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "download_client_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    download_client_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    encrypted_value = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_download_client_credentials", x => x.id);
                    table.ForeignKey(
                        name: "FK_download_client_credentials_download_client_configs_downloa~",
                        column: x => x.download_client_config_id,
                        principalTable: "download_client_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexer_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    indexer_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    encrypted_value = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_credentials", x => x.id);
                    table.ForeignKey(
                        name: "FK_indexer_credentials_indexer_configs_indexer_config_id",
                        column: x => x.indexer_config_id,
                        principalTable: "indexer_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "acquisition_import_hints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    acquisition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    plugin_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    plugin_item_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    external_ids_json = table.Column<string>(type: "jsonb", nullable: false),
                    source_urls_json = table.Column<string>(type: "jsonb", nullable: false),
                    title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    author = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    series = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    year = table.Column<int>(type: "integer", nullable: true),
                    poster_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    consumed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_acquisition_import_hints", x => x.id);
                    table.ForeignKey(
                        name: "FK_acquisition_import_hints_acquisitions_acquisition_id",
                        column: x => x.acquisition_id,
                        principalTable: "acquisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "download_transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    acquisition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    download_client_config_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_item_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    category = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    save_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    content_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    progress = table.Column<double>(type: "double precision", nullable: false),
                    state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_download_transfers", x => x.id);
                    table.ForeignKey(
                        name: "FK_download_transfers_acquisitions_acquisition_id",
                        column: x => x.acquisition_id,
                        principalTable: "acquisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_download_transfers_download_client_configs_download_client_~",
                        column: x => x.download_client_config_id,
                        principalTable: "download_client_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "release_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    acquisition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    indexer_config_id = table.Column<Guid>(type: "uuid", nullable: true),
                    indexer_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    title = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    seeders = table.Column<int>(type: "integer", nullable: true),
                    peers = table.Column<int>(type: "integer", nullable: true),
                    protocol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "torrent"),
                    download_url = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    magnet_url = table.Column<string>(type: "text", nullable: true),
                    info_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    info_url = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    score = table.Column<double>(type: "double precision", nullable: false),
                    accepted = table.Column<bool>(type: "boolean", nullable: false),
                    rejections_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_candidates", x => x.id);
                    table.ForeignKey(
                        name: "FK_release_candidates_acquisitions_acquisition_id",
                        column: x => x.acquisition_id,
                        principalTable: "acquisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_release_candidates_indexer_configs_indexer_config_id",
                        column: x => x.indexer_config_id,
                        principalTable: "indexer_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_acquisition_import_hints_acquisition_id",
                table: "acquisition_import_hints",
                column: "acquisition_id");

            migrationBuilder.CreateIndex(
                name: "IX_acquisition_import_hints_source_path",
                table: "acquisition_import_hints",
                column: "source_path");

            migrationBuilder.CreateIndex(
                name: "IX_acquisitions_created_at",
                table: "acquisitions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_acquisitions_profile_id",
                table: "acquisitions",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "IX_acquisitions_request_history_id",
                table: "acquisitions",
                column: "request_history_id");

            migrationBuilder.CreateIndex(
                name: "IX_acquisitions_status",
                table: "acquisitions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_book_acquisition_profiles_is_default",
                table: "book_acquisition_profiles",
                column: "is_default");

            migrationBuilder.CreateIndex(
                name: "IX_book_acquisition_profiles_target_library_root_id",
                table: "book_acquisition_profiles",
                column: "target_library_root_id");

            migrationBuilder.CreateIndex(
                name: "IX_download_client_configs_kind_enabled",
                table: "download_client_configs",
                columns: new[] { "kind", "enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_download_client_credentials_download_client_config_id_crede~",
                table: "download_client_credentials",
                columns: new[] { "download_client_config_id", "credential_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_download_transfers_acquisition_id",
                table: "download_transfers",
                column: "acquisition_id");

            migrationBuilder.CreateIndex(
                name: "IX_download_transfers_download_client_config_id",
                table: "download_transfers",
                column: "download_client_config_id");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_configs_kind_enabled",
                table: "indexer_configs",
                columns: new[] { "kind", "enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_indexer_credentials_indexer_config_id_credential_key",
                table: "indexer_credentials",
                columns: new[] { "indexer_config_id", "credential_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_release_candidates_acquisition_id_score",
                table: "release_candidates",
                columns: new[] { "acquisition_id", "score" });

            migrationBuilder.CreateIndex(
                name: "IX_release_candidates_indexer_config_id",
                table: "release_candidates",
                column: "indexer_config_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acquisition_import_hints");

            migrationBuilder.DropTable(
                name: "download_client_credentials");

            migrationBuilder.DropTable(
                name: "download_transfers");

            migrationBuilder.DropTable(
                name: "indexer_credentials");

            migrationBuilder.DropTable(
                name: "release_candidates");

            migrationBuilder.DropTable(
                name: "download_client_configs");

            migrationBuilder.DropTable(
                name: "acquisitions");

            migrationBuilder.DropTable(
                name: "indexer_configs");

            migrationBuilder.DropTable(
                name: "book_acquisition_profiles");
        }
    }
}
