using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

public sealed partial class AcquisitionService {
    /// <summary>
    /// Re-runs the release search for an existing acquisition on demand (the manual counterpart to monitoring).
    /// Enqueues the standard <see cref="JobType.AcquisitionSearch"/> — deduped per acquisition, and the handler
    /// re-checks that the acquisition is still searchable — so it can't disturb an in-flight grab. An explicit
    /// user action may revive Cancelled by claiming Searching before enqueue; stale queued jobs cannot. Returns
    /// the acquisition, or null when it no longer exists.
    /// </summary>
    public async Task<AcquisitionDetail?> ReSearchAsync(
        Guid id,
        CancellationToken cancellationToken,
        string? customQuery = null) {
        var detail = await store.GetAsync(id, cancellationToken);
        if (detail is null) {
            return null;
        }

        var explicitRevival = detail.Summary.Status == AcquisitionStatus.Cancelled;
        return await ScheduleSearchAsync(
            detail,
            manualReview: true,
            explicitRevival,
            customQuery,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> EnsureOpenEntitySearchAsync(
        Guid entityId,
        BookRendition? bookRendition,
        CancellationToken cancellationToken) {
        var detail = (await store.ListForEntityAsync(entityId, cancellationToken))
            .FirstOrDefault(candidate =>
                candidate.Summary.BookRendition == bookRendition
                && candidate.Summary.Status is not AcquisitionStatus.Imported and not AcquisitionStatus.Cancelled);
        if (detail is null) {
            return false;
        }

        if (detail.Summary.Status is AcquisitionStatus.Pending or AcquisitionStatus.AwaitingSelection) {
            var refreshed = await ScheduleSearchAsync(
                detail,
                manualReview: false,
                explicitRevival: false,
                customQuery: null,
                cancellationToken);
            return refreshed?.Summary.Status is not AcquisitionStatus.Imported and not AcquisitionStatus.Cancelled;
        }

        return true;
    }

    /// <summary>Claims and publishes one search while preserving the caller's automatic/manual intent.</summary>
    private async Task<AcquisitionDetail?> ScheduleSearchAsync(
        AcquisitionDetail detail,
        bool manualReview,
        bool explicitRevival,
        string? customQuery,
        CancellationToken cancellationToken) {
        if (!explicitRevival && !AcquisitionSearchJobHandler.CanScheduleSearch(detail.Summary.Status)) {
            return detail;
        }

        await EnsureImportCheckpointCanBeSupersededAsync(detail, cancellationToken);
        if (!await store.TryTransitionStatusAsync(
                detail.Summary.Id,
                [detail.Summary.Status],
                AcquisitionStatus.Searching,
                null,
                cancellationToken)) {
            return await store.GetAsync(detail.Summary.Id, cancellationToken);
        }

        await queue.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.AcquisitionSearch,
                PayloadJson: AcquisitionJobPayload.Serialize(
                    detail.Summary.Id,
                    manualReview: manualReview,
                    customQuery: customQuery),
                TargetEntityId: detail.Summary.Id.ToString(),
                TargetLabel: detail.Summary.Title,
                Priority: JobPriorities.InteractiveRequest,
                Lane: JobRunLane.ForegroundIdentify),
            cancellationToken);
        return await store.GetAsync(detail.Summary.Id, cancellationToken);
    }
}
