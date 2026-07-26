using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyJobPriorityLane : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO job_graphs (
                    id,
                    origin,
                    status,
                    display_name,
                    root_run_id,
                    initiating_user_id,
                    root_entity_kind,
                    root_entity_id,
                    active_key,
                    cancellation_requested,
                    last_dispatched_at,
                    created_at,
                    updated_at,
                    finished_at)
                SELECT
                    run.id,
                    CASE WHEN run.lane = 'foreground-identify' THEN 'interactive' ELSE 'background' END,
                    run.status,
                    COALESCE(NULLIF(run.target_label, ''), run.type),
                    run.id,
                    NULL,
                    run.target_entity_kind,
                    run.target_entity_id,
                    NULL,
                    run.status = 'cancelled',
                    run.started_at,
                    run.created_at,
                    COALESCE(run.finished_at, run.started_at, run.created_at),
                    run.finished_at
                FROM job_runs AS run
                WHERE run.graph_id IS NULL
                ON CONFLICT (id) DO NOTHING;

                UPDATE job_runs
                SET graph_id = id,
                    node_key = COALESCE(node_key, 'legacy:' || id::text),
                    sequence = 0
                WHERE graph_id IS NULL;
                """);

            migrationBuilder.DropIndex(
                name: "IX_job_runs_status_lane_available_at_priority",
                table: "job_runs");

            migrationBuilder.DropColumn(
                name: "lane",
                table: "job_runs");

            migrationBuilder.DropColumn(
                name: "priority",
                table: "job_runs");

            migrationBuilder.CreateIndex(
                name: "IX_job_runs_status_available_at",
                table: "job_runs",
                columns: new[] { "status", "available_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_job_runs_status_available_at",
                table: "job_runs");

            migrationBuilder.AddColumn<string>(
                name: "lane",
                table: "job_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "priority",
                table: "job_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_job_runs_status_lane_available_at_priority",
                table: "job_runs",
                columns: new[] { "status", "lane", "available_at", "priority" });
        }
    }
}
