using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Queue;

public sealed class JobQueueService : IJobQueueService {
    private readonly PrismediaDbContext _db;

    public JobQueueService(PrismediaDbContext db) {
        _db = db;
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

    /// <summary>
    /// Library scan job types. Each scan covers every enabled root of its kind, so only one of each
    /// may be queued or running at a time — see the singleton guard in <see cref="EnqueueAsync(EnqueueJobRequest, CancellationToken)"/>.
    /// </summary>
    private static readonly JobType[] ScanJobTypes =
        [JobType.ScanLibrary, JobType.ScanGallery, JobType.ScanBook, JobType.ScanAudio];

    public async Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) {
        return await EnqueueAsync(new EnqueueJobRequest(type), cancellationToken);
    }

    public async Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
        // Scans are per-kind singletons: a scan job already walks every enabled root of its kind
        // (skipping unchanged ones), so a second scan of the same kind would only duplicate work. When
        // one is already queued or running, return the in-flight job instead of stacking another.
        if (ScanJobTypes.Contains(request.Type)) {
            var existing = await _db.JobRuns.AsNoTracking()
                .Where(job => job.Type == request.Type &&
                              (job.Status == JobRunStatus.Queued || job.Status == JobRunStatus.Running))
                .OrderBy(job => job.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null) {
                return ToSnapshot(existing);
            }
        }

        if (request.TargetEntityId is not null) {
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

        var now = DateTimeOffset.UtcNow;
        var row = new JobRunRow {
            Id = Guid.NewGuid(),
            Type = request.Type,
            Status = JobRunStatus.Queued,
            PayloadJson = request.PayloadJson ?? "{}",
            Priority = request.Priority,
            Lane = request.Lane,
            Attempts = 0,
            MaxAttempts = 3,
            Progress = 0,
            TargetEntityKind = request.TargetEntityKind,
            TargetEntityId = request.TargetEntityId,
            TargetLabel = request.TargetLabel,
            AvailableAt = now,
            CreatedAt = now
        };

        _db.JobRuns.Add(row);
        try {
            await _db.SaveChangesAsync(cancellationToken);
        } catch (DbUpdateException) when (request.TargetEntityId is not null) {
            _db.Entry(row).State = EntityState.Detached;
            var existing = await _db.JobRuns.AsNoTracking()
                .Where(job => job.Type == request.Type &&
                              job.TargetEntityId == request.TargetEntityId &&
                              (job.Status == JobRunStatus.Queued || job.Status == JobRunStatus.Running))
                .OrderBy(job => job.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is null) {
                throw;
            }

            return ToSnapshot(existing);
        }

        return ToSnapshot(row);
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

            _db.JobRuns.Add(new JobRunRow {
                Id = Guid.NewGuid(),
                Type = request.Type,
                Status = JobRunStatus.Queued,
                PayloadJson = request.PayloadJson ?? "{}",
                Priority = request.Priority,
                Lane = request.Lane,
                Attempts = 0,
                MaxAttempts = 3,
                Progress = 0,
                TargetEntityKind = request.TargetEntityKind,
                TargetEntityId = request.TargetEntityId,
                TargetLabel = request.TargetLabel,
                AvailableAt = now,
                CreatedAt = now
            });
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

        var rows = await query.ToListAsync(cancellationToken);
        foreach (var row in rows) {
            row.Status = JobRunStatus.Cancelled;
            row.Message = "Cancelled";
            row.LockedAt = null;
            row.LockedBy = null;
            row.FinishedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
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

    /// <summary>
    /// Claims the next available job, optionally restricted to one foreground lane. Uses atomic
    /// FOR UPDATE SKIP LOCKED on PostgreSQL for safe concurrent access, with an EF Core fallback for
    /// test providers.
    /// </summary>
    public async Task<JobRunSnapshot?> ClaimNextAsync(
        string workerId,
        CancellationToken cancellationToken,
        JobRunLane? lane = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        var now = DateTimeOffset.UtcNow;

        if (_db.Database.IsRelational()) {
            var claimed = lane is null
                ? await _db.Database.SqlQueryRaw<Guid>(
                    """
                    UPDATE job_runs
                    SET status = 'running',
                        locked_at = {0},
                        locked_by = {1},
                        started_at = COALESCE(started_at, {0}),
                        attempts = attempts + 1
                    WHERE id = (
                        SELECT id FROM job_runs
                        WHERE status = 'queued' AND available_at <= {0}
                        ORDER BY priority DESC, CASE WHEN lane = {2} THEN 1 ELSE 0 END DESC, available_at, created_at
                        LIMIT 1
                        FOR UPDATE SKIP LOCKED
                    )
                    RETURNING id
                    """,
                    now, workerId, JobRunLane.ForegroundIdentify.ToCode()).ToListAsync(cancellationToken)
                : await _db.Database.SqlQueryRaw<Guid>(
                    """
                    UPDATE job_runs
                    SET status = 'running',
                        locked_at = {0},
                        locked_by = {1},
                        started_at = COALESCE(started_at, {0}),
                        attempts = attempts + 1
                    WHERE id = (
                        SELECT id FROM job_runs
                        WHERE status = 'queued' AND available_at <= {0} AND lane = {2}
                        ORDER BY priority DESC, available_at, created_at
                        LIMIT 1
                        FOR UPDATE SKIP LOCKED
                    )
                    RETURNING id
                    """,
                    now, workerId, lane.Value.ToCode()).ToListAsync(cancellationToken);

            if (claimed.Count == 0) {
                return null;
            }

            var claimedRow = await _db.JobRuns.FindAsync([claimed[0]], cancellationToken);
            return claimedRow is null ? null : ToSnapshot(claimedRow);
        }

        var query = _db.JobRuns
            .Where(job => job.Status == JobRunStatus.Queued && job.AvailableAt <= now);
        if (lane is not null) {
            query = query.Where(job => job.Lane == lane);
        }

        var row = await query
            .OrderByDescending(job => job.Priority)
            .ThenByDescending(job => job.Lane == JobRunLane.ForegroundIdentify)
            .ThenBy(job => job.AvailableAt)
            .ThenBy(job => job.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null) {
            return null;
        }

        row.Status = JobRunStatus.Running;
        row.LockedAt = now;
        row.LockedBy = workerId;
        row.StartedAt ??= now;
        row.Attempts += 1;
        await _db.SaveChangesAsync(cancellationToken);

        return ToSnapshot(row);
    }

    public async Task<int> RecoverStaleRunningAsync(
        string currentWorkerId,
        TimeSpan staleAfter,
        CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentWorkerId);
        if (staleAfter <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(staleAfter), staleAfter, "Stale timeout must be positive.");
        }

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
            if (row is null || !mutate(row)) {
                return false;
            }

            try {
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            } catch (DbUpdateConcurrencyException) when (attempt < maxConcurrencyRetries) {
                await _db.Entry(row).ReloadAsync(cancellationToken);
            }
        }
    }

    public async Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) {
        var query = _db.JobRuns.AsNoTracking();
        var visibleRows = await FilterVisibleRowsAsync(query, hideNsfw, cancellationToken);
        var rows = visibleRows
            .GroupBy(r => new { r.Type, r.Status })
            .Select(g => new { g.Key.Type, g.Key.Status, Count = g.Count() })
            .ToList();

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
        return await _db.JobRuns
            .Where(job =>
                (job.Status == JobRunStatus.Completed || job.Status == JobRunStatus.Cancelled) &&
                job.FinishedAt != null &&
                job.FinishedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static JobRunSnapshot ToSnapshot(JobRunRow row) {
        return new JobRunSnapshot(
            row.Id,
            row.Type,
            row.Status,
            row.Progress,
            row.Message,
            row.PayloadJson,
            row.TargetEntityKind,
            row.TargetEntityId,
            row.TargetLabel,
            row.CreatedAt,
            row.StartedAt,
            row.FinishedAt,
            row.Attempts,
            row.MaxAttempts,
            row.Lane);
    }
}
