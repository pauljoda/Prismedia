using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Reads the durable roots that can resume managed file deletion after their source rows have already
/// disappeared. A root is recoverable when it owns the Entity lifecycle claim or a direct monitor still
/// carries the delete-files state; ordinary fileless Wanted Entities satisfy neither condition.
/// </summary>
internal sealed class EfEntityFileDeletionRecoveryProjection(PrismediaDbContext db)
    : IEntityFileDeletionRecoveryReader, IEntityLifecycleRecoveryStore {
    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> ResolveAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        if (entityIds.Count == 0) {
            return new HashSet<Guid>();
        }

        var distinctIds = entityIds.Distinct().ToArray();
        var lifecycleClaims = db.Entities.AsNoTracking()
            .Where(entity => distinctIds.Contains(entity.Id)
                && entity.LifecycleClaimKind == EntityLifecycleClaimKind.DeletingFiles)
            .Select(entity => entity.Id);
        var deletingMonitors = db.Monitors.AsNoTracking()
            .Where(monitor => monitor.EntityId != null
                && distinctIds.Contains(monitor.EntityId.Value)
                && monitor.Status == MonitorStatus.DeletingFiles)
            .Select(monitor => monitor.EntityId!.Value);

        return await lifecycleClaims
            .Concat(deletingMonitors)
            .ToHashSetAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EntityLifecycleRecoveryBatch> ListAsync(
        int limit,
        CancellationToken cancellationToken) {
        if (limit <= 0) {
            return new EntityLifecycleRecoveryBatch([], [], [], []);
        }

        var claimedEntityIds = await db.Entities.AsNoTracking()
            .Where(entity => entity.LifecycleClaimKind == EntityLifecycleClaimKind.DeletingFiles)
            .OrderBy(entity => entity.LifecycleClaimedAt)
            .ThenBy(entity => entity.UpdatedAt)
            .Select(entity => entity.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var remaining = limit - claimedEntityIds.Count;
        if (remaining > 0) {
            var monitorEntityIds = await db.Monitors.AsNoTracking()
                .Where(monitor => monitor.Status == MonitorStatus.DeletingFiles && monitor.EntityId != null)
                .Where(monitor => db.Entities.Any(entity => entity.Id == monitor.EntityId!.Value))
                .OrderBy(monitor => monitor.UpdatedAt)
                .Select(monitor => monitor.EntityId!.Value)
                .Distinct()
                .Take(remaining)
                .ToListAsync(cancellationToken);
            claimedEntityIds.AddRange(monitorEntityIds.Where(id => !claimedEntityIds.Contains(id)));
        }

        remaining = limit - claimedEntityIds.Count;
        if (remaining > 0) {
            var acquisitionEntityIds = await (
                    from monitor in db.Monitors.AsNoTracking()
                    join acquisition in db.Acquisitions.AsNoTracking()
                        on monitor.AcquisitionId equals acquisition.Id
                    where monitor.Status == MonitorStatus.DeletingFiles
                        && acquisition.EntityId != null
                        && db.Entities.Any(entity => entity.Id == acquisition.EntityId.Value)
                    orderby monitor.UpdatedAt
                    select acquisition.EntityId!.Value)
                .Distinct()
                .Take(remaining)
                .ToListAsync(cancellationToken);
            claimedEntityIds.AddRange(acquisitionEntityIds.Where(id => !claimedEntityIds.Contains(id)));
        }

        remaining = limit - claimedEntityIds.Count;
        var orphanedMonitorIds = remaining <= 0
            ? []
            : await db.Monitors.AsNoTracking()
                .Where(monitor => monitor.Status == MonitorStatus.DeletingFiles)
                .Where(monitor => monitor.EntityId == null
                    || !db.Entities.Any(entity => entity.Id == monitor.EntityId.Value))
                .Where(monitor => monitor.AcquisitionId == null
                    || !db.Acquisitions.Any(acquisition => acquisition.Id == monitor.AcquisitionId.Value))
                .Where(monitor => monitor.UpgradeChildAcquisitionId == null
                    || !db.Acquisitions.Any(acquisition => acquisition.Id == monitor.UpgradeChildAcquisitionId.Value))
                .OrderBy(monitor => monitor.UpdatedAt)
                .Select(monitor => monitor.Id)
                .Take(remaining)
                .ToListAsync(cancellationToken);

        remaining -= orphanedMonitorIds.Count;
        var stoppingMonitorIds = remaining <= 0
            ? []
            : await db.Monitors.AsNoTracking()
                .Where(monitor => monitor.Status == MonitorStatus.Stopping)
                .OrderBy(monitor => monitor.UpdatedAt)
                .Select(monitor => monitor.Id)
                .Take(remaining)
                .ToListAsync(cancellationToken);

        remaining -= stoppingMonitorIds.Count;
        var orphanedStoppingAcquisitionIds = remaining <= 0
            ? []
            : await db.Acquisitions.AsNoTracking()
                .Where(acquisition => acquisition.Status == AcquisitionStatus.Stopping
                    && acquisition.TeardownIntent == AcquisitionTeardownIntent.Remove
                    && acquisition.EntityId != null)
                .Where(acquisition => !db.Entities.Any(entity => entity.Id == acquisition.EntityId!.Value))
                .OrderBy(acquisition => acquisition.UpdatedAt)
                .Select(acquisition => acquisition.Id)
                .Take(remaining)
                .ToListAsync(cancellationToken);

        return new EntityLifecycleRecoveryBatch(
            claimedEntityIds,
            orphanedMonitorIds,
            stoppingMonitorIds,
            orphanedStoppingAcquisitionIds);
    }

    /// <inheritdoc />
    public async Task<bool> CompleteOrphanedDeletionAsync(
        Guid monitorId,
        CancellationToken cancellationToken) {
        var candidate = db.Monitors
            .Where(monitor => monitor.Id == monitorId
                && monitor.Status == MonitorStatus.DeletingFiles)
            .Where(monitor => monitor.EntityId == null
                || !db.Entities.Any(entity => entity.Id == monitor.EntityId.Value))
            .Where(monitor => monitor.AcquisitionId == null
                || !db.Acquisitions.Any(acquisition => acquisition.Id == monitor.AcquisitionId.Value))
            .Where(monitor => monitor.UpgradeChildAcquisitionId == null
                || !db.Acquisitions.Any(acquisition => acquisition.Id == monitor.UpgradeChildAcquisitionId.Value));
        if (db.Database.IsRelational()) {
            return await candidate.ExecuteDeleteAsync(cancellationToken) == 1;
        }

        var row = await candidate.SingleOrDefaultAsync(cancellationToken);
        if (row is null) {
            return false;
        }
        db.Monitors.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
