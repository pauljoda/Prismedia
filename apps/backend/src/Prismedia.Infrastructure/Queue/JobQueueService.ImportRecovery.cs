using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Queue;

public sealed partial class JobQueueService {
    /// <summary>
    /// Resumes durable acquisition checkpoints whose owning graph reached a terminal state without an
    /// acquisition finalizer. Imports without a checkpoint fail visibly instead of remaining Importing
    /// forever; their files and diagnostic state are preserved for explicit review.
    /// </summary>
    private async Task RecoverStrandedImportsAsync(
        DateTimeOffset staleBefore,
        CancellationToken cancellationToken) {
        var terminalStatuses = new[] {
            JobGraphStatus.Completed,
            JobGraphStatus.CompletedWithWarnings,
            JobGraphStatus.Failed,
            JobGraphStatus.Cancelled,
        };
        var candidates = await _db.Acquisitions
            .Where(acquisition =>
                acquisition.Status == AcquisitionStatus.Importing
                && acquisition.UpdatedAt <= staleBefore
                && (acquisition.JobGraphId == null
                    || _db.JobGraphs.Any(graph =>
                        graph.Id == acquisition.JobGraphId
                        && terminalStatuses.Contains(graph.Status))))
            .ToArrayAsync(cancellationToken);
        if (candidates.Length == 0) {
            return;
        }

        var targetIds = candidates.Select(acquisition => acquisition.Id.ToString()).ToArray();
        var activeTargets = (await _db.JobRuns.AsNoTracking()
                .Where(run =>
                    run.Type == JobType.AcquisitionImport
                    && run.TargetEntityId != null
                    && targetIds.Contains(run.TargetEntityId)
                    && (run.Status == JobRunStatus.Queued || run.Status == JobRunStatus.Running))
                .Select(run => run.TargetEntityId!)
                .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var stranded = candidates
            .Where(acquisition => !activeTargets.Contains(acquisition.Id.ToString()))
            .ToArray();
        if (stranded.Length == 0) {
            return;
        }

        var graphIds = stranded
            .Where(acquisition => acquisition.JobGraphId is not null)
            .Select(acquisition => acquisition.JobGraphId!.Value)
            .Distinct()
            .ToArray();
        var graphStatuses = await _db.JobGraphs.AsNoTracking()
            .Where(graph => graphIds.Contains(graph.Id))
            .ToDictionaryAsync(graph => graph.Id, graph => graph.Status, cancellationToken);

        const string resumeMessage =
            "The prior post-import graph ended before finalization. Prismedia is resuming the saved import checkpoint automatically.";
        const string interruptedMessage =
            "The import graph failed or was cancelled before finalization. The saved checkpoint is ready for an explicit retry.";
        const string reviewMessage =
            "The import workflow ended before a resumable checkpoint was recorded. Review this acquisition and retry or reacquire the release.";
        var resumable = new List<AcquisitionRow>();
        var now = DateTimeOffset.UtcNow;
        foreach (var acquisition in stranded) {
            acquisition.Status = AcquisitionStatus.Failed;
            var graphCompleted = acquisition.JobGraphId is { } graphId
                && graphStatuses.TryGetValue(graphId, out var graphStatus)
                && graphStatus is JobGraphStatus.Completed or JobGraphStatus.CompletedWithWarnings;
            acquisition.StatusMessage = acquisition.ImportCheckpointJson is null
                ? reviewMessage
                : graphCompleted
                    ? resumeMessage
                    : interruptedMessage;
            acquisition.UpdatedAt = now;
            if (acquisition.ImportCheckpointJson is not null && graphCompleted) {
                resumable.Add(acquisition);
            }
        }
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var acquisition in resumable) {
            var retry = await EnqueueAsync(
                new EnqueueJobRequest(
                    JobType.AcquisitionImport,
                    PayloadJson: AcquisitionJobPayload.Serialize(
                        acquisition.Id,
                        allowFormatChange: false,
                        manualRetry: true),
                    TargetEntityId: acquisition.Id.ToString(),
                    TargetLabel: acquisition.Title,
                    Origin: JobGraphOrigin.Interactive,
                    GraphRootEntityKind: acquisition.Kind.ToCode(),
                    GraphRootEntityId: acquisition.EntityId?.ToString()),
                cancellationToken);
            acquisition.JobGraphId = retry.GraphId;
        }
        await _db.SaveChangesAsync(cancellationToken);
    }
}
