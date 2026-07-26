using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceJobTargetDedupeWithGraphKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_job_runs_pending_type_target",
                table: "job_runs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_job_runs_pending_type_target",
                table: "job_runs",
                columns: new[] { "type", "target_entity_id" },
                unique: true,
                filter: "status IN ('queued', 'running') AND target_entity_id IS NOT NULL");
        }
    }
}
