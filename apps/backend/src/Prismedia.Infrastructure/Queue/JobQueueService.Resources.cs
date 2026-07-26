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
        var row = await _db.JobResourceStates.FindAsync([resourceKey], cancellationToken);
        if (row is null) {
            _db.JobResourceStates.Add(new JobResourceStateRow {
                Key = resourceKey,
                MaxConcurrency = maxConcurrency,
                MinimumStartIntervalMs = checked((int)minimumStartInterval.TotalMilliseconds),
                NextAvailableAt = DateTimeOffset.MinValue,
                UpdatedAt = now
            });
        } else {
            row.MaxConcurrency = maxConcurrency;
            row.MinimumStartIntervalMs = checked((int)minimumStartInterval.TotalMilliseconds);
            row.UpdatedAt = now;
        }

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
                .FromSqlInterpolated($"SELECT * FROM job_resource_states WHERE key = {resourceKey} FOR UPDATE")
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
