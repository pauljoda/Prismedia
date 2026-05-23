using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

internal sealed class DatesCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var rows = await db.EntityDates.AsNoTracking()
            .Where(r => r.EntityId == entity.Id)
            .OrderBy(r => r.Code)
            .ToArrayAsync(cancellationToken);
        if (rows.Length == 0) {
            return;
        }

        entity.RemoveCapability<CapabilityDates>();
        entity.AddCapability(new CapabilityDates(rows.Select(r =>
            new EntityDate(r.Code, r.Value, r.SortableValue, r.Precision)).ToArray()));
    }

    public Task ClearAsync(Entity entity, CancellationToken cancellationToken) {
        db.EntityDates.RemoveRange(db.EntityDates.Where(r => r.EntityId == entity.Id));
        return Task.CompletedTask;
    }

    public Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity.Dates is not { } dates) {
            return Task.CompletedTask;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var date in dates.Items) {
            db.EntityDates.Add(new EntityDateRow {
                EntityId = entity.Id,
                Code = date.Code,
                Value = date.Value,
                SortableValue = date.SortableValue,
                Precision = date.Precision,
                UpdatedAt = now,
            });
        }

        return Task.CompletedTask;
    }
}
