using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Queue;

public sealed partial class JobGraphService {
    public Task<IReadOnlyList<JobGraphSnapshot>> ListAsync(CancellationToken cancellationToken) =>
        ListAsync(hideNsfw: false, cancellationToken);

    public async Task<IReadOnlyList<JobGraphSnapshot>> ListAsync(
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var rows = await db.JobGraphs.AsNoTracking()
            .OrderBy(graph => graph.Status == Domain.Entities.JobGraphStatus.Running ? 0 : 1)
            .ThenByDescending(graph => graph.UpdatedAt)
            .Take(200)
            .ToArrayAsync(cancellationToken);
        if (hideNsfw && rows.Length > 0) {
            var hiddenGraphIds = await HiddenGraphIdsAsync(rows, cancellationToken);
            rows = rows.Where(graph => !hiddenGraphIds.Contains(graph.Id)).ToArray();
        }
        return rows.Select(ToSnapshot).ToArray();
    }

    public Task<JobGraphDetailSnapshot?> GetAsync(Guid graphId, CancellationToken cancellationToken) =>
        GetAsync(graphId, hideNsfw: false, cancellationToken);

    public async Task<JobGraphDetailSnapshot?> GetAsync(
        Guid graphId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var graph = await db.JobGraphs.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == graphId, cancellationToken);
        if (graph is null) return null;
        if (hideNsfw && (await HiddenGraphIdsAsync([graph], cancellationToken)).Contains(graphId)) {
            return null;
        }

        var nodeRows = await db.JobRuns.AsNoTracking()
            .Where(run => run.GraphId == graphId)
            .OrderBy(run => run.Sequence)
            .ToArrayAsync(cancellationToken);
        var nodes = nodeRows.Select(run => JobQueueService.ToSnapshot(run, graph.Origin)).ToArray();
        var dependencies = await db.JobDependencies.AsNoTracking()
            .Where(edge => edge.GraphId == graphId)
            .Select(edge => new JobGraphDependencySnapshot(edge.PredecessorJobRunId, edge.SuccessorJobRunId))
            .ToArrayAsync(cancellationToken);
        var signals = await db.JobGraphSignals.AsNoTracking()
            .Where(signal => signal.GraphId == graphId)
            .OrderBy(signal => signal.CreatedAt)
            .Select(signal => new JobGraphSignalSnapshot(
                signal.Id,
                signal.GraphId,
                signal.Key,
                signal.Kind,
                signal.CorrelationId,
                signal.Message,
                signal.CreatedAt,
                signal.ResolvedAt,
                signal.CancelledAt))
            .ToArrayAsync(cancellationToken);
        return new JobGraphDetailSnapshot(ToSnapshot(graph), nodes, dependencies, signals);
    }

    private async Task<HashSet<Guid>> HiddenGraphIdsAsync(
        IReadOnlyCollection<JobGraphRow> graphs,
        CancellationToken cancellationToken) {
        var rootTargetByGraph = graphs
            .Select(graph => new {
                graph.Id,
                TargetId = Guid.TryParse(graph.RootEntityId, out var targetId) ? targetId : (Guid?)null
            })
            .Where(candidate => candidate.TargetId is not null)
            .ToDictionary(candidate => candidate.Id, candidate => candidate.TargetId!.Value);
        var targetIds = rootTargetByGraph.Values.Distinct().ToArray();
        var hiddenEntityIds = targetIds.Length == 0
            ? []
            : await db.Entities.AsNoTracking()
                .Where(entity => targetIds.Contains(entity.Id) && entity.IsNsfw)
                .Select(entity => entity.Id)
                .ToArrayAsync(cancellationToken);
        var hiddenRootIds = targetIds.Length == 0
            ? []
            : await db.LibraryRoots.AsNoTracking()
                .Where(root => targetIds.Contains(root.Id) && root.IsNsfw)
                .Select(root => root.Id)
                .ToArrayAsync(cancellationToken);
        var hiddenTargets = hiddenEntityIds.Concat(hiddenRootIds).ToHashSet();
        var hiddenGraphs = rootTargetByGraph
            .Where(candidate => hiddenTargets.Contains(candidate.Value))
            .Select(candidate => candidate.Key)
            .ToHashSet();

        var graphIds = graphs.Select(graph => graph.Id).ToArray();
        var hiddenAcquisitionGraphs = await db.Acquisitions.AsNoTracking()
            .Where(acquisition =>
                acquisition.JobGraphId != null &&
                graphIds.Contains(acquisition.JobGraphId.Value) &&
                acquisition.EntityId != null)
            .Join(
                db.Entities.AsNoTracking().Where(entity => entity.IsNsfw),
                acquisition => acquisition.EntityId,
                entity => (Guid?)entity.Id,
                (acquisition, _) => acquisition.JobGraphId!.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        hiddenGraphs.UnionWith(hiddenAcquisitionGraphs);
        return hiddenGraphs;
    }
}
