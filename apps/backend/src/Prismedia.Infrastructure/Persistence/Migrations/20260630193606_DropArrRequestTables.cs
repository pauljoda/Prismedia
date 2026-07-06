using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropArrRequestTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_acquisitions_request_history_request_history_id",
                table: "acquisitions");

            // The new first-party acquisition model no longer maps the upstream request-service tables,
            // but existing deployments may still contain useful Radarr/Sonarr/Lidarr request settings and
            // history. Leave those legacy tables in place as unmanaged data instead of dropping them.
            migrationBuilder.DropIndex(
                name: "IX_acquisitions_request_history_id",
                table: "acquisitions");

            migrationBuilder.DropColumn(
                name: "request_history_id",
                table: "acquisitions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "request_history_id",
                table: "acquisitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_acquisitions_request_history_id",
                table: "acquisitions",
                column: "request_history_id");

            migrationBuilder.AddForeignKey(
                name: "FK_acquisitions_request_history_request_history_id",
                table: "acquisitions",
                column: "request_history_id",
                principalTable: "request_history",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
