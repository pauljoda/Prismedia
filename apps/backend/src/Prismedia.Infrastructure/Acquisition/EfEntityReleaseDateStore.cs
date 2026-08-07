using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Reads canonical release milestones from the shared Entity dates capability.</summary>
public sealed class EfEntityReleaseDateStore(PrismediaDbContext db) : IEntityReleaseDateStore {
    public async Task<EntityDate?> GetAsync(
        Guid entityId,
        EntityDateType type,
        CancellationToken cancellationToken) {
        var row = await db.EntityDates.AsNoTracking()
            .FirstOrDefaultAsync(
                date => date.EntityId == entityId && date.Code == type.ToCode(),
                cancellationToken);
        return row is null
            ? null
            : new EntityDate(row.Code, row.Value, row.SortableValue, row.Precision);
    }

    /// <inheritdoc />
    public async Task<EntityReleaseDateCoverage> GetDirectChildCoverageAsync(
        Guid parentEntityId,
        EntityKind childKind,
        EntityDateType type,
        CancellationToken cancellationToken) {
        var childIds = await db.Entities.AsNoTracking()
            .Where(entity => entity.ParentEntityId == parentEntityId
                && entity.KindCode == childKind.ToCode())
            .Select(entity => entity.Id)
            .ToArrayAsync(cancellationToken);
        if (childIds.Length == 0) {
            return new EntityReleaseDateCoverage(0, 0, LatestDate: null);
        }

        var datedChildren = await db.EntityDates.AsNoTracking()
            .Where(date => childIds.Contains(date.EntityId)
                && date.Code == type.ToCode()
                && date.SortableValue != null)
            .ToArrayAsync(cancellationToken);
        var latest = datedChildren
            .OrderByDescending(date => date.SortableValue)
            .FirstOrDefault();
        return new EntityReleaseDateCoverage(
            childIds.Length,
            datedChildren.Select(date => date.EntityId).Distinct().Count(),
            latest is null
                ? null
                : new EntityDate(latest.Code, latest.Value, latest.SortableValue, latest.Precision));
    }
}
