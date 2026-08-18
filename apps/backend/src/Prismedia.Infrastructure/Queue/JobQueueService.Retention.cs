using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Queue;

public sealed partial class JobQueueService {
    public async Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) {
        var cutoff = DateTimeOffset.UtcNow - retention;
        var query = _db.JobRuns
            .Where(job =>
                (job.Status == JobRunStatus.Completed || job.Status == JobRunStatus.Cancelled) &&
                job.FinishedAt != null &&
                job.FinishedAt < cutoff &&
                (job.GraphId == null || !_db.JobGraphs.Any(graph =>
                    graph.Id == job.GraphId &&
                    (graph.Status == JobGraphStatus.Queued ||
                     graph.Status == JobGraphStatus.Running ||
                     graph.Status == JobGraphStatus.Waiting))));
        int pruned;
        if (_db.Database.IsRelational()) {
            pruned = await query.ExecuteDeleteAsync(cancellationToken);
        } else {
            var rows = await query.ToArrayAsync(cancellationToken);
            _db.JobRuns.RemoveRange(rows);
            await _db.SaveChangesAsync(cancellationToken);
            pruned = rows.Length;
        }

        pruned += await PruneGraphHistoryAsync(cutoff, cancellationToken);
        return pruned;
    }

    /// <summary>
    /// Deletes terminal job graphs past the retention cutoff once their runs are gone, plus
    /// per-entity resource states nothing references anymore. Graph rows previously lived
    /// forever (hundreds of thousands of rows joined by every claim attempt), and
    /// <c>entity:{id}</c> resource rows accumulated one per entity ever targeted.
    /// </summary>
    private async Task<int> PruneGraphHistoryAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) {
        var graphQuery = _db.JobGraphs
            .Where(graph =>
                graph.Status != JobGraphStatus.Queued &&
                graph.Status != JobGraphStatus.Running &&
                graph.Status != JobGraphStatus.Waiting &&
                graph.UpdatedAt < cutoff &&
                !_db.JobRuns.Any(run => run.GraphId == graph.Id));
        var entityResourcePrefix = JobResourceKeys.EntityPrefix;
        var resourceQuery = _db.JobResourceStates
            .Where(resource =>
                resource.Key.StartsWith(entityResourcePrefix) &&
                resource.UpdatedAt < cutoff &&
                !_db.JobResourceLeases.Any(lease => lease.ResourceKey == resource.Key) &&
                !_db.JobRuns.Any(run => run.ResourceKey == resource.Key &&
                    (run.Status == JobRunStatus.Queued || run.Status == JobRunStatus.Running)));

        if (_db.Database.IsRelational()) {
            return await graphQuery.ExecuteDeleteAsync(cancellationToken) +
                await resourceQuery.ExecuteDeleteAsync(cancellationToken);
        }

        var graphs = await graphQuery.ToArrayAsync(cancellationToken);
        _db.JobGraphs.RemoveRange(graphs);
        var resources = await resourceQuery.ToArrayAsync(cancellationToken);
        _db.JobResourceStates.RemoveRange(resources);
        await _db.SaveChangesAsync(cancellationToken);
        return graphs.Length + resources.Length;
    }
}
