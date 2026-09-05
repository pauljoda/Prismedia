using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequeueCompletedUsenetPayloadCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Older SABnzbd cleanup removed history while leaving completed files behind. Reopen
            // those exact recorded transfers once; runtime ownership and library checks govern deletion.
            migrationBuilder.Sql($"""
                UPDATE download_transfers AS transfer
                SET seeding_since = now(), updated_at = now()
                FROM acquisitions AS acquisition, download_client_configs AS client
                WHERE transfer.acquisition_id = acquisition.id
                  AND transfer.download_client_config_id = client.id
                  AND acquisition.status = '{AcquisitionStatus.Imported.ToCode()}'
                  AND client.kind = '{DownloadClientKind.Sabnzbd.ToCode()}'
                  AND transfer.progress = 1
                  AND transfer.content_path IS NOT NULL AND btrim(transfer.content_path) <> ''
                  AND transfer.seeding_since IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A scheduled cleanup may already have progressed; rolling back cannot safely unschedule it.
        }
    }
}
