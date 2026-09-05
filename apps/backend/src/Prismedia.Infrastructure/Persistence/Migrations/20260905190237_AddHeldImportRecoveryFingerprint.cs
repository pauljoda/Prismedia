using Microsoft.EntityFrameworkCore.Migrations;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHeldImportRecoveryFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "import_manual_review",
                table: "acquisitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "import_recovery_fingerprint",
                table: "acquisitions",
                type: "text",
                nullable: true);

            // Existing explicit reviews remain authoritative, including holds whose original job history
            // has expired. Only a recorded automatic claim proves that unattended remapping is appropriate.
            migrationBuilder.Sql($"""
                UPDATE acquisitions AS acquisition
                SET import_manual_review = TRUE
                WHERE acquisition.status = '{AcquisitionStatus.ManualImportRequired.ToCode()}'
                  AND NOT EXISTS (
                    SELECT 1 FROM job_runs AS job
                    WHERE job.id = acquisition.import_claim_job_id
                      AND job.type = '{JobType.AcquisitionImport.ToCode()}'
                      AND job.payload_json->>'{nameof(AcquisitionJobPayload.AcquisitionId)}' = acquisition.id::text
                      AND COALESCE(job.payload_json->>'{nameof(AcquisitionJobPayload.ManualRetry)}', 'false') = 'false'
                      AND COALESCE(job.payload_json->>'{nameof(AcquisitionJobPayload.AllowFormatChange)}', 'false') = 'false'
                      AND (job.payload_json->'{nameof(AcquisitionJobPayload.ManualFileMappings)}' IS NULL
                        OR job.payload_json->'{nameof(AcquisitionJobPayload.ManualFileMappings)}' IN ('null'::jsonb, '[]'::jsonb))
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "import_manual_review",
                table: "acquisitions");

            migrationBuilder.DropColumn(
                name: "import_recovery_fingerprint",
                table: "acquisitions");
        }
    }
}
