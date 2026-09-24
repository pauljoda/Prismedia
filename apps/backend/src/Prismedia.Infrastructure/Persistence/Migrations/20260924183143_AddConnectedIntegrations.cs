using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectedIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Older untargeted scans had no shared resource. Fence queued scans before any new
            // connected reconciliation can claim work; running jobs finish under their existing lease.
            migrationBuilder.Sql($"""
                INSERT INTO job_resource_states (key, max_concurrency, minimum_start_interval_ms, next_available_at, updated_at)
                SELECT '{JobResourceKeys.LibraryScan}', 1, 0, '-infinity'::timestamptz, now()
                WHERE EXISTS (SELECT 1 FROM job_runs WHERE type = '{JobType.ScanLibrary.ToCode()}' AND status = '{JobRunStatus.Queued.ToCode()}')
                ON CONFLICT (key) DO NOTHING;
                UPDATE job_runs SET resource_key = '{JobResourceKeys.LibraryScan}'
                WHERE type = '{JobType.ScanLibrary.ToCode()}' AND status = '{JobRunStatus.Queued.ToCode()}';
                """);

            migrationBuilder.AddColumn<int>(
                name: "protection_version",
                table: "provider_credentials",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "preserve_container",
                table: "gallery_details",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_library_archived",
                table: "entities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "entity_metadata_fields",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(7,6)", precision: 7, scale: 6, nullable: true),
                    is_cleared = table.Column<bool>(type: "boolean", nullable: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_metadata_fields", x => new { x.entity_id, x.field });
                    table.ForeignKey(
                        name: "FK_entity_metadata_fields_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "integration_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plugin_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    enabled_capabilities = table.Column<string>(type: "jsonb", nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false),
                    protected_secrets = table.Column<string>(type: "jsonb", nullable: false),
                    effective_capabilities = table.Column<string>(type: "jsonb", nullable: false),
                    remote_instance_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    has_persistent_remote_identity = table.Column<bool>(type: "boolean", nullable: false),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_connections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plugin_invocation_states",
                columns: table => new
                {
                    plugin_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    next_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_invocation_states", x => x.plugin_id);
                });

            migrationBuilder.CreateTable(
                name: "external_library_mounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    library_root_id = table.Column<Guid>(type: "uuid", nullable: false),
                    remote_root_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    remote_path = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    local_path = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_library_mounts", x => x.id);
                    table.ForeignKey(
                        name: "FK_external_library_mounts_integration_connections_connection_~",
                        column: x => x.connection_id,
                        principalTable: "integration_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_library_mounts_library_roots_library_root_id",
                        column: x => x.library_root_id,
                        principalTable: "library_roots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fulfillment_reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    book_rendition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    external_ids = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fulfillment_reservations", x => x.id);
                    table.ForeignKey(
                        name: "FK_fulfillment_reservations_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fulfillment_reservations_integration_connections_connection~",
                        column: x => x.connection_id,
                        principalTable: "integration_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "integration_transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    phase = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    state = table.Column<string>(type: "jsonb", nullable: false),
                    protected_plan = table.Column<string>(type: "text", nullable: false),
                    active_ownership_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_transfers", x => x.id);
                    table.ForeignKey(
                        name: "FK_integration_transfers_integration_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "integration_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "managed_holdings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    library_root_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    book_rendition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    remote_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    item = table.Column<string>(type: "jsonb", nullable: false),
                    selections = table.Column<string>(type: "jsonb", nullable: false),
                    targets = table.Column<string>(type: "jsonb", nullable: false),
                    people_enrichment_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    people_enrichment_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    problem = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    release_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    release_request = table.Column<string>(type: "jsonb", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_bindings = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_holdings", x => x.id);
                    table.ForeignKey(
                        name: "FK_managed_holdings_integration_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "integration_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_managed_holdings_library_roots_library_root_id",
                        column: x => x.library_root_id,
                        principalTable: "library_roots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "managed_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    library_root_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    phase = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    state = table.Column<string>(type: "jsonb", nullable: false),
                    plan = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    problem = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_requests", x => x.id);
                    table.ForeignKey(
                        name: "FK_managed_requests_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_managed_requests_integration_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "integration_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_managed_requests_library_roots_library_root_id",
                        column: x => x.library_root_id,
                        principalTable: "library_roots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plugin_invocation_leases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plugin_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_invocation_leases", x => x.id);
                    table.ForeignKey(
                        name: "FK_plugin_invocation_leases_plugin_invocation_states_plugin_id",
                        column: x => x.plugin_id,
                        principalTable: "plugin_invocation_states",
                        principalColumn: "plugin_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "managed_controls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    holding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    active_holding_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "jsonb", nullable: false),
                    plan = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    problem = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_controls", x => x.id);
                    table.ForeignKey(
                        name: "FK_managed_controls_integration_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "integration_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_managed_controls_managed_holdings_holding_id",
                        column: x => x.holding_id,
                        principalTable: "managed_holdings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "managed_source_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    holding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    remote_target_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    season_number = table.Column<int>(type: "integer", nullable: true),
                    episode_number = table.Column<int>(type: "integer", nullable: true),
                    absolute_number = table.Column<int>(type: "integer", nullable: true),
                    issue_label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    remote_file_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    local_path = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    written_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_source_bindings", x => x.id);
                    table.ForeignKey(
                        name: "FK_managed_source_bindings_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_managed_source_bindings_entity_files_source_file_id",
                        column: x => x.source_file_id,
                        principalTable: "entity_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_managed_source_bindings_managed_holdings_holding_id",
                        column: x => x.holding_id,
                        principalTable: "managed_holdings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_external_library_mounts_connection_id_remote_root_id",
                table: "external_library_mounts",
                columns: new[] { "connection_id", "remote_root_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_library_mounts_library_root_id",
                table: "external_library_mounts",
                column: "library_root_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fulfillment_reservations_connection_id",
                table: "fulfillment_reservations",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "IX_fulfillment_reservations_entity_id",
                table: "fulfillment_reservations",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_fulfillment_reservations_owner_id_owner_kind_entity_id_book~",
                table: "fulfillment_reservations",
                columns: new[] { "owner_id", "owner_kind", "entity_id", "book_rendition" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_integration_connections_plugin_id",
                table: "integration_connections",
                column: "plugin_id");

            migrationBuilder.CreateIndex(
                name: "IX_integration_transfers_active_ownership_key",
                table: "integration_transfers",
                column: "active_ownership_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_transfers_connection_id",
                table: "integration_transfers",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "IX_integration_transfers_created_at",
                table: "integration_transfers",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_managed_controls_active_holding_id",
                table: "managed_controls",
                column: "active_holding_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_managed_controls_connection_id",
                table: "managed_controls",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "IX_managed_controls_holding_id_created_at",
                table: "managed_controls",
                columns: new[] { "holding_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_managed_controls_next_check_at",
                table: "managed_controls",
                column: "next_check_at");

            migrationBuilder.CreateIndex(
                name: "IX_managed_holdings_connection_id_kind_remote_id_book_rendition",
                table: "managed_holdings",
                columns: new[] { "connection_id", "kind", "remote_id", "book_rendition" },
                unique: true,
                filter: "released_at IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_managed_holdings_library_root_id",
                table: "managed_holdings",
                column: "library_root_id");

            migrationBuilder.CreateIndex(
                name: "IX_managed_holdings_next_check_at",
                table: "managed_holdings",
                column: "next_check_at");

            migrationBuilder.CreateIndex(
                name: "IX_managed_holdings_release_operation_id",
                table: "managed_holdings",
                column: "release_operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_managed_requests_connection_id_created_at",
                table: "managed_requests",
                columns: new[] { "connection_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_managed_requests_entity_id",
                table: "managed_requests",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_managed_requests_library_root_id",
                table: "managed_requests",
                column: "library_root_id");

            migrationBuilder.CreateIndex(
                name: "IX_managed_requests_next_check_at",
                table: "managed_requests",
                column: "next_check_at");

            migrationBuilder.CreateIndex(
                name: "IX_managed_source_bindings_entity_id",
                table: "managed_source_bindings",
                column: "entity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_managed_source_bindings_holding_id_remote_target_id",
                table: "managed_source_bindings",
                columns: new[] { "holding_id", "remote_target_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_managed_source_bindings_source_file_id",
                table: "managed_source_bindings",
                column: "source_file_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plugin_invocation_leases_plugin_id_expires_at",
                table: "plugin_invocation_leases",
                columns: new[] { "plugin_id", "expires_at" });

            migrationBuilder.Sql(OwnershipGuards);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RemoveOwnershipGuards);

            migrationBuilder.DropTable(
                name: "entity_metadata_fields");

            migrationBuilder.DropTable(
                name: "external_library_mounts");

            migrationBuilder.DropTable(
                name: "fulfillment_reservations");

            migrationBuilder.DropTable(
                name: "integration_transfers");

            migrationBuilder.DropTable(
                name: "managed_controls");

            migrationBuilder.DropTable(
                name: "managed_requests");

            migrationBuilder.DropTable(
                name: "managed_source_bindings");

            migrationBuilder.DropTable(
                name: "plugin_invocation_leases");

            migrationBuilder.DropTable(
                name: "managed_holdings");

            migrationBuilder.DropTable(
                name: "plugin_invocation_states");

            migrationBuilder.DropTable(
                name: "integration_connections");

            migrationBuilder.DropColumn(
                name: "protection_version",
                table: "provider_credentials");

            migrationBuilder.DropColumn(
                name: "preserve_container",
                table: "gallery_details");

            migrationBuilder.DropColumn(
                name: "is_library_archived",
                table: "entities");
        }
    }
}
