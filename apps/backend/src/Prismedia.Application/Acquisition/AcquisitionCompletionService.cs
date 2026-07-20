using Prismedia.Application.Jobs;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Shared post-transfer handoff for remote download clients and browser uploads. Both sources publish a
/// Downloaded acquisition with a content path; this service routes that same ticket to import or replacement.
/// </summary>
public sealed class AcquisitionCompletionService(
    IAcquisitionStore acquisitions,
    IJobQueueService jobs) {
    public async Task ScheduleAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var detail = await acquisitions.GetAsync(acquisitionId, cancellationToken);
        if (detail?.Summary.Status != AcquisitionStatus.Downloaded) {
            return;
        }

        var isUpgrade = await acquisitions.GetUpgradeOwnedQualityAsync(acquisitionId, cancellationToken) is not null;
        await jobs.EnqueueAsync(
            new EnqueueJobRequest(
                isUpgrade ? JobType.AcquisitionUpgradeReplace : JobType.AcquisitionImport,
                PayloadJson: AcquisitionJobPayload.Serialize(acquisitionId),
                TargetEntityId: acquisitionId.ToString(),
                TargetLabel: isUpgrade ? "Replace with upgrade" : "Import completed acquisition",
                Priority: JobPriorities.InteractiveRequest),
            cancellationToken);
    }
}

/// <summary>Accepts local bytes through the upload adapter and joins the shared completed-acquisition flow.</summary>
public sealed class AcquisitionUploadService(
    IAcquisitionUploadStore uploadState,
    IAcquisitionUploadStorage uploads,
    IAcquisitionStore acquisitions,
    AcquisitionCompletionService completion) {
    public async Task<AcquisitionDetail> UploadAsync(
        Guid entityId,
        IReadOnlyList<AcquisitionUploadItem> items,
        CancellationToken cancellationToken) {
        var acquisitionId = await uploadState.PrepareAsync(entityId, cancellationToken)
            ?? throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                "This item is not ready to accept a manual upload.");
        var staged = await uploads.StageAsync(acquisitionId, items, cancellationToken);
        try {
            if (!await uploadState.CompleteAsync(acquisitionId, staged, cancellationToken)) {
                throw new AcquisitionConfigurationException(
                    ApiProblemCodes.AcquisitionInvalid,
                    "The acquisition changed before the upload could be imported. Refresh and try again.");
            }
            await completion.ScheduleAsync(acquisitionId, cancellationToken);
            return await acquisitions.GetAsync(acquisitionId, cancellationToken)
                ?? throw new AcquisitionConfigurationException(
                    ApiProblemCodes.AcquisitionNotFound,
                    "The uploaded acquisition no longer exists.");
        } catch {
            await uploads.DeleteAsync(staged.ClientItemId, CancellationToken.None);
            throw;
        }
    }
}
