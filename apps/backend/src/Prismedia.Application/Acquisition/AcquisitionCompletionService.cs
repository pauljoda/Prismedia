using Microsoft.Extensions.Logging;
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
    IJobQueueService jobs,
    IJobGraphService? graphs = null,
    ILogger<AcquisitionCompletionService>? logger = null) {
    public async Task ScheduleAsync(
        Guid acquisitionId,
        CancellationToken cancellationToken,
        JobGraphOrigin fallbackOrigin = JobGraphOrigin.Interactive) {
        var detail = await acquisitions.GetAsync(acquisitionId, cancellationToken);
        if (detail?.Summary.Status != AcquisitionStatus.Downloaded) {
            return;
        }

        var isUpgrade = await acquisitions.GetUpgradeOwnedQualityAsync(acquisitionId, cancellationToken) is not null;
        var jobType = CompletionJobType(detail.Summary.Kind, isUpgrade);
        var request = new EnqueueJobRequest(
                jobType,
                PayloadJson: AcquisitionJobPayload.Serialize(acquisitionId),
                TargetEntityKind: detail.Summary.Kind.ToCode(),
                TargetEntityId: acquisitionId.ToString(),
                TargetLabel: isUpgrade ? "Replace with reviewed release" : "Import completed acquisition",
                Origin: fallbackOrigin,
                GraphRootEntityKind: detail.Summary.Kind.ToCode(),
                GraphRootEntityId: detail.Summary.EntityId?.ToString());
        if (graphs is not null && detail.Summary.JobGraphId is { } graphId) {
            var node = new GraphJobNodeRequest(
                $"{jobType.ToCode()}:{acquisitionId}",
                request,
                Importance: JobNodeImportance.Required,
                ResourceClass: JobDefinitionRegistry.ResourceClass(jobType),
                ResourceKey: detail.Summary.EntityId is { } entityId
                    ? JobResourceKeys.Entity(entityId.ToString())
                    : null);
            var signalKey = AcquisitionGraphSignals.ExternalTransfer(acquisitionId);
            try {
                var graph = await graphs.GetAsync(graphId, cancellationToken);
                if (graph?.Graph.Status is JobGraphStatus.Queued or JobGraphStatus.Running or JobGraphStatus.Waiting) {
                    var openSignal = graph.Signals.FirstOrDefault(signal =>
                        (signal.Key == signalKey || signal.Key == AcquisitionGraphSignals.Review(acquisitionId))
                        && signal.ResolvedAt is null
                        && signal.CancelledAt is null);
                    if (openSignal is not null) {
                        await graphs.ResolveSignalAsync(graphId, openSignal.Key, [node], cancellationToken);
                    } else {
                        await graphs.AppendNodeAsync(graphId, node, cancellationToken);
                    }
                    return;
                }
            } catch (InvalidOperationException ex) {
                // A download is already durable at this boundary. A stale, terminal, or concurrently
                // changing review graph must not strand those bytes in Downloaded forever; the queue's
                // type+target guard makes the fresh background workflow idempotent.
                logger?.LogWarning(
                    ex,
                    "Could not continue acquisition {AcquisitionId} in graph {GraphId}; starting a fresh completion workflow",
                    acquisitionId,
                    graphId);
            }
        }

        var job = await jobs.EnqueueAsync(request, cancellationToken);
        if (job.GraphId is { } recoveryGraphId) {
            await acquisitions.SetJobGraphIdAsync(acquisitionId, recoveryGraphId, cancellationToken);
        }
    }

    /// <summary>
    /// Entity definitions decide whether an upgrade can atomically replace one owned file. Multi-file
    /// and structural units continue through their family import engine's durable placement plan.
    /// </summary>
    public static JobType CompletionJobType(EntityKind kind, bool isUpgrade) =>
        isUpgrade && EntityKindRegistry.Describe(kind).UpgradeMode != EntityUpgradeMode.Import
            ? JobType.AcquisitionUpgradeReplace
            : JobType.AcquisitionImport;
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
