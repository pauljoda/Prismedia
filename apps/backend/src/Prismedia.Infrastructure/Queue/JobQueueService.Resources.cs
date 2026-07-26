using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Queue;

public sealed partial class JobQueueService {
    private static readonly TimeSpan ResourceLeaseDuration = TimeSpan.FromMinutes(5);

    public async Task DeclareResourceAsync(
        string resourceKey,
        int maxConcurrency,
        TimeSpan minimumStartInterval,
        CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        if (maxConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        if (minimumStartInterval < TimeSpan.Zero || minimumStartInterval > TimeSpan.FromDays(1)) {
            throw new ArgumentOutOfRangeException(nameof(minimumStartInterval));
        }

        var now = DateTimeOffset.UtcNow;
        var minimumIntervalMs = checked((int)minimumStartInterval.TotalMilliseconds);
        if (_db.Database.IsRelational()) {
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO job_resource_states
                    (key, max_concurrency, minimum_start_interval_ms, next_available_at, updated_at)
                VALUES
                    ({resourceKey}, {maxConcurrency}, {minimumIntervalMs}, {now}, {now})
                ON CONFLICT (key) DO UPDATE
                SET max_concurrency = EXCLUDED.max_concurrency,
                    minimum_start_interval_ms = EXCLUDED.minimum_start_interval_ms,
                    updated_at = EXCLUDED.updated_at
                """, cancellationToken);
            return;
        }

        var row = await _db.JobResourceStates.FindAsync([resourceKey], cancellationToken);
        if (row is null) {
            _db.JobResourceStates.Add(new JobResourceStateRow {
                Key = resourceKey,
                MaxConcurrency = maxConcurrency,
                MinimumStartIntervalMs = minimumIntervalMs,
                NextAvailableAt = DateTimeOffset.MinValue,
                UpdatedAt = now
            });
        } else {
            row.MaxConcurrency = maxConcurrency;
            row.MinimumStartIntervalMs = minimumIntervalMs;
            row.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task HeartbeatAsync(
        Guid id,
        string workerId,
        CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        var now = DateTimeOffset.UtcNow;
        if (_db.Database.IsRelational()) {
            await _db.JobRuns
                .Where(run => run.Id == id && run.Status == JobRunStatus.Running && run.LockedBy == workerId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(run => run.LockedAt, now), cancellationToken);
            await _db.JobResourceLeases
                .Where(lease => lease.JobRunId == id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(lease => lease.ExpiresAt, now.Add(ResourceLeaseDuration)),
                    cancellationToken);
            return;
        }

        var run = await _db.JobRuns.SingleOrDefaultAsync(
            candidate => candidate.Id == id && candidate.Status == JobRunStatus.Running && candidate.LockedBy == workerId,
            cancellationToken);
        if (run is null) return;
        run.LockedAt = now;
        var leases = await _db.JobResourceLeases
            .Where(lease => lease.JobRunId == id)
            .ToArrayAsync(cancellationToken);
        foreach (var lease in leases) lease.ExpiresAt = now.Add(ResourceLeaseDuration);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> TryAcquireResourceAsync(
        string resourceKey,
        Guid jobRunId,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        JobResourceStateRow? resource;
        if (_db.Database.IsRelational()) {
            await _db.JobResourceLeases
                .Where(lease => lease.ResourceKey == resourceKey && lease.ExpiresAt <= now)
                .ExecuteDeleteAsync(cancellationToken);
            resource = await _db.JobResourceStates
                // PostgreSQL system columns are not part of SELECT *. EF composes this query as a
                // subquery and projects the mapped xmin concurrency token from its outer alias, so
                // xmin must be named explicitly or the first real resource claim fails at runtime.
                .FromSqlInterpolated($"SELECT *, xmin FROM job_resource_states WHERE key = {resourceKey} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
        } else {
            var expired = await _db.JobResourceLeases
                .Where(lease => lease.ResourceKey == resourceKey && lease.ExpiresAt <= now)
                .ToListAsync(cancellationToken);
            _db.JobResourceLeases.RemoveRange(expired);
            resource = await _db.JobResourceStates.FindAsync([resourceKey], cancellationToken);
        }

        if (resource is null || resource.NextAvailableAt > now) return false;

        var active = await _db.JobResourceLeases.CountAsync(
            lease => lease.ResourceKey == resourceKey && lease.ExpiresAt > now,
            cancellationToken);
        if (active >= resource.MaxConcurrency) return false;

        _db.JobResourceLeases.Add(new JobResourceLeaseRow {
            ResourceKey = resourceKey,
            JobRunId = jobRunId,
            ExpiresAt = now.Add(ResourceLeaseDuration)
        });
        resource.NextAvailableAt = now.AddMilliseconds(resource.MinimumStartIntervalMs);
        resource.UpdatedAt = now;
        return true;
    }

    private async Task ReleaseResourceLeaseAsync(Guid jobRunId, CancellationToken cancellationToken) {
        if (_db.Database.IsRelational()) {
            await _db.JobResourceLeases
                .Where(lease => lease.JobRunId == jobRunId)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var leases = await _db.JobResourceLeases
            .Where(lease => lease.JobRunId == jobRunId)
            .ToListAsync(cancellationToken);
        if (leases.Count == 0) return;

        _db.JobResourceLeases.RemoveRange(leases);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RequeueUnstartedClaimAsync(Guid jobRunId, CancellationToken cancellationToken) {
        await _db.JobRuns
            .Where(run => run.Id == jobRunId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(run => run.Status, JobRunStatus.Queued)
                    .SetProperty(run => run.LockedAt, (DateTimeOffset?)null)
                    .SetProperty(run => run.LockedBy, (string?)null)
                    .SetProperty(run => run.StartedAt, (DateTimeOffset?)null)
                    .SetProperty(run => run.Attempts, run => run.Attempts - 1),
                cancellationToken);
    }
}
