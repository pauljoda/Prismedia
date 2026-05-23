using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

internal sealed class LinksCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var urls = await db.EntityUrls.AsNoTracking()
            .Where(r => r.EntityId == entity.Id)
            .OrderBy(r => r.SortOrder)
            .ToArrayAsync(cancellationToken);
        var externalIds = await db.EntityExternalIds.AsNoTracking()
            .Where(r => r.EntityId == entity.Id)
            .ToArrayAsync(cancellationToken);
        if (urls.Length == 0 && externalIds.Length == 0) {
            return;
        }

        entity.RemoveCapability<CapabilityLinks>();
        entity.AddCapability(new CapabilityLinks(
            urls.Select(r => new CapabilityLinks.Url(r.Url, r.Label)).ToArray(),
            externalIds.Select(r => new CapabilityLinks.ExternalId(r.Provider, r.Value, r.Url)).ToArray()));
    }

    public Task ClearAsync(Entity entity, CancellationToken cancellationToken) {
        db.EntityUrls.RemoveRange(db.EntityUrls.Where(r => r.EntityId == entity.Id));
        db.EntityExternalIds.RemoveRange(db.EntityExternalIds.Where(r => r.EntityId == entity.Id));
        return Task.CompletedTask;
    }

    public Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity.GetCapability<CapabilityLinks>() is not { } links) {
            return Task.CompletedTask;
        }

        var now = DateTimeOffset.UtcNow;
        var order = 0;
        foreach (var url in links.Urls) {
            db.EntityUrls.Add(new EntityUrlRow {
                Id = Guid.NewGuid(),
                EntityId = entity.Id,
                Url = url.Value,
                Label = url.Label,
                SortOrder = order++,
                CreatedAt = now,
            });
        }

        foreach (var externalId in links.ExternalIds) {
            db.EntityExternalIds.Add(new EntityExternalIdRow {
                Id = Guid.NewGuid(),
                EntityId = entity.Id,
                Provider = externalId.Provider,
                Value = externalId.Value,
                Url = externalId.Url,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        return Task.CompletedTask;
    }
}
