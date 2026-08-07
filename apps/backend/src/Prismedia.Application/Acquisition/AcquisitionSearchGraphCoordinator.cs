using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Hands an acquisition from a completed release-review wait to its next search graph. A review signal
/// represents the candidates from one completed search; deliberately searching again supersedes that wait
/// before a new graph claims the same acquisition-scoped active key.
/// </summary>
internal sealed class AcquisitionSearchGraphCoordinator(
    IAcquisitionLifecycleStore acquisitions,
    IJobGraphService? graphs) {
    /// <summary>
    /// Closes the prior graph only when it is durably waiting on this acquisition's release review. Running
    /// or queued search graphs remain intact so ordinary at-least-once publication still deduplicates.
    /// </summary>
    public async Task<Guid?> PrepareAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var priorGraphId = await acquisitions.GetJobGraphIdAsync(acquisitionId, cancellationToken);
        if (priorGraphId is not { } graphId || graphs is null) {
            return priorGraphId;
        }

        var detail = await graphs.GetAsync(graphId, cancellationToken);
        if (detail?.Graph.Status != JobGraphStatus.Waiting) {
            return priorGraphId;
        }

        var reviewKey = AcquisitionGraphSignals.Review(acquisitionId);
        var review = detail.Signals.FirstOrDefault(signal =>
            signal.Key == reviewKey && signal.ResolvedAt is null && signal.CancelledAt is null);
        if (review is not null) {
            await graphs.ResolveSignalAsync(graphId, reviewKey, [], cancellationToken);
        }

        return priorGraphId;
    }

    /// <summary>Atomically links the newly published search graph over the graph observed by PrepareAsync.</summary>
    public async Task LinkAsync(
        Guid acquisitionId,
        Guid? priorGraphId,
        Guid newGraphId,
        CancellationToken cancellationToken) {
        if (priorGraphId == newGraphId) {
            return;
        }

        if (await acquisitions.TryRelinkJobGraphIdAsync(
                acquisitionId,
                priorGraphId,
                newGraphId,
                cancellationToken)) {
            return;
        }

        var currentGraphId = await acquisitions.GetJobGraphIdAsync(acquisitionId, cancellationToken);
        if (currentGraphId != newGraphId) {
            throw new InvalidOperationException(
                $"Acquisition '{acquisitionId}' moved from graph '{priorGraphId}' to '{currentGraphId}' while publishing search graph '{newGraphId}'.");
        }
    }
}
