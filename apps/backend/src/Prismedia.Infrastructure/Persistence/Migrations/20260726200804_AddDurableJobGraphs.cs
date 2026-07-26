using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableJobGraphs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "graph_id",
                table: "job_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "importance",
                table: "job_runs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "required");

            migrationBuilder.AddColumn<string>(
                name: "node_key",
                table: "job_runs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_run_id",
                table: "job_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resource_class",
                table: "job_runs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "light");

            migrationBuilder.AddColumn<string>(
                name: "resource_key",
                table: "job_runs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "sequence",
                table: "job_runs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "job_graphs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    display_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    root_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    initiating_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    root_entity_kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    root_entity_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    active_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    cancellation_requested = table.Column<bool>(type: "boolean", nullable: false),
                    last_dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_graphs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_resource_states",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    max_concurrency = table.Column<int>(type: "integer", nullable: false),
                    minimum_start_interval_ms = table.Column<int>(type: "integer", nullable: false),
                    next_available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_resource_states", x => x.key);
                    table.CheckConstraint("ck_job_resource_states_concurrency", "max_concurrency > 0");
                    table.CheckConstraint("ck_job_resource_states_interval", "minimum_start_interval_ms >= 0");
                });

            migrationBuilder.CreateTable(
                name: "job_dependencies",
                columns: table => new
                {
                    predecessor_job_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    successor_job_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    graph_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_dependencies", x => new { x.predecessor_job_run_id, x.successor_job_run_id });
                    table.ForeignKey(
                        name: "FK_job_dependencies_job_graphs_graph_id",
                        column: x => x.graph_id,
                        principalTable: "job_graphs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_job_dependencies_job_runs_predecessor_job_run_id",
                        column: x => x.predecessor_job_run_id,
                        principalTable: "job_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_job_dependencies_job_runs_successor_job_run_id",
                        column: x => x.successor_job_run_id,
                        principalTable: "job_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_graph_signals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    graph_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_graph_signals", x => x.id);
                    table.ForeignKey(
                        name: "FK_job_graph_signals_job_graphs_graph_id",
                        column: x => x.graph_id,
                        principalTable: "job_graphs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_resource_leases",
                columns: table => new
                {
                    resource_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    job_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_resource_leases", x => new { x.resource_key, x.job_run_id });
                    table.ForeignKey(
                        name: "FK_job_resource_leases_job_resource_states_resource_key",
                        column: x => x.resource_key,
                        principalTable: "job_resource_states",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_job_resource_leases_job_runs_job_run_id",
                        column: x => x.job_run_id,
                        principalTable: "job_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_job_runs_graph_id_status_available_at_sequence",
                table: "job_runs",
                columns: new[] { "graph_id", "status", "available_at", "sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_job_runs_parent_run_id",
                table: "job_runs",
                column: "parent_run_id");

            migrationBuilder.CreateIndex(
                name: "ux_job_runs_graph_node_key",
                table: "job_runs",
                columns: new[] { "graph_id", "node_key" },
                unique: true,
                filter: "graph_id IS NOT NULL AND node_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_job_dependencies_graph_id_successor_job_run_id",
                table: "job_dependencies",
                columns: new[] { "graph_id", "successor_job_run_id" });

            migrationBuilder.CreateIndex(
                name: "IX_job_dependencies_successor_job_run_id",
                table: "job_dependencies",
                column: "successor_job_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_job_graph_signals_graph_id_key",
                table: "job_graph_signals",
                columns: new[] { "graph_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_job_graphs_origin_status_last_dispatched_at",
                table: "job_graphs",
                columns: new[] { "origin", "status", "last_dispatched_at" });

            migrationBuilder.CreateIndex(
                name: "ux_job_graphs_active_key",
                table: "job_graphs",
                column: "active_key",
                unique: true,
                filter: "active_key IS NOT NULL AND status IN ('queued', 'running', 'waiting')");

            migrationBuilder.CreateIndex(
                name: "IX_job_resource_leases_expires_at",
                table: "job_resource_leases",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_job_resource_leases_job_run_id",
                table: "job_resource_leases",
                column: "job_run_id");

            migrationBuilder.AddForeignKey(
                name: "FK_job_runs_job_graphs_graph_id",
                table: "job_runs",
                column: "graph_id",
                principalTable: "job_graphs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_job_runs_job_runs_parent_run_id",
                table: "job_runs",
                column: "parent_run_id",
                principalTable: "job_runs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_job_runs_job_graphs_graph_id",
                table: "job_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_job_runs_job_runs_parent_run_id",
                table: "job_runs");

            migrationBuilder.DropTable(
                name: "job_dependencies");

            migrationBuilder.DropTable(
                name: "job_graph_signals");

            migrationBuilder.DropTable(
                name: "job_resource_leases");

            migrationBuilder.DropTable(
                name: "job_graphs");

            migrationBuilder.DropTable(
                name: "job_resource_states");

            migrationBuilder.DropIndex(
                name: "IX_job_runs_graph_id_status_available_at_sequence",
                table: "job_runs");

            migrationBuilder.DropIndex(
                name: "IX_job_runs_parent_run_id",
                table: "job_runs");

            migrationBuilder.DropIndex(
                name: "ux_job_runs_graph_node_key",
                table: "job_runs");

            migrationBuilder.DropColumn(
                name: "graph_id",
                table: "job_runs");

            migrationBuilder.DropColumn(
                name: "importance",
                table: "job_runs");

            migrationBuilder.DropColumn(
                name: "node_key",
                table: "job_runs");

            migrationBuilder.DropColumn(
                name: "parent_run_id",
                table: "job_runs");

            migrationBuilder.DropColumn(
                name: "resource_class",
                table: "job_runs");

            migrationBuilder.DropColumn(
                name: "resource_key",
                table: "job_runs");

            migrationBuilder.DropColumn(
                name: "sequence",
                table: "job_runs");
        }
    }
}
