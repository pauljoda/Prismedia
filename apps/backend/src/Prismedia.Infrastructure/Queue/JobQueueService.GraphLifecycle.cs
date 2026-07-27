using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Queue;

public sealed partial class JobQueueService {
    /// <summary>Projects node, dependency, and signal state onto the owning durable graph.</summary>
    private async Task ReconcileGraphStateAsync(Guid? graphId, CancellationToken cancellationToken) {
        if (graphId is not { } id) return;

        await using var mutation = await JobGraphMutationScope.AcquireAsync(_db, id, cancellationToken);
        if (mutation is null) return;
        var graph = mutation.Graph;

        var runs = await _db.JobRuns
            .Where(run => run.GraphId == id)
            .ToArrayAsync(cancellationToken);
        var requiredFailures = runs
            .Where(run => run.Status == JobRunStatus.Failed && run.Importance == JobNodeImportance.Required)
            .Select(run => run.Id)
            .ToHashSet();
        if (requiredFailures.Count > 0) {
            var dependencies = await _db.JobDependencies
                .Where(edge => edge.GraphId == id)
                .ToArrayAsync(cancellationToken);
            var blocked = new HashSet<Guid>(requiredFailures);
            var changed = true;
            while (changed) {
                changed = false;
                foreach (var edge in dependencies.Where(edge => blocked.Contains(edge.PredecessorJobRunId))) {
                    changed |= blocked.Add(edge.SuccessorJobRunId);
                }
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var descendant in runs.Where(run =>
                         blocked.Contains(run.Id)
                         && !requiredFailures.Contains(run.Id)
                         && run.Status == JobRunStatus.Queued)) {
                descendant.Status = JobRunStatus.Cancelled;
                descendant.Message = "Skipped because a required dependency failed.";
                descendant.FinishedAt = now;
            }
        }

        var hasActiveNodes = runs.Any(run => run.Status is JobRunStatus.Queued or JobRunStatus.Running);
        var hasOpenSignals = await _db.JobGraphSignals.AnyAsync(
            signal => signal.GraphId == id && signal.ResolvedAt == null && signal.CancelledAt == null,
            cancellationToken);
        var nowUtc = DateTimeOffset.UtcNow;
        if (hasActiveNodes) {
            graph.Status = runs.Any(run => run.Status == JobRunStatus.Running)
                ? JobGraphStatus.Running
                : JobGraphStatus.Queued;
            graph.FinishedAt = null;
        } else if (hasOpenSignals) {
            graph.Status = JobGraphStatus.Waiting;
            graph.FinishedAt = null;
        } else if (graph.CancellationRequested || runs.All(run => run.Status == JobRunStatus.Cancelled)) {
            graph.Status = JobGraphStatus.Cancelled;
            graph.FinishedAt = nowUtc;
        } else if (runs.Any(run => run.Status == JobRunStatus.Failed && run.Importance == JobNodeImportance.Required)) {
            graph.Status = JobGraphStatus.Failed;
            graph.FinishedAt = nowUtc;
        } else if (runs.Any(run => run.Status == JobRunStatus.Failed)) {
            graph.Status = JobGraphStatus.CompletedWithWarnings;
            graph.FinishedAt = nowUtc;
        } else {
            graph.Status = JobGraphStatus.Completed;
            graph.FinishedAt = nowUtc;
        }

        if (graph.Status == JobGraphStatus.Failed) {
            var linkedImports = await _db.Acquisitions
                .Where(acquisition => acquisition.JobGraphId == id && acquisition.Status == AcquisitionStatus.Importing)
                .ToArrayAsync(cancellationToken);
            foreach (var acquisition in linkedImports) {
                acquisition.Status = AcquisitionStatus.Failed;
                acquisition.StatusMessage = "Required post-import entity processing failed; retry the import to resume its exact checkpoint.";
                acquisition.UpdatedAt = nowUtc;
            }
        }

        graph.UpdatedAt = nowUtc;
        await _db.SaveChangesAsync(cancellationToken);
        await mutation.CommitAsync(cancellationToken);
    }
}
