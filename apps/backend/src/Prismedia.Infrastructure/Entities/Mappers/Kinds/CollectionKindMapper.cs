using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Collections;
using Prismedia.Contracts.Entities;
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
            ? new Collection(row.Id, row.Title)
            : new Collection(
                row.Id,
                row.Title,
                detail.Mode,
                detail.RuleTreeJson,
                detail.CoverMode,
                detail.CoverItemEntityId,
                detail.LastRefreshedAt);
    }

    public async Task PersistDetailAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity is not Collection collection) {
            return;
        }

        var row = await db.CollectionDetails.FindAsync([entity.Id], cancellationToken)
            ?? Track(new CollectionDetailRow { EntityId = entity.Id });
        row.Mode = collection.Mode;
        row.RuleTreeJson = collection.RuleTreeJson;
        row.CoverMode = collection.CoverMode;
        row.CoverItemEntityId = collection.CoverItemId;
        row.LastRefreshedAt = collection.LastRefreshedAt;
    }

    public IEntityCard ProjectDetail(
        Entity entity,
        EntityCard card,
        IReadOnlyList<EntityCreditMetadata> creditMetadata) =>
        entity is Collection collection
            ? new CollectionDetail {
                Id = card.Id,
                Kind = card.Kind,
                Title = card.Title,
                ParentEntityId = card.ParentEntityId,
                SortOrder = card.SortOrder,
                HasSourceMedia = card.HasSourceMedia,
                Capabilities = card.Capabilities,
                ChildrenByKind = card.ChildrenByKind,
                Relationships = card.Relationships,
                Mode = collection.Mode,
                RuleTreeJson = collection.RuleTreeJson,
                CoverMode = collection.CoverMode,
                CoverItemId = collection.CoverItemId,
                LastRefreshedAt = collection.LastRefreshedAt,
            }
            : card;

    private CollectionDetailRow Track(CollectionDetailRow row) {
        db.CollectionDetails.Add(row);
        return row;
    }
}
