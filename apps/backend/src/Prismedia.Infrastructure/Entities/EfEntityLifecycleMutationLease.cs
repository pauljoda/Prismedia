using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// PostgreSQL-backed Entity lifecycle lease. Monitor rows are locked first for compatibility with
/// unmonitor/provider discovery, followed by the stable Entity ancestry used by monitorless and
/// source-backed trees. Every caller therefore observes one deterministic lifecycle winner.
/// </summary>
public sealed class EfEntityLifecycleMutationLease(
    PrismediaDbContext db,
    IEntityHierarchyReader hierarchy) : IEntityLifecycleMutationLease {
    /// <inheritdoc />
    public Task<bool> ExecuteAsync(
        Guid entityId,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken) =>
        ExecuteManyAsync([entityId], mutation, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> ExecuteManyAsync(
        IReadOnlyCollection<Guid> entityIds,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken) {
        var targetEntityIds = entityIds.Distinct().Order().ToArray();
        if (targetEntityIds.Length == 0) {
            await mutation(cancellationToken);
            return true;
        }

        IDbContextTransaction? transaction = null;
        var ownsTransaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null;
        if (ownsTransaction) {
            transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        }

        try {
            var lifecycleEntityIds = new HashSet<Guid>(targetEntityIds);
            lifecycleEntityIds.UnionWith(await hierarchy.ListAncestorIdsAsync(
                targetEntityIds,
                cancellationToken));
            var orderedLifecycleEntityIds = lifecycleEntityIds.Order().ToArray();
            var monitorIds = await ListMonitorIdsTargetingAsync(
                orderedLifecycleEntityIds,
                cancellationToken);

            var lockedMonitors = (await LockMonitorsAsync(monitorIds, cancellationToken)).ToList();
            var lockedEntities = await LockEntitiesAsync(orderedLifecycleEntityIds, cancellationToken);
            var lockedEntityIds = lockedEntities.Select(row => row.Id).ToHashSet();
            if (targetEntityIds.Any(targetEntityId => !lockedEntityIds.Contains(targetEntityId))
                || lockedEntities.Any(row => row.LifecycleClaimKind != null)) {
                if (transaction is not null) {
                    await transaction.RollbackAsync(cancellationToken);
                }
                return false;
            }

            // The Entity lock may have waited behind a monitorless explicit intent that published a new
            // monitor. Re-read after acquiring the serialization anchor and include that committed row.
            var refreshedMonitorIds = await ListMonitorIdsTargetingAsync(
                orderedLifecycleEntityIds,
                cancellationToken);
            lockedMonitors.AddRange(await LockMonitorsAsync(
                refreshedMonitorIds.Except(monitorIds).ToArray(),
                cancellationToken));
            if (lockedMonitors.Any(row => row.Status is MonitorStatus.Stopping or MonitorStatus.DeletingFiles)) {
                if (transaction is not null) {
                    await transaction.RollbackAsync(cancellationToken);
                }
                return false;
            }

            await mutation(cancellationToken);
            if (transaction is not null) {
                await transaction.CommitAsync(cancellationToken);
            }
            return true;
        } finally {
            if (transaction is not null) {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<IReadOnlyList<MonitorRow>> LockMonitorsAsync(
        IReadOnlyCollection<Guid> monitorIds,
        CancellationToken cancellationToken) {
        var ids = monitorIds.Distinct().Order().ToArray();
        if (ids.Length == 0) {
            return [];
        }

        return db.Database.IsRelational()
            && db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
                ? await db.Monitors
                    .FromSqlInterpolated($"SELECT * FROM monitors WHERE id = ANY ({ids}) ORDER BY id FOR UPDATE")
                    .AsNoTracking()
                    .ToArrayAsync(cancellationToken)
                : await db.Monitors.AsNoTracking()
                    .Where(row => ids.Contains(row.Id))
                    .OrderBy(row => row.Id)
                    .ToArrayAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<EntityRow>> LockEntitiesAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        var ids = entityIds.Distinct().Order().ToArray();
        if (ids.Length == 0) {
            return [];
        }

        return db.Database.IsRelational()
            && db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
                ? await db.Entities
                    // The entity model maps PostgreSQL's xmin system column for optimistic
                    // concurrency. FromSql is composed as a subquery, so project xmin explicitly;
                    // SELECT * alone does not expose PostgreSQL system columns to that outer query.
                    .FromSqlInterpolated($"SELECT *, xmin FROM entities WHERE id = ANY ({ids}) ORDER BY id FOR UPDATE")
                    .AsNoTracking()
                    .ToArrayAsync(cancellationToken)
                : await db.Entities.AsNoTracking()
                    .Where(row => ids.Contains(row.Id))
                    .OrderBy(row => row.Id)
                    .ToArrayAsync(cancellationToken);
    }

    private async Task<IReadOnlySet<Guid>> ListMonitorIdsTargetingAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        var ids = entityIds.Distinct().ToArray();
        var acquisitionIds = await db.Acquisitions.AsNoTracking()
            .Where(row => row.EntityId != null && ids.Contains(row.EntityId.Value))
            .Select(row => row.Id)
            .ToArrayAsync(cancellationToken);
        return await db.Monitors.AsNoTracking()
            .Where(row => (row.EntityId != null && ids.Contains(row.EntityId.Value))
                || (row.AcquisitionId != null && acquisitionIds.Contains(row.AcquisitionId.Value)))
            .Select(row => row.Id)
            .ToHashSetAsync(cancellationToken);
    }
}
