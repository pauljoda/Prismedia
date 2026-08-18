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

        // One grouped aggregate replaces materializing and tracking every node row on every
        // transition — on a scan graph with thousands of downstream nodes that made each node's
        // completion cost proportional to the whole graph.
        var counts = await _db.JobRuns
            .Where(run => run.GraphId == id)
            .GroupBy(run => 1)
            .Select(group => new {
                Total = group.Count(),
                Running = group.Count(run => run.Status == JobRunStatus.Running),
                Queued = group.Count(run => run.Status == JobRunStatus.Queued),
                Cancelled = group.Count(run => run.Status == JobRunStatus.Cancelled),
                Failed = group.Count(run => run.Status == JobRunStatus.Failed),
                RequiredFailed = group.Count(run =>
                    run.Status == JobRunStatus.Failed && run.Importance == JobNodeImportance.Required),
            })
            .FirstOrDefaultAsync(cancellationToken);
        var total = counts?.Total ?? 0;
        var running = counts?.Running ?? 0;
        var queued = counts?.Queued ?? 0;
        var cancelled = counts?.Cancelled ?? 0;
        var failed = counts?.Failed ?? 0;
        var requiredFailed = counts?.RequiredFailed ?? 0;

        // The dependency-cascade skip is the rare path (a required node failed while others are
        // still queued); only then does the full node/edge graph need materializing.
        if (requiredFailed > 0 && queued > 0) {
            var runs = await _db.JobRuns
                .Where(run => run.GraphId == id)
                .ToArrayAsync(cancellationToken);
            var requiredFailures = runs
                .Where(run => run.Status == JobRunStatus.Failed && run.Importance == JobNodeImportance.Required)
                .Select(run => run.Id)
                .ToHashSet();
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
                queued--;
                cancelled++;
            }
        }

        var hasActiveNodes = running > 0 || queued > 0;
        var hasOpenSignals = await _db.JobGraphSignals.AnyAsync(
            signal => signal.GraphId == id && signal.ResolvedAt == null && signal.CancelledAt == null,
            cancellationToken);
        var nowUtc = DateTimeOffset.UtcNow;
        if (hasActiveNodes) {
            graph.Status = running > 0
                ? JobGraphStatus.Running
                : JobGraphStatus.Queued;
            graph.FinishedAt = null;
        } else if (hasOpenSignals) {
            graph.Status = JobGraphStatus.Waiting;
            graph.FinishedAt = null;
        } else if (graph.CancellationRequested || cancelled == total) {
            graph.Status = JobGraphStatus.Cancelled;
            graph.FinishedAt = nowUtc;
        } else if (requiredFailed > 0) {
            graph.Status = JobGraphStatus.Failed;
            graph.FinishedAt = nowUtc;
        } else if (failed > 0) {
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
