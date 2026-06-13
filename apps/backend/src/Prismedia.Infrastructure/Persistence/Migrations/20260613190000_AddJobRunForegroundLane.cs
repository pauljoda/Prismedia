using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobRunForegroundLane : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_job_runs_status_available_at_priority",
                table: "job_runs");

            migrationBuilder.AddColumn<string>(
                name: "lane",
                table: "job_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_job_runs_status_lane_available_at_priority",
                table: "job_runs",
                columns: new[] { "status", "lane", "available_at", "priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_job_runs_status_lane_available_at_priority",
                table: "job_runs");

            migrationBuilder.DropColumn(
                name: "lane",
                table: "job_runs");

            migrationBuilder.CreateIndex(
                name: "IX_job_runs_status_available_at_priority",
                table: "job_runs",
                columns: new[] { "status", "available_at", "priority" });
        }
    }
}
