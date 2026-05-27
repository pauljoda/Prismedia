using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Collections;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Collections;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Collections;

/// <summary>
/// EF-backed read model for ordered collection membership.
/// </summary>
public sealed class CollectionItemReadService(
    PrismediaDbContext db,
    IEntityReadService entities) : ICollectionItemReadService {
    public async Task<CollectionItemsResponse> ListItemsAsync(
        Guid collectionId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var collectionExists = await db.Entities.AsNoTracking()
            .AnyAsync(entity =>
                entity.Id == collectionId &&
                entity.KindCode == EntityKindRegistry.Collection.Code &&
                entity.DeletedAt == null,
                cancellationToken);

        if (!collectionExists) {
            return new CollectionItemsResponse([]);
        }

        var rows = await (
            from item in db.CollectionItemDetails.AsNoTracking()
            join entity in db.Entities.AsNoTracking() on item.ItemEntityId equals entity.Id
            where item.CollectionEntityId == collectionId &&
                  entity.DeletedAt == null &&
                  (!hideNsfw || !entity.IsNsfw)
            orderby item.SortOrder, entity.Title, item.Id
            select new {
                item.Id,
                item.CollectionEntityId,
                item.ItemEntityId,
                item.Source,
                item.SortOrder,
                item.AddedAt,
                entity.KindCode
            }).ToArrayAsync(cancellationToken);

        if (rows.Length == 0) {
            return new CollectionItemsResponse([]);
        }

        var thumbnails = await entities.GetThumbnailsAsync(
            rows.Select(row => row.ItemEntityId).ToArray(),
            hideNsfw,
            cancellationToken);
        var thumbnailsById = thumbnails.Items.ToDictionary(thumbnail => thumbnail.Id);

        var items = rows
            .Select(row => thumbnailsById.TryGetValue(row.ItemEntityId, out var thumbnail)
                ? new CollectionItemDetail(
                    row.Id,
                    row.CollectionEntityId,
                    row.KindCode,
                    row.ItemEntityId,
                    row.Source.ToCode(),
                    row.SortOrder,
                    row.AddedAt,
                    thumbnail)
                : null)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();

        return new CollectionItemsResponse(items);
    }
}
