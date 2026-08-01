using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Publishes an acquisition's terminal imported state only after its graph's required entity-readiness
/// predecessors have completed. Re-execution is safe because the acquisition store performs one idempotent
/// terminal update and clears the import claim in the same commit.
/// </summary>
[JobDefinition(JobType.AcquisitionFinalize)]
public sealed class AcquisitionFinalizeJobHandler(
    IAcquisitionStore acquisitions,
    IMonitorStore monitors) : IJobHandler {
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = AcquisitionFinalizeJobPayload.Parse(context.Job.PayloadJson);
        if (payload.UpgradeParentAcquisitionId is not null) {
            if (!string.IsNullOrWhiteSpace(payload.ReplacementBackupPath)
                && Directory.Exists(payload.ReplacementBackupPath)) {
                Directory.Delete(payload.ReplacementBackupPath, recursive: true);
            }
            await monitors.ResolveUpgradeChildAsync(
                payload.AcquisitionId,
                succeeded: true,
                cancellationToken);
            await acquisitions.DeleteAsync(payload.AcquisitionId, cancellationToken);
            await context.ReportProgressAsync(100, payload.Message ?? "Upgrade ready", cancellationToken);
            return;
        }

        await acquisitions.MarkImportedWithQualityAsync(
            payload.AcquisitionId,
            payload.OwnedQuality(),
            payload.Message,
            cancellationToken,
            payload.OwnedMediaQuality,
            payload.OwnedMediaRevision,
            payload.OwnedFormatScore);
        await context.ReportProgressAsync(100, payload.Message ?? "Import ready", cancellationToken);
    }
}
