using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Queue;

/// <summary>EF Core implementation of durable graph creation and idempotent expansion.</summary>
public sealed class JobGraphService(PrismediaDbContext db) : IJobGraphService {
    public async Task<JobGraphSnapshot> StartAsync(
        StartJobGraphRequest request,
        CancellationToken cancellationToken) {
        ValidateGraphRequest(request);

        if (request.ActiveKey is not null) {
            var active = await db.JobGraphs.AsNoTracking()
                .Where(graph => graph.ActiveKey == request.ActiveKey &&
                    (graph.Status == JobGraphStatus.Queued ||
                     graph.Status == JobGraphStatus.Running ||
                     graph.Status == JobGraphStatus.Waiting))
                .OrderBy(graph => graph.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (active is not null) {
                return ToSnapshot(active);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var graphId = Guid.NewGuid();
        var rootRunId = Guid.NewGuid();
        var graph = new JobGraphRow {
            Id = graphId,
            Origin = request.Origin,
            Status = JobGraphStatus.Queued,
            DisplayName = request.DisplayName.Trim(),
            RootRunId = rootRunId,
            InitiatingUserId = request.InitiatingUserId,
            RootEntityKind = request.RootEntityKind,
            RootEntityId = request.RootEntityId,
            ActiveKey = request.ActiveKey,
            CreatedAt = now,
            UpdatedAt = now
        };
        var root = CreateRun(graphId, rootRunId, request.Root, sequence: 0, now);

        db.JobGraphs.Add(graph);
        db.JobRuns.Add(root);
        AddDependencies(graphId, rootRunId, request.Root.DependsOn);

        try {
            await db.SaveChangesAsync(cancellationToken);
        } catch (DbUpdateException) when (request.ActiveKey is not null) {
            db.ChangeTracker.Clear();
            var active = await db.JobGraphs.AsNoTracking()
                .Where(candidate => candidate.ActiveKey == request.ActiveKey &&
                    (candidate.Status == JobGraphStatus.Queued ||
                     candidate.Status == JobGraphStatus.Running ||
                     candidate.Status == JobGraphStatus.Waiting))
                .OrderBy(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (active is null) {
                throw;
            }

            return ToSnapshot(active);
        }

        return ToSnapshot(graph);
    }

    public async Task<JobRunSnapshot> AppendNodeAsync(
        Guid graphId,
        GraphJobNodeRequest request,
        CancellationToken cancellationToken) {
        ValidateNodeRequest(request);
        var graph = await db.JobGraphs.FindAsync([graphId], cancellationToken)
            ?? throw new InvalidOperationException($"Job graph '{graphId}' was not found.");
        if (graph.Status is JobGraphStatus.Completed or JobGraphStatus.CompletedWithWarnings
            or JobGraphStatus.Failed or JobGraphStatus.Cancelled) {
            throw new InvalidOperationException($"Job graph '{graphId}' is already terminal.");
        }

        var existing = await db.JobRuns.AsNoTracking()
            .SingleOrDefaultAsync(
                run => run.GraphId == graphId && run.NodeKey == request.NodeKey,
                cancellationToken);
        if (existing is not null) {
            return JobQueueService.ToSnapshot(existing, graph.Origin);
        }

        await ValidateReferencesAsync(graphId, request, cancellationToken);
        var sequence = await db.JobRuns
            .Where(run => run.GraphId == graphId)
            .Select(run => (long?)run.Sequence)
            .MaxAsync(cancellationToken) ?? -1;
        var now = DateTimeOffset.UtcNow;
        var row = CreateRun(graphId, Guid.NewGuid(), request, sequence + 1, now);
        db.JobRuns.Add(row);
        AddDependencies(graphId, row.Id, request.DependsOn);
        graph.UpdatedAt = now;

        try {
            await db.SaveChangesAsync(cancellationToken);
        } catch (DbUpdateException) {
            db.ChangeTracker.Clear();
            existing = await db.JobRuns.AsNoTracking()
                .SingleOrDefaultAsync(
                    run => run.GraphId == graphId && run.NodeKey == request.NodeKey,
                    cancellationToken);
            if (existing is null) {
                throw;
            }

            return JobQueueService.ToSnapshot(existing, graph.Origin);
        }

        return JobQueueService.ToSnapshot(row, graph.Origin);
    }

    private static void ValidateGraphRequest(StartJobGraphRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.DisplayName)) {
            throw new ArgumentException("A job graph display name is required.", nameof(request));
        }

        ValidateNodeRequest(request.Root);
    }

    private static void ValidateNodeRequest(GraphJobNodeRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.NodeKey)) {
            throw new ArgumentException("A graph node key is required.", nameof(request));
        }
    }

    private async Task ValidateReferencesAsync(
        Guid graphId,
        GraphJobNodeRequest request,
        CancellationToken cancellationToken) {
        var references = new HashSet<Guid>(request.DependsOn ?? []);
        if (request.ParentRunId is { } parentRunId) {
            references.Add(parentRunId);
        }

        if (references.Count == 0) {
            return;
        }

        var validCount = await db.JobRuns.CountAsync(
            run => run.GraphId == graphId && references.Contains(run.Id),
            cancellationToken);
        if (validCount != references.Count) {
            throw new InvalidOperationException("Graph node parents and dependencies must belong to the same graph.");
        }
    }

    private void AddDependencies(Guid graphId, Guid successorId, IReadOnlyCollection<Guid>? predecessors) {
        foreach (var predecessorId in predecessors ?? []) {
            db.JobDependencies.Add(new JobDependencyRow {
                GraphId = graphId,
                PredecessorJobRunId = predecessorId,
                SuccessorJobRunId = successorId
            });
        }
    }

    private static JobRunRow CreateRun(
        Guid graphId,
        Guid runId,
        GraphJobNodeRequest request,
        long sequence,
        DateTimeOffset now) =>
        new() {
            Id = runId,
            GraphId = graphId,
            NodeKey = request.NodeKey.Trim(),
            ParentRunId = request.ParentRunId,
            Importance = request.Importance,
            ResourceClass = request.ResourceClass,
            ResourceKey = request.ResourceKey,
            Sequence = sequence,
            Type = request.Job.Type,
            Status = JobRunStatus.Queued,
            PayloadJson = request.Job.PayloadJson ?? "{}",
            Priority = request.Job.Priority,
            Lane = request.Job.Lane,
            Attempts = 0,
            MaxAttempts = 3,
            Progress = 0,
            TargetEntityKind = request.Job.TargetEntityKind,
            TargetEntityId = request.Job.TargetEntityId,
            TargetLabel = request.Job.TargetLabel,
            AvailableAt = now,
            CreatedAt = now
        };

    private static JobGraphSnapshot ToSnapshot(JobGraphRow graph) =>
        new(
            graph.Id,
            graph.Id,
            graph.Origin,
            graph.Status,
            graph.DisplayName,
            graph.RootRunId,
            graph.InitiatingUserId,
            graph.RootEntityKind,
            graph.RootEntityId,
            graph.CreatedAt,
            graph.UpdatedAt,
            graph.FinishedAt);
}
