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
    : IEntityFileDeletionRecoveryReader {
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
}
