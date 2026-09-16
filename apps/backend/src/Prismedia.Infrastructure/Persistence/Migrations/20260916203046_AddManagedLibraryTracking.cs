using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedLibraryTracking : Migration
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
            migrationBuilder.CreateTable(
                name: "managed_holdings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    library_root_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    remote_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    item = table.Column<string>(type: "jsonb", nullable: false),
                    selections = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    problem = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true)
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
                name: "IX_managed_holdings_connection_id_kind_remote_id",
                table: "managed_holdings",
                columns: new[] { "connection_id", "kind", "remote_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_managed_holdings_library_root_id",
                table: "managed_holdings",
                column: "library_root_id");

            migrationBuilder.CreateIndex(
                name: "IX_managed_holdings_next_check_at",
                table: "managed_holdings",
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "managed_source_bindings");

            migrationBuilder.DropTable(
                name: "managed_holdings");
        }
    }
}
