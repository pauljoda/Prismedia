using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Queue;

public sealed partial class JobGraphService {
    public async Task<JobGraphSignalSnapshot> OpenSignalAsync(
        Guid graphId,
        string key,
        JobGraphSignalKind kind,
        string? correlationId,
        string? message,
        CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var mutation = await JobGraphMutationScope.AcquireAsync(db, graphId, cancellationToken)
            ?? throw new InvalidOperationException($"Job graph '{graphId}' was not found.");
        var graph = mutation.Graph;
        if (graph.Status is JobGraphStatus.Completed or JobGraphStatus.CompletedWithWarnings
            or JobGraphStatus.Failed or JobGraphStatus.Cancelled) {
            throw new InvalidOperationException($"Job graph '{graphId}' is already terminal.");
        }

        var existing = await db.JobGraphSignals.AsNoTracking()
            .SingleOrDefaultAsync(signal => signal.GraphId == graphId && signal.Key == key, cancellationToken);
        if (existing is not null) {
            await mutation.CommitAsync(cancellationToken);
            return ToSnapshot(existing);
        }

        var now = DateTimeOffset.UtcNow;
        var row = new JobGraphSignalRow {
            Id = Guid.NewGuid(),
            GraphId = graphId,
            Key = key.Trim(),
            Kind = kind,
            CorrelationId = correlationId,
            Message = message,
            CreatedAt = now
        };
        db.JobGraphSignals.Add(row);
        var hasRunning = await db.JobRuns.AnyAsync(
            run => run.GraphId == graphId && run.Status == JobRunStatus.Running,
            cancellationToken);
        var hasQueued = await db.JobRuns.AnyAsync(
            run => run.GraphId == graphId && run.Status == JobRunStatus.Queued,
            cancellationToken);
        graph.Status = hasRunning
            ? JobGraphStatus.Running
            : hasQueued
                ? JobGraphStatus.Queued
                : JobGraphStatus.Waiting;
        graph.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await mutation.CommitAsync(cancellationToken);
        return ToSnapshot(row);
    }

    public async Task<JobGraphSignalSnapshot> ResolveSignalAsync(
        Guid graphId,
        string key,
        IReadOnlyList<GraphJobNodeRequest> continuationNodes,
        CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var mutation = await JobGraphMutationScope.AcquireAsync(db, graphId, cancellationToken)
            ?? throw new InvalidOperationException($"Job graph '{graphId}' was not found.");
        var signal = await db.JobGraphSignals
            .SingleOrDefaultAsync(item => item.GraphId == graphId && item.Key == key, cancellationToken)
            ?? throw new InvalidOperationException($"Signal '{key}' was not found in graph '{graphId}'.");
        if (signal.CancelledAt is not null) {
            throw new InvalidOperationException($"Signal '{key}' was cancelled.");
        }

        if (signal.ResolvedAt is null) {
            signal.ResolvedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        foreach (var node in continuationNodes) {
            await AppendNodeAsync(graphId, node, cancellationToken);
        }

        var graph = await db.JobGraphs.FindAsync([graphId], cancellationToken)
            ?? throw new InvalidOperationException($"Job graph '{graphId}' was not found.");
        var hasActive = await db.JobRuns.AnyAsync(
            run => run.GraphId == graphId && (run.Status == JobRunStatus.Queued || run.Status == JobRunStatus.Running),
            cancellationToken);
        var hasOpenSignals = await db.JobGraphSignals.AnyAsync(
            item => item.GraphId == graphId && item.ResolvedAt == null && item.CancelledAt == null,
            cancellationToken);
        if (hasActive) {
            graph.Status = JobGraphStatus.Queued;
        } else if (hasOpenSignals) {
            graph.Status = JobGraphStatus.Waiting;
        } else {
            var failed = await db.JobRuns
                .Where(run => run.GraphId == graphId && run.Status == JobRunStatus.Failed)
                .Select(run => run.Importance)
                .ToArrayAsync(cancellationToken);
            graph.Status = failed.Contains(JobNodeImportance.Required)
                ? JobGraphStatus.Failed
                : failed.Length > 0
                    ? JobGraphStatus.CompletedWithWarnings
                    : JobGraphStatus.Completed;
            graph.FinishedAt = DateTimeOffset.UtcNow;
        }
        graph.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await mutation.CommitAsync(cancellationToken);
        return ToSnapshot(signal);
    }

    public async Task<bool> CancelAsync(Guid graphId, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            // Phase 1: publish the cancellation intent alone and commit. The claim loop skips
            // graphs with CancellationRequested, so no new nodes of this graph get dispatched
            // while the sweep below runs.
            {
                await using var mutation = await JobGraphMutationScope.AcquireAsync(db, graphId, cancellationToken);
                if (mutation is null || mutation.Graph.Status is JobGraphStatus.Completed
                    or JobGraphStatus.CompletedWithWarnings or JobGraphStatus.Failed
                    or JobGraphStatus.Cancelled) return false;
                mutation.Graph.CancellationRequested = true;
                mutation.Graph.UpdatedAt = now;
                await db.SaveChangesAsync(cancellationToken);
                await mutation.CommitAsync(cancellationToken);
            }

            // Phase 2: sweep active nodes set-based, OUTSIDE the graph lock. Claims lock the
            // run row first and the graph row second; a sweep holding the graph lock while
            // updating run rows deadlocks against them (observed live on a 9k-node scan
            // graph). Each statement here runs in its own transaction, and claims already in
            // flight when phase 1 committed are absorbed by a short deadlock retry.
            for (var attempt = 1; ; attempt++) {
                try {
                    await db.JobRuns
                        .Where(run => run.GraphId == graphId &&
                            (run.Status == JobRunStatus.Queued || run.Status == JobRunStatus.Running))
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(run => run.Status, JobRunStatus.Cancelled)
                            .SetProperty(run => run.Message, "Cancelled with graph.")
                            .SetProperty(run => run.LockedAt, (DateTimeOffset?)null)
                            .SetProperty(run => run.LockedBy, (string?)null)
                            .SetProperty(run => run.FinishedAt, now), cancellationToken);
                    break;
                } catch (Exception exception) when (attempt < 4 && IsDeadlock(exception)) {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
                }
            }

            await db.JobResourceLeases
                .Where(lease => db.JobRuns.Any(run => run.Id == lease.JobRunId && run.GraphId == graphId))
                .ExecuteDeleteAsync(cancellationToken);
            await db.JobGraphSignals
                .Where(signal => signal.GraphId == graphId && signal.ResolvedAt == null && signal.CancelledAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(signal => signal.CancelledAt, (DateTimeOffset?)now), cancellationToken);

            // Phase 3: finalize the graph under the lock again.
            {
                await using var mutation = await JobGraphMutationScope.AcquireAsync(db, graphId, cancellationToken);
                if (mutation is null) return false;
                mutation.Graph.CancellationRequested = true;
                mutation.Graph.Status = JobGraphStatus.Cancelled;
                mutation.Graph.UpdatedAt = now;
                mutation.Graph.FinishedAt = now;
                await db.SaveChangesAsync(cancellationToken);
                await mutation.CommitAsync(cancellationToken);
            }

            return true;
        }

        await using var tracked = await JobGraphMutationScope.AcquireAsync(db, graphId, cancellationToken);
        if (tracked is null || tracked.Graph.Status is JobGraphStatus.Completed or JobGraphStatus.CompletedWithWarnings
            or JobGraphStatus.Failed or JobGraphStatus.Cancelled) return false;
        var graph = tracked.Graph;
        var runs = await db.JobRuns
            .Where(run => run.GraphId == graphId && (run.Status == JobRunStatus.Queued || run.Status == JobRunStatus.Running))
            .ToArrayAsync(cancellationToken);
        foreach (var run in runs) {
            run.Status = JobRunStatus.Cancelled;
            run.Message = "Cancelled with graph.";
            run.LockedAt = null;
            run.LockedBy = null;
            run.FinishedAt = now;
        }
        var signals = await db.JobGraphSignals
            .Where(signal => signal.GraphId == graphId && signal.ResolvedAt == null && signal.CancelledAt == null)
            .ToArrayAsync(cancellationToken);
        foreach (var signal in signals) signal.CancelledAt = now;
        var runIds = runs.Select(run => run.Id).ToArray();
        var leases = await db.JobResourceLeases
            .Where(lease => runIds.Contains(lease.JobRunId))
            .ToArrayAsync(cancellationToken);
        db.JobResourceLeases.RemoveRange(leases);
        graph.CancellationRequested = true;
        graph.Status = JobGraphStatus.Cancelled;
        graph.UpdatedAt = now;
        graph.FinishedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await tracked.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>Matches Postgres deadlock aborts (SQLSTATE 40P01) anywhere in the exception chain.</summary>
    private static bool IsDeadlock(Exception exception) {
        for (var current = exception; current is not null; current = current.InnerException!) {
            if (current is Npgsql.PostgresException { SqlState: "40P01" }) {
                return true;
            }

            if (current.InnerException is null) {
                break;
            }
        }

        return false;
    }

    private static JobGraphSignalSnapshot ToSnapshot(JobGraphSignalRow signal) =>
        new(
            signal.Id,
            signal.GraphId,
            signal.Key,
            signal.Kind,
            signal.CorrelationId,
            signal.Message,
            signal.CreatedAt,
            signal.ResolvedAt,
            signal.CancelledAt);
}
