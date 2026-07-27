using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Queue;

/// <summary>
/// Resolves and persists scheduler-owned resource policies that are implicit in graph nodes.
/// External resources remain explicitly declared by their adapters; entity mutation gates always
/// receive the canonical single-writer policy at the lowest graph persistence boundary.
/// </summary>
internal static class JobResourceDeclaration {
    public static string? Resolve(GraphJobNodeRequest request) =>
        request.Job.ResourceKey
        ?? request.ResourceKey
        ?? EntityKey(request.Job);

    public static string? EntityKey(EnqueueJobRequest request) =>
        request.TargetEntityId is null || request.TargetEntityKind is null
            ? null
            : JobResourceKeys.Entity(request.TargetEntityId);

    public static async Task EnsureImplicitAsync(
        PrismediaDbContext db,
        string? resourceKey,
        CancellationToken cancellationToken) {
        if (resourceKey is null || !JobResourceKeys.IsEntity(resourceKey)) return;

        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO job_resource_states
                    (key, max_concurrency, minimum_start_interval_ms, next_available_at, updated_at)
                VALUES
                    ({resourceKey}, {1}, {0}, {DateTimeOffset.MinValue}, {now})
                ON CONFLICT (key) DO NOTHING
                """, cancellationToken);
            return;
        }

        if (db.JobResourceStates.Local.Any(resource => resource.Key == resourceKey)
            || await db.JobResourceStates.AnyAsync(resource => resource.Key == resourceKey, cancellationToken)) {
            return;
        }

        db.JobResourceStates.Add(new JobResourceStateRow {
            Key = resourceKey,
            MaxConcurrency = 1,
            MinimumStartIntervalMs = 0,
            NextAvailableAt = DateTimeOffset.MinValue,
            UpdatedAt = now
        });
    }

    public static async Task RepairQueuedEntityResourcesAsync(
        PrismediaDbContext db,
        CancellationToken cancellationToken) {
        if (db.Database.IsRelational()) {
            var queued = JobRunStatus.Queued.ToCode();
            var entityPattern = $"{JobResourceKeys.EntityPrefix}%";
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO job_resource_states
                    (key, max_concurrency, minimum_start_interval_ms, next_available_at, updated_at)
                SELECT DISTINCT
                    run.resource_key, {1}, {0}, {DateTimeOffset.MinValue}, {now}
                FROM job_runs AS run
                WHERE run.status = {queued}
                  AND run.resource_key LIKE {entityPattern}
                ON CONFLICT (key) DO NOTHING
                """, cancellationToken);
            return;
        }

        var keys = await db.JobRuns.AsNoTracking()
            .Where(run => run.Status == JobRunStatus.Queued
                && run.ResourceKey != null
                && run.ResourceKey.StartsWith(JobResourceKeys.EntityPrefix))
            .Select(run => run.ResourceKey!)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        foreach (var key in keys) {
            await EnsureImplicitAsync(db, key, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
