using Microsoft.EntityFrameworkCore.Migrations;
using Prismedia.Domain.Entities;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KeepReleaseGatesWaiting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "release_date_metadata_unavailable",
                table: "acquisitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql($"""
                UPDATE monitors AS monitor
                SET last_searched_at = NULL
                FROM acquisitions AS acquisition
                WHERE monitor.acquisition_id = acquisition.id
                  AND acquisition.status = '{AcquisitionStatus.ManualSearchRequired.ToCode()}';

                UPDATE acquisitions
                SET status = '{AcquisitionStatus.WaitingForRelease.ToCode()}',
                    release_date_metadata_unavailable = TRUE
                WHERE status = '{AcquisitionStatus.ManualSearchRequired.ToCode()}';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE acquisitions
                SET status = '{AcquisitionStatus.ManualSearchRequired.ToCode()}'
                WHERE status = '{AcquisitionStatus.WaitingForRelease.ToCode()}'
                  AND release_date_metadata_unavailable = TRUE;
                """);

            migrationBuilder.DropColumn(
                name: "release_date_metadata_unavailable",
                table: "acquisitions");
        }
    }
}
