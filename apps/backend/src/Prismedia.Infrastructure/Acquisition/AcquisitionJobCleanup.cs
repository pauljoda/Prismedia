using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Cancels exact acquisition work and terminalizes every durable graph that owns it, including graphs
/// paused on release review or another external signal with no queued/running node left to cancel.
/// </summary>
public sealed class AcquisitionJobCleanup(
    PrismediaDbContext db,
    IJobGraphService? graphs = null) : IAcquisitionJobCleanup {
    /// <inheritdoc />
    public async Task<int> CancelAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var target = acquisitionId.ToString();
        var now = DateTimeOffset.UtcNow;
        var graphIds = (await db.JobRuns.AsNoTracking()
                .Where(job => job.TargetEntityId == target && job.GraphId != null)
                .Select(job => job.GraphId!.Value)
                .ToArrayAsync(cancellationToken))
            .ToHashSet();
        var acquisitionGraphId = await db.Acquisitions.AsNoTracking()
            .Where(acquisition => acquisition.Id == acquisitionId)
            .Select(acquisition => acquisition.JobGraphId)
            .SingleOrDefaultAsync(cancellationToken);
        if (acquisitionGraphId is { } linkedGraphId) {
            graphIds.Add(linkedGraphId);
        }
        var query = db.JobRuns.Where(job =>
            job.TargetEntityId == target
            && (job.Status == JobRunStatus.Queued || job.Status == JobRunStatus.Running));

        int cancelled;
        if (db.Database.IsRelational()) {
            cancelled = await query.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, JobRunStatus.Cancelled)
                    .SetProperty(job => job.Message, "Cancelled because its acquisition was removed.")
                    .SetProperty(job => job.LockedAt, (DateTimeOffset?)null)
                    .SetProperty(job => job.LockedBy, (string?)null)
                    .SetProperty(job => job.FinishedAt, now),
                cancellationToken);
        } else {
            var rows = await query.ToArrayAsync(cancellationToken);
            foreach (var row in rows) {
                row.Status = JobRunStatus.Cancelled;
                row.Message = "Cancelled because its acquisition was removed.";
                row.LockedAt = null;
                row.LockedBy = null;
                row.FinishedAt = now;
            }

            await db.SaveChangesAsync(cancellationToken);
            cancelled = rows.Length;
        }

        if (graphs is not null && graphIds.Count > 0) {
            var activeGraphIds = await db.JobGraphs.AsNoTracking()
                .Where(graph => graphIds.Contains(graph.Id)
                    && (graph.Status == JobGraphStatus.Queued
                        || graph.Status == JobGraphStatus.Running
                        || graph.Status == JobGraphStatus.Waiting))
                .Select(graph => graph.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var graphId in activeGraphIds) {
                await graphs.CancelAsync(graphId, cancellationToken);
            }
        }

        return cancelled;
    }
}
