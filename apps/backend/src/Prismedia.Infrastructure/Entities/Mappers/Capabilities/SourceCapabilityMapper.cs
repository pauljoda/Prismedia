using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

internal sealed class SourceCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var rows = await db.EntitySources.AsNoTracking()
            .Where(r => r.EntityId == entity.Id)
            .OrderBy(r => r.Code)
            .ToArrayAsync(cancellationToken);
        if (rows.Length == 0) {
            return;
        }

        entity.RemoveCapability<CapabilitySource>();
        entity.AddCapability(new CapabilitySource(
            rows.Select(r => new CapabilitySource.Item(r.Code, r.Value)).ToArray()));
    }

    public Task ClearAsync(Entity entity, CancellationToken cancellationToken) {
        db.EntitySources.RemoveRange(db.EntitySources.Where(r => r.EntityId == entity.Id));
        return Task.CompletedTask;
    }

    public Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity.Source is not { } source) {
            return Task.CompletedTask;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var item in source.Items) {
            db.EntitySources.Add(new EntitySourceRow {
                EntityId = entity.Id,
                Code = item.Code,
                Value = item.Value,
                UpdatedAt = now,
            });
        }

        return Task.CompletedTask;
    }
}
