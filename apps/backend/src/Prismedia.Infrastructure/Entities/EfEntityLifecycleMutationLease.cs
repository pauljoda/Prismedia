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
            foreach (var targetEntityId in targetEntityIds) {
                lifecycleEntityIds.UnionWith(await hierarchy.ListAncestorIdsAsync(
                    targetEntityId,
                    cancellationToken));
            }
            var orderedLifecycleEntityIds = lifecycleEntityIds.Order().ToArray();
            var monitorIds = await ListMonitorIdsTargetingAsync(
                orderedLifecycleEntityIds,
                cancellationToken);

            var lockedMonitors = new List<MonitorRow>(monitorIds.Count);
            foreach (var monitorId in monitorIds.Order()) {
                var locked = await LockMonitorAsync(monitorId, cancellationToken);
                if (locked is not null) {
                    lockedMonitors.Add(locked);
                }
            }

            var lockedEntities = new List<EntityRow>(orderedLifecycleEntityIds.Length);
            foreach (var lifecycleEntityId in orderedLifecycleEntityIds) {
                var locked = await LockEntityAsync(lifecycleEntityId, cancellationToken);
                if (locked is not null) {
                    lockedEntities.Add(locked);
                }
            }
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
            foreach (var monitorId in refreshedMonitorIds.Except(monitorIds).Order()) {
                var locked = await LockMonitorAsync(monitorId, cancellationToken);
                if (locked is not null) {
                    lockedMonitors.Add(locked);
                }
            }
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

    private Task<MonitorRow?> LockMonitorAsync(Guid monitorId, CancellationToken cancellationToken) =>
        db.Database.IsRelational()
        && db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
            ? db.Monitors
                .FromSqlInterpolated($"SELECT * FROM monitors WHERE id = {monitorId} FOR UPDATE")
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken)
            : db.Monitors.FirstOrDefaultAsync(row => row.Id == monitorId, cancellationToken);

    private Task<EntityRow?> LockEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        db.Database.IsRelational()
        && db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
            ? db.Entities
                .FromSqlInterpolated($"SELECT * FROM entities WHERE id = {entityId} FOR UPDATE")
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken)
            : db.Entities.FirstOrDefaultAsync(row => row.Id == entityId, cancellationToken);

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
