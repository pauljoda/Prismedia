using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Application.Security;

namespace Prismedia.Infrastructure.Queue;

public sealed partial class JobQueueService : IJobQueueService {
    private readonly PrismediaDbContext _db;
    private readonly ICurrentUserContext? _currentUser;

    public JobQueueService(PrismediaDbContext db, ICurrentUserContext? currentUser = null) {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) {
        const int limit = 200;

        var activeRows = await FilterVisibleRowsAsync(_db.JobRuns
            .AsNoTracking()
            .Where(row => row.Status == JobRunStatus.Running || row.Status == JobRunStatus.Failed)
            .OrderByDescending(row => row.StartedAt ?? row.FinishedAt ?? row.CreatedAt)
            .ThenByDescending(row => row.CreatedAt)
            .Take(limit), hideNsfw, cancellationToken);

        var activeIds = activeRows.Select(row => row.Id).ToList();
        var recentRows = await FilterVisibleRowsAsync(_db.JobRuns
            .AsNoTracking()
            .Where(row => !activeIds.Contains(row.Id))
            .OrderByDescending(row => row.CreatedAt)
            .Take(Math.Max(0, limit - activeRows.Count)), hideNsfw, cancellationToken);

        return activeRows
            .Concat(recentRows)
            .OrderBy(row =>
                row.Status == JobRunStatus.Running ? 0 :
                row.Status == JobRunStatus.Failed ? 1 :
                2)
            .ThenByDescending(row => row.StartedAt ?? row.FinishedAt ?? row.CreatedAt)
            .ThenByDescending(row => row.CreatedAt)
            .Select(row => ToSnapshot(row))
            .ToList();
    }

    private static readonly JobType[] AutoIdentifyBarrierJobTypes = JobDefinitionRegistry.All
        .Where(definition => definition.BlocksAutoIdentify)
        .Select(definition => definition.Type)
        .ToArray();

    private static readonly string AutoIdentifyJobTypeCode = JobType.AutoIdentify.ToCode();
    private static readonly string[] TargetedAutoIdentifyKindCodes = EntityKindRegistry.All
        .OfType<IPlayableVideoKindDefinition>()
        .Select(definition => definition.Kind.ToCode())
        .ToArray();
    private static readonly IReadOnlySet<string> TargetedAutoIdentifyKindCodeSet =
        TargetedAutoIdentifyKindCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly string MusicArtistKindCode = EntityKind.MusicArtist.ToCode();
    private static readonly string AudioLibraryKindCode = EntityKind.AudioLibrary.ToCode();

    public async Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) {
        return await EnqueueAsync(new EnqueueJobRequest(type), cancellationToken);
    }

    public async Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
        // Some jobs are queue-wide singletons: scans already walk every enabled root of their kind,
        // and database backups should never overlap. When one is already queued or running, return
        // the in-flight job instead of stacking another.
        var definition = JobDefinitionRegistry.Get(request.Type);
        var isQueueWideSingleton = JobDefinitionRegistry.IsQueueWideSingleton(
            request.Type,
            hasTarget: request.TargetEntityId is not null);
        if (isQueueWideSingleton) {
            var existing = await _db.JobRuns.AsNoTracking()
                .Where(job => job.Type == request.Type &&
                              (definition.SingletonBehavior != JobSingletonBehavior.QueueWideWhenUntargeted || job.TargetEntityId == null) &&
                              (job.Status == JobRunStatus.Queued || job.Status == JobRunStatus.Running))
                .OrderBy(job => job.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null) {
                return ToSnapshot(existing);
            }
        }

        var origin = OriginFor(request);
        if (origin == JobGraphOrigin.Background && request.TargetEntityId is not null) {
            var existing = await _db.JobRuns.AsNoTracking()
                .Where(job => job.Type == request.Type &&
                              job.TargetEntityId == request.TargetEntityId &&
                              (job.Status == JobRunStatus.Queued || job.Status == JobRunStatus.Running))
                .OrderBy(job => job.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null) {
                return ToSnapshot(existing);
            }
        }

        var (graph, row) = CreateRootGraph(request, origin, InitiatingUserId(), DateTimeOffset.UtcNow);
        if (graph.ActiveKey is not null) {
            var active = await FindActiveGraphRootAsync(graph.ActiveKey, cancellationToken);
            if (active is not null) {
                return ToSnapshot(active.Value.Run, active.Value.Origin);
            }

            await TryReleaseInvalidActiveGraphAsync(graph.ActiveKey, cancellationToken);
        }
        await EnsureEntityResourceDeclaredAsync(row.ResourceKey, cancellationToken);
        _db.JobGraphs.Add(graph);
        _db.JobRuns.Add(row);
        try {
            await _db.SaveChangesAsync(cancellationToken);
        } catch (DbUpdateException) when (graph.ActiveKey is not null) {
            _db.ChangeTracker.Clear();
            var existing = await FindActiveGraphRootAsync(graph.ActiveKey, cancellationToken);
            if (existing is null) {
                if (await TryReleaseInvalidActiveGraphAsync(graph.ActiveKey, cancellationToken)) {
                    return await EnqueueAsync(request, cancellationToken);
                }

                throw;
            }

            return ToSnapshot(existing.Value.Run, existing.Value.Origin);
        }

        return ToSnapshot(row, origin);
    }

    private async Task<(JobGraphOrigin Origin, JobRunRow Run)?> FindActiveGraphRootAsync(
        string activeKey,
        CancellationToken cancellationToken) {
        var existing = await _db.JobGraphs.AsNoTracking()
            .Where(active => active.ActiveKey == activeKey &&
                (active.Status == JobGraphStatus.Queued ||
                 active.Status == JobGraphStatus.Running ||
                 active.Status == JobGraphStatus.Waiting))
            .Where(active => _db.JobRuns.Any(run => run.GraphId == active.Id &&
                    (run.Status == JobRunStatus.Queued || run.Status == JobRunStatus.Running)) ||
                _db.JobGraphSignals.Any(signal => signal.GraphId == active.Id &&
                    signal.ResolvedAt == null && signal.CancelledAt == null))
            .Join(
                _db.JobRuns.AsNoTracking(),
                active => active.RootRunId,
                run => run.Id,
                (active, run) => new { active.Origin, Run = run })
            .OrderBy(candidate => candidate.Run.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return existing is null ? null : (existing.Origin, existing.Run);
    }

    private async Task<bool> TryReleaseInvalidActiveGraphAsync(
        string activeKey,
        CancellationToken cancellationToken) {
        if (await TryRetireRootlessActiveGraphAsync(activeKey, cancellationToken)) {
            return true;
        }

        var inertGraphId = await _db.JobGraphs.AsNoTracking()
            .Where(graph => graph.ActiveKey == activeKey &&
                (graph.Status == JobGraphStatus.Queued ||
                 graph.Status == JobGraphStatus.Running ||
                 graph.Status == JobGraphStatus.Waiting))
            .Where(graph => _db.JobRuns.Any(run => run.Id == graph.RootRunId))
            .Where(graph => !_db.JobRuns.Any(run => run.GraphId == graph.Id &&
                (run.Status == JobRunStatus.Queued || run.Status == JobRunStatus.Running)))
            .Where(graph => !_db.JobGraphSignals.Any(signal => signal.GraphId == graph.Id &&
                signal.ResolvedAt == null && signal.CancelledAt == null))
            .Select(graph => (Guid?)graph.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (inertGraphId is not { } id) {
            return false;
        }

        await ReconcileGraphStateAsync(id, cancellationToken);
        return !await _db.JobGraphs.AsNoTracking().AnyAsync(graph =>
            graph.Id == id &&
            (graph.Status == JobGraphStatus.Queued ||
             graph.Status == JobGraphStatus.Running ||
             graph.Status == JobGraphStatus.Waiting), cancellationToken);
    }

    /// <summary>
    /// Retires historical active-key blockers whose root was pruned by older builds. A graph with any
    /// queued or running node is deliberately left untouched; only a workflow that cannot execute and
    /// cannot provide its deduplication snapshot is safe to supersede.
    /// </summary>
    private async Task<bool> TryRetireRootlessActiveGraphAsync(
        string activeKey,
        CancellationToken cancellationToken) {
        var graphId = await _db.JobGraphs.AsNoTracking()
            .Where(graph => graph.ActiveKey == activeKey &&
                (graph.Status == JobGraphStatus.Queued ||
                 graph.Status == JobGraphStatus.Running ||
                 graph.Status == JobGraphStatus.Waiting))
            .Where(graph => !_db.JobRuns.Any(run => run.Id == graph.RootRunId))
            .Where(graph => !_db.JobRuns.Any(run => run.GraphId == graph.Id &&
                (run.Status == JobRunStatus.Queued || run.Status == JobRunStatus.Running)))
            .Select(graph => (Guid?)graph.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (graphId is not { } id) {
            return false;
        }

        await using var mutation = await JobGraphMutationScope.AcquireAsync(_db, id, cancellationToken);
        if (mutation is null || mutation.Graph.ActiveKey != activeKey ||
            mutation.Graph.Status is not (JobGraphStatus.Queued or JobGraphStatus.Running or JobGraphStatus.Waiting) ||
            await _db.JobRuns.AnyAsync(run => run.Id == mutation.Graph.RootRunId, cancellationToken) ||
            await _db.JobRuns.AnyAsync(run => run.GraphId == id &&
                (run.Status == JobRunStatus.Queued || run.Status == JobRunStatus.Running), cancellationToken)) {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var signals = await _db.JobGraphSignals
            .Where(signal => signal.GraphId == id && signal.ResolvedAt == null && signal.CancelledAt == null)
            .ToArrayAsync(cancellationToken);
        foreach (var signal in signals) {
            signal.CancelledAt = now;
        }

        mutation.Graph.CancellationRequested = true;
        mutation.Graph.Status = JobGraphStatus.Cancelled;
        mutation.Graph.UpdatedAt = now;
        mutation.Graph.FinishedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        await mutation.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<JobRunSnapshot> EnqueueChildAsync(
        JobRunSnapshot parent,
        EnqueueJobRequest request,
        CancellationToken cancellationToken) {
        if (parent.GraphId is not { } graphId) {
            return await EnqueueAsync(request, cancellationToken);
        }

        var graph = await _db.JobGraphs.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == graphId, cancellationToken)
            ?? throw new InvalidOperationException($"Job graph '{graphId}' was not found.");
        var node = new GraphJobNodeRequest(
            request.NodeKey ?? DefaultNodeKey(request),
            request,
            ParentRunId: parent.Id,
            DependsOn: [parent.Id],
            Importance: request.Importance ?? JobDefinitionRegistry.Importance(request.Type),
            ResourceClass: request.ResourceClass ?? JobDefinitionRegistry.ResourceClass(request.Type),
            ResourceKey: request.ResourceKey ?? EntityResourceKey(request));
        await EnsureEntityResourceDeclaredAsync(node.ResourceKey, cancellationToken);

        var appended = await new JobGraphService(_db)
            .AppendNodeAsync(graphId, node, cancellationToken);
        return appended with { GraphOrigin = graph.Origin };
    }

    public async Task<JobRunSnapshot> AppendChildGraphNodeAsync(
        JobRunSnapshot parent,
        GraphJobNodeRequest request,
        CancellationToken cancellationToken) {
        if (parent.GraphId is not { } graphId) {
            return await EnqueueChildAsync(parent, request.Job, cancellationToken);
        }

        var graph = await _db.JobGraphs.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == graphId, cancellationToken)
            ?? throw new InvalidOperationException($"Job graph '{graphId}' was not found.");
        var dependencies = request.DependsOn is { Count: > 0 }
            ? request.DependsOn
            : [parent.Id];
        var inherited = request with {
            ParentRunId = parent.Id,
            DependsOn = dependencies,
            Importance = request.Job.Importance ?? request.Importance,
            ResourceClass = request.Job.ResourceClass ?? request.ResourceClass,
            ResourceKey = request.Job.ResourceKey ?? request.ResourceKey ?? EntityResourceKey(request.Job)
        };
        await EnsureEntityResourceDeclaredAsync(inherited.ResourceKey, cancellationToken);
        var appended = await new JobGraphService(_db)
            .AppendNodeAsync(graphId, inherited, cancellationToken);
        return appended with { GraphOrigin = graph.Origin };
    }

    public async Task<int> EnqueueChildBatchAsync(
        JobRunSnapshot parent,
        IReadOnlyList<EnqueueJobRequest> requests,
        CancellationToken cancellationToken) {
        var added = 0;
        foreach (var request in requests) {
            var before = parent.GraphId is null
                ? await _db.JobRuns.CountAsync(cancellationToken)
                : await _db.JobRuns.CountAsync(
                    run => run.GraphId == parent.GraphId && run.NodeKey == (request.NodeKey ?? DefaultNodeKey(request)),
                    cancellationToken);
            await EnqueueChildAsync(parent, request, cancellationToken);
            var after = parent.GraphId is null
                ? await _db.JobRuns.CountAsync(cancellationToken)
                : await _db.JobRuns.CountAsync(
                    run => run.GraphId == parent.GraphId && run.NodeKey == (request.NodeKey ?? DefaultNodeKey(request)),
                    cancellationToken);
            if (after > before) {
                added++;
            }
        }

        return added;
    }

    public async Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) {
        if (requests.Count == 0) return 0;

        var pendingTypes = requests.Select(r => r.Type).Distinct().ToList();
        var pendingTargets = requests
            .Where(r => r.TargetEntityId is not null)
            .Select(r => r.TargetEntityId!)
            .Distinct()
            .ToList();

        var existingPending = await _db.JobRuns
            .AsNoTracking()
            .Where(j => pendingTypes.Contains(j.Type) &&
                        (j.Status == JobRunStatus.Queued || j.Status == JobRunStatus.Running) &&
                        j.TargetEntityId != null &&
                        pendingTargets.Contains(j.TargetEntityId))
            .Select(j => new { j.Type, j.TargetEntityId })
            .ToListAsync(cancellationToken);

        var pendingSet = existingPending
            .Select(p => (p.Type, p.TargetEntityId))
            .ToHashSet();

        var now = DateTimeOffset.UtcNow;
        var enqueued = 0;

        foreach (var request in requests) {
            if (request.TargetEntityId is not null &&
                pendingSet.Contains((request.Type, request.TargetEntityId))) {
                continue;
            }

            var (graph, run) = CreateRootGraph(request, OriginFor(request), InitiatingUserId(), now);
            await EnsureEntityResourceDeclaredAsync(run.ResourceKey, cancellationToken);
            _db.JobGraphs.Add(graph);
            _db.JobRuns.Add(run);
            enqueued++;
        }

        if (enqueued > 0) {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return enqueued;
    }

    public async Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) {
        var query = _db.JobRuns.Where(job =>
            job.Type == type &&
            (job.Status == JobRunStatus.Queued || job.Status == JobRunStatus.Running));

        if (targetEntityId is not null) {
            query = query.Where(job => job.TargetEntityId == targetEntityId);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var query = _db.JobRuns
            .Where(job => job.Status == JobRunStatus.Queued || job.Status == JobRunStatus.Running);

        if (type is not null) {
            query = query.Where(job => job.Type == type.Value);
        }

        var targets = await query
            .Select(job => new { job.Id, job.GraphId })
            .ToArrayAsync(cancellationToken);
        if (_db.Database.IsRelational()) {
            var affected = await query.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, JobRunStatus.Cancelled)
                    .SetProperty(job => job.Message, "Cancelled")
                    .SetProperty(job => job.LockedAt, (DateTimeOffset?)null)
                    .SetProperty(job => job.LockedBy, (string?)null)
                    .SetProperty(job => job.FinishedAt, now),
                cancellationToken);
            foreach (var target in targets) {
                await ReleaseResourceLeaseAsync(target.Id, cancellationToken);
            }
            foreach (var graphId in targets.Where(target => target.GraphId is not null).Select(target => target.GraphId!.Value).Distinct()) {
                await ReconcileGraphStateAsync(graphId, cancellationToken);
            }
            return affected;
        }

        var rows = await query.ToListAsync(cancellationToken);
        foreach (var row in rows) {
            row.Status = JobRunStatus.Cancelled;
            row.Message = "Cancelled";
            row.LockedAt = null;
            row.LockedBy = null;
            row.FinishedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        foreach (var target in targets) {
            await ReleaseResourceLeaseAsync(target.Id, cancellationToken);
        }
        foreach (var graphId in targets.Where(target => target.GraphId is not null).Select(target => target.GraphId!.Value).Distinct()) {
            await ReconcileGraphStateAsync(graphId, cancellationToken);
        }
        return rows.Count;
    }

    public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) =>
        MutateRunAsync(id, row => {
            if (row.Status != JobRunStatus.Queued && row.Status != JobRunStatus.Running) {
                return false;
            }

            row.Status = JobRunStatus.Cancelled;
            row.Message = "Cancelled";
            row.LockedAt = null;
            row.LockedBy = null;
            row.FinishedAt = DateTimeOffset.UtcNow;
            return true;
        }, cancellationToken);

    public async Task<bool> IsRunCancelledAsync(Guid id, CancellationToken cancellationToken) {
        var status = await _db.JobRuns
            .AsNoTracking()
            .Where(job => job.Id == id)
            .Select(job => (JobRunStatus?)job.Status)
            .SingleOrDefaultAsync(cancellationToken);

        return status == JobRunStatus.Cancelled;
    }

    public async Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) {
        var query = _db.JobRuns.Where(job => job.Status == JobRunStatus.Failed);
        if (type is not null) {
            query = query.Where(job => job.Type == type.Value);
        }

        var rows = await query.ToListAsync(cancellationToken);
        foreach (var row in rows) {
            row.Status = JobRunStatus.Cancelled;
            row.Message = "Cleared failure";
        }

        await _db.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    /// <summary>Compatibility claim for background consumers; all persisted work is graph-backed.</summary>
    public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken) =>
        ClaimNextGraphNodeAsync(workerId, JobGraphOrigin.Background, cancellationToken);

    /// <summary>
    /// Claims one dependency-ready node from a durable graph. PostgreSQL performs the selection and
    /// state transition under row locks; the in-memory provider follows the same predicates for tests.
    /// </summary>
    public async Task<JobRunSnapshot?> ClaimNextGraphNodeAsync(
        string workerId,
        JobGraphOrigin origin,
        CancellationToken cancellationToken,
        IReadOnlyCollection<JobResourceClass>? allowedResourceClasses = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        var now = DateTimeOffset.UtcNow;
        var allowed = allowedResourceClasses?.ToHashSet() ?? Enum.GetValues<JobResourceClass>().ToHashSet();
        var allowStandardCpu = allowed.Contains(JobResourceClass.StandardCpu);
        var allowHeavyCpu = allowed.Contains(JobResourceClass.HeavyCpu);
        var autoIdentifyBlocked = origin == JobGraphOrigin.Background
            && await HasPendingAutoIdentifyPrerequisiteAsync(cancellationToken);
        var audioLibraryAutoIdentifyBlocked = origin == JobGraphOrigin.Background
            && await HasPendingMusicArtistAutoIdentifyAsync(cancellationToken);

        Guid? claimedId;
        if (_db.Database.IsRelational()) {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            var claimed = await _db.Database.SqlQueryRaw<Guid>(
                """
                UPDATE job_runs AS claimed
                SET status = 'running',
                    locked_at = {0},
                    locked_by = {1},
                    started_at = COALESCE(started_at, {0}),
                    attempts = attempts + 1
                WHERE claimed.id = (
                    SELECT candidate.id
                    FROM job_runs AS candidate
                    INNER JOIN job_graphs AS graph ON graph.id = candidate.graph_id
                    WHERE candidate.status = 'queued'
                      AND candidate.available_at <= {0}
                      AND graph.origin = {2}
                      AND graph.status IN ('queued', 'running')
                      AND graph.cancellation_requested = FALSE
                      AND (
                          candidate.resource_class = 'light'
                          OR (candidate.resource_class = 'standard-cpu' AND {3})
                          OR (candidate.resource_class = 'heavy-cpu' AND {4})
                      )
                      AND ({5} = FALSE OR candidate.type <> {6} OR COALESCE(candidate.target_entity_kind, '') = ANY({7}))
                      AND ({8} = FALSE OR candidate.type <> {6} OR COALESCE(candidate.target_entity_kind, '') <> {9})
                      AND (
                          candidate.resource_key IS NULL
                          OR EXISTS (
                              SELECT 1
                              FROM job_resource_states AS resource
                              WHERE resource.key = candidate.resource_key
                                AND resource.next_available_at <= {0}
                                AND (
                                    SELECT COUNT(*)
                                    FROM job_resource_leases AS lease
                                    WHERE lease.resource_key = resource.key
                                      AND lease.expires_at > {0}
                                ) < resource.max_concurrency
                          )
                      )
                      AND NOT EXISTS (
                          SELECT 1
                          FROM job_dependencies AS dependency
                          INNER JOIN job_runs AS predecessor
                              ON predecessor.id = dependency.predecessor_job_run_id
                          WHERE dependency.successor_job_run_id = candidate.id
                            AND predecessor.status <> 'completed'
                      )
                      AND (
                          graph.origin <> 'interactive'
                          OR NOT EXISTS (
                              SELECT 1
                              FROM job_runs AS active
                              WHERE active.graph_id = graph.id
                                AND active.status = 'running'
                          )
                      )
                    ORDER BY graph.last_dispatched_at NULLS FIRST,
                             graph.created_at,
                             candidate.sequence,
                             candidate.available_at,
                             candidate.created_at
                    LIMIT 1
                    FOR UPDATE OF candidate SKIP LOCKED
                )
                RETURNING claimed.id
                """,
                now,
                workerId,
                origin.ToCode(),
                allowStandardCpu,
                allowHeavyCpu,
                autoIdentifyBlocked,
                AutoIdentifyJobTypeCode,
                TargetedAutoIdentifyKindCodes,
                audioLibraryAutoIdentifyBlocked,
                AudioLibraryKindCode).ToListAsync(cancellationToken);
            claimedId = claimed.Count == 0 ? null : claimed[0];
            if (claimedId is not null) {
                var claimDetails = await _db.JobRuns.AsNoTracking()
                    .Where(run => run.Id == claimedId)
                    .Select(run => new { run.GraphId, run.ResourceKey })
                    .SingleAsync(cancellationToken);
                if (claimDetails.ResourceKey is not null &&
                    !await TryAcquireResourceAsync(
                        claimDetails.ResourceKey,
                        claimedId.Value,
                        now,
                        cancellationToken)) {
                    await RequeueUnstartedClaimAsync(claimedId.Value, cancellationToken);
                    claimedId = null;
                } else if (claimDetails.GraphId is not null) {
                    await _db.JobGraphs
                        .Where(graph => graph.Id == claimDetails.GraphId)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(graph => graph.Status, JobGraphStatus.Running)
                                .SetProperty(graph => graph.LastDispatchedAt, now)
                                .SetProperty(graph => graph.UpdatedAt, now),
                            cancellationToken);
                }
            }

            // Resource acquisition uses tracked rows so the lease insert and next-eligible start
            // time share this claim transaction. The run/graph claims above use ExecuteUpdate;
            // without this explicit save the in-memory resource reservation would be discarded at
            // commit and concurrent workers could immediately exceed the declared policy.
            if (claimedId is not null) {
                await _db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        } else {
            var candidates = _db.JobRuns
                .Where(run => run.GraphId != null &&
                    run.Status == JobRunStatus.Queued &&
                    run.AvailableAt <= now &&
                    allowed.Contains(run.ResourceClass))
                .Join(
                    _db.JobGraphs.Where(graph => graph.Origin == origin &&
                        (graph.Status == JobGraphStatus.Queued || graph.Status == JobGraphStatus.Running) &&
                        !graph.CancellationRequested),
                    run => run.GraphId,
                    graph => graph.Id,
                    (run, graph) => new { Run = run, Graph = graph })
                .Where(candidate => !_db.JobDependencies
                    .Where(dependency => dependency.SuccessorJobRunId == candidate.Run.Id)
                    .Join(
                        _db.JobRuns,
                        dependency => dependency.PredecessorJobRunId,
                        predecessor => predecessor.Id,
                        (_, predecessor) => predecessor)
                    .Any(predecessor => predecessor.Status != JobRunStatus.Completed));
            if (autoIdentifyBlocked) {
                candidates = candidates.Where(candidate =>
                    candidate.Run.Type != JobType.AutoIdentify ||
                    TargetedAutoIdentifyKindCodeSet.Contains(candidate.Run.TargetEntityKind ?? string.Empty));
            }
            if (audioLibraryAutoIdentifyBlocked) {
                candidates = candidates.Where(candidate =>
                    candidate.Run.Type != JobType.AutoIdentify ||
                    candidate.Run.TargetEntityKind != AudioLibraryKindCode);
            }
            candidates = candidates.Where(candidate => candidate.Run.ResourceKey == null ||
                _db.JobResourceStates.Any(resource =>
                    resource.Key == candidate.Run.ResourceKey &&
                    resource.NextAvailableAt <= now &&
                    _db.JobResourceLeases.Count(lease =>
                        lease.ResourceKey == resource.Key && lease.ExpiresAt > now) < resource.MaxConcurrency));
            if (origin == JobGraphOrigin.Interactive) {
                candidates = candidates.Where(candidate => !_db.JobRuns.Any(active =>
                    active.GraphId == candidate.Graph.Id && active.Status == JobRunStatus.Running));
            }

            var selected = await candidates
                .OrderBy(candidate => candidate.Graph.LastDispatchedAt)
                .ThenBy(candidate => candidate.Graph.CreatedAt)
                .ThenBy(candidate => candidate.Run.Sequence)
                .ThenBy(candidate => candidate.Run.AvailableAt)
                .ThenBy(candidate => candidate.Run.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (selected is null) {
                return null;
            }

            if (selected.Run.ResourceKey is not null &&
                !await TryAcquireResourceAsync(selected.Run.ResourceKey, selected.Run.Id, now, cancellationToken)) {
                return null;
            }

            selected.Run.Status = JobRunStatus.Running;
            selected.Run.LockedAt = now;
            selected.Run.LockedBy = workerId;
            selected.Run.StartedAt ??= now;
            selected.Run.Attempts += 1;
            selected.Graph.Status = JobGraphStatus.Running;
            selected.Graph.LastDispatchedAt = now;
            selected.Graph.UpdatedAt = now;
            await _db.SaveChangesAsync(cancellationToken);
            claimedId = selected.Run.Id;
        }

        if (claimedId is null) {
            return null;
        }

        var claimedRow = await _db.JobRuns.AsNoTracking()
            .SingleAsync(run => run.Id == claimedId, cancellationToken);
        return ToSnapshot(claimedRow, origin);
    }

    private Task<bool> HasPendingAutoIdentifyPrerequisiteAsync(CancellationToken cancellationToken) =>
        _db.JobRuns.AsNoTracking().AnyAsync(job =>
            AutoIdentifyBarrierJobTypes.Contains(job.Type) &&
            (job.Status == JobRunStatus.Queued || job.Status == JobRunStatus.Running),
            cancellationToken);

    private Task<bool> HasPendingMusicArtistAutoIdentifyAsync(CancellationToken cancellationToken) =>
        _db.JobRuns.AsNoTracking().AnyAsync(job =>
            job.Type == JobType.AutoIdentify &&
            job.TargetEntityKind == MusicArtistKindCode &&
            (job.Status == JobRunStatus.Queued || job.Status == JobRunStatus.Running),
            cancellationToken);

    public async Task<int> RecoverStaleRunningAsync(
        string currentWorkerId,
        TimeSpan staleAfter,
        CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentWorkerId);
        if (staleAfter <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(staleAfter), staleAfter, "Stale timeout must be positive.");
        }

        await JobResourceDeclaration.RepairQueuedEntityResourcesAsync(_db, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var cutoff = now.Subtract(staleAfter);
        var rows = await _db.JobRuns
            .Where(job =>
                job.Status == JobRunStatus.Running &&
                job.LockedAt != null &&
                job.LockedAt <= cutoff &&
                job.LockedBy != currentWorkerId)
            .ToListAsync(cancellationToken);

        foreach (var row in rows) {
            row.Status = JobRunStatus.Queued;
            row.Progress = 0;
            row.Message = "Recovered from stale worker lease";
            row.AvailableAt = now;
            row.LockedAt = null;
            row.LockedBy = null;
            row.StartedAt = null;
            row.FinishedAt = null;
        }

        if (rows.Count > 0) {
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var row in rows) {
                await ReleaseResourceLeaseAsync(row.Id, cancellationToken);
            }
        }

        var orphanedGraphIds = await _db.JobGraphs.AsNoTracking()
            .Where(graph => graph.Status == JobGraphStatus.Queued
                || graph.Status == JobGraphStatus.Running
                || graph.Status == JobGraphStatus.Waiting)
            .Where(graph => !_db.JobRuns.Any(run => run.GraphId == graph.Id
                && (run.Status == JobRunStatus.Queued || run.Status == JobRunStatus.Running)))
            .Where(graph => !_db.JobGraphSignals.Any(signal => signal.GraphId == graph.Id
                && signal.ResolvedAt == null
                && signal.CancelledAt == null))
            .Select(graph => graph.Id)
            .ToArrayAsync(cancellationToken);
        var affectedGraphIds = rows
            .Where(row => row.GraphId is not null)
            .Select(row => row.GraphId!.Value)
            .Concat(orphanedGraphIds)
            .Distinct();
        foreach (var graphId in affectedGraphIds) {
            await ReconcileGraphStateAsync(graphId, cancellationToken);
        }

        return rows.Count;
    }

    public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) =>
        MutateRunAsync(id, row => {
            if (row.Status != JobRunStatus.Running) {
                return false;
            }

            row.Progress = Math.Clamp(progress, 0, 100);
            if (message is not null) {
                row.Message = message;
            }

            return true;
        }, cancellationToken);

    public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) =>
        MutateRunAsync(id, row => {
            if (row.Status != JobRunStatus.Running) {
                return false;
            }

            row.Status = JobRunStatus.Completed;
            row.Progress = 100;
            row.Message = message;
            row.LockedAt = null;
            row.LockedBy = null;
            row.FinishedAt = DateTimeOffset.UtcNow;
            return true;
        }, cancellationToken);

    public Task FailAsync(
        Guid id,
        string message,
        TimeSpan retryDelay,
        CancellationToken cancellationToken) =>
        MutateRunAsync(id, row => {
            if (row.Status != JobRunStatus.Running) {
                return false;
            }

            var shouldRetry = row.Attempts < row.MaxAttempts;
            row.Status = shouldRetry ? JobRunStatus.Queued : JobRunStatus.Failed;
            row.Message = message;
            row.LockedAt = null;
            row.LockedBy = null;
            row.AvailableAt = shouldRetry ? DateTimeOffset.UtcNow.Add(retryDelay) : row.AvailableAt;
            row.FinishedAt = shouldRetry ? null : DateTimeOffset.UtcNow;
            return true;
        }, cancellationToken);

    public Task DeferAsync(
        Guid id,
        string message,
        TimeSpan retryDelay,
        CancellationToken cancellationToken) =>
        MutateRunAsync(id, row => {
            if (row.Status != JobRunStatus.Running) {
                return false;
            }

            row.Status = JobRunStatus.Queued;
            row.Progress = 0;
            row.Message = message;
            row.Attempts = Math.Max(0, row.Attempts - 1);
            row.LockedAt = null;
            row.LockedBy = null;
            row.AvailableAt = DateTimeOffset.UtcNow.Add(retryDelay);
            row.StartedAt = null;
            row.FinishedAt = null;
            return true;
        }, cancellationToken);

    /// <summary>
    /// Loads a single job run, applies a mutation, and saves it, retrying on optimistic-concurrency
    /// conflicts. job_runs is written by both background workers and API endpoints; the xmin token
    /// turns a lost update into a <see cref="DbUpdateConcurrencyException"/>, which we resolve by
    /// reloading the current row state and re-evaluating the mutation (which may now be a no-op).
    /// </summary>
    /// <param name="id">Job run identifier.</param>
    /// <param name="mutate">Mutation returning true to persist, or false to abort without saving.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the row was mutated and saved; otherwise false.</returns>
    private async Task<bool> MutateRunAsync(
        Guid id,
        Func<JobRunRow, bool> mutate,
        CancellationToken cancellationToken) {
        const int maxConcurrencyRetries = 3;
        for (var attempt = 0; ; attempt++) {
            var row = await _db.JobRuns.FindAsync([id], cancellationToken);
            var wasRunning = row?.Status == JobRunStatus.Running;
            var previousStatus = row?.Status;
            if (row is null || !mutate(row)) {
                return false;
            }

            try {
                await _db.SaveChangesAsync(cancellationToken);
            } catch (DbUpdateConcurrencyException) when (attempt < maxConcurrencyRetries) {
                await _db.Entry(row).ReloadAsync(cancellationToken);
                continue;
            }

            if (wasRunning && row.Status != JobRunStatus.Running) {
                await ReleaseResourceLeaseAsync(id, cancellationToken);
            }
            if (previousStatus != row.Status) {
                await ReconcileGraphStateAsync(row.GraphId, cancellationToken);
            }
            return true;
        }
    }

    public async Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) {
        // Aggregated in SQL: this is polled by the jobs dashboard, and the run history can hold
        // hundreds of thousands of rows — materializing them (the previous implementation) cost
        // seconds per poll. The NSFW wall folds into the WHERE: a job is hidden when its target id
        // is the text form of an NSFW entity or library-root id, mirroring the row-level filter.
        var query = _db.JobRuns.AsNoTracking();
        if (hideNsfw) {
            query = query.Where(row => row.TargetEntityId == null
                || (!_db.Entities.Any(entity => entity.IsNsfw && entity.Id.ToString() == row.TargetEntityId)
                    && !_db.LibraryRoots.Any(root => root.IsNsfw && root.Id.ToString() == row.TargetEntityId)));
        }

        var rows = await query
            .GroupBy(row => new { row.Type, row.Status })
            .Select(group => new { group.Key.Type, group.Key.Status, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new JobQueueCount(r.Type.ToCode(), r.Status.ToCode(), r.Count))
            .ToList();
    }

    private async Task<IReadOnlyList<JobRunRow>> FilterVisibleRowsAsync(
        IQueryable<JobRunRow> query,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var rows = await query.ToListAsync(cancellationToken);
        if (!hideNsfw || rows.Count == 0) {
            return rows;
        }

        var targetIds = rows
            .Select(row => row.TargetEntityId)
            .Select(value => Guid.TryParse(value, out var id) ? id : (Guid?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        if (targetIds.Length == 0) {
            return rows;
        }

        var hiddenEntityIds = await _db.Entities.AsNoTracking()
            .Where(entity => entity.IsNsfw && targetIds.Contains(entity.Id))
            .Select(entity => entity.Id)
            .ToArrayAsync(cancellationToken);
        var hiddenRootIds = await _db.LibraryRoots.AsNoTracking()
            .Where(root => root.IsNsfw && targetIds.Contains(root.Id))
            .Select(root => root.Id)
            .ToArrayAsync(cancellationToken);
        var hiddenTargets = hiddenEntityIds.Concat(hiddenRootIds).ToHashSet();
        if (hiddenTargets.Count == 0) {
            return rows;
        }

        return rows
            .Where(row => !Guid.TryParse(row.TargetEntityId, out var id) || !hiddenTargets.Contains(id))
            .ToArray();
    }

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
        if (_db.Database.IsRelational()) {
            return await query.ExecuteDeleteAsync(cancellationToken);
        }

        var rows = await query.ToArrayAsync(cancellationToken);
        _db.JobRuns.RemoveRange(rows);
        await _db.SaveChangesAsync(cancellationToken);
        return rows.Length;
    }

}
