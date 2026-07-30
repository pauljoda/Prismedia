using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Kinds;

internal sealed class CollectionKindMapper(PrismediaDbContext db) : IEntityKindMapper {
    public EntityKind Kind => EntityKind.Collection;

    public async Task<Entity> ConstructAsync(EntityRow row, CancellationToken cancellationToken) {
        var detail = await db.CollectionDetails.AsNoTracking()
            .FirstOrDefaultAsync(d => d.EntityId == row.Id, cancellationToken);
        return detail is null
            ? throw new InvalidOperationException($"Collection '{row.Id}' is missing ownership details.")
            : new Collection(
                row.Id,
                row.Title,
                detail.OwnerUserId,
                detail.Mode,
                detail.RuleTreeJson,
                detail.CoverMode,
                detail.CoverItemEntityId,
                detail.LastRefreshedAt,
                detail.IsShared);
    }

    public async Task PersistDetailAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity is not Collection collection) {
            return;
        }

        var row = await db.CollectionDetails.FindAsync([entity.Id], cancellationToken)
            ?? Track(new CollectionDetailRow { EntityId = entity.Id });
        row.Mode = collection.Mode;
        row.OwnerUserId = collection.OwnerUserId;
        row.IsShared = collection.IsShared;
        row.RuleTreeJson = collection.RuleTreeJson;
        row.CoverMode = collection.CoverMode;
        row.CoverItemEntityId = collection.CoverItemId;
        row.LastRefreshedAt = collection.LastRefreshedAt;
    }

    private CollectionDetailRow Track(CollectionDetailRow row) {
        db.CollectionDetails.Add(row);
        return row;
    }
}
