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
}
