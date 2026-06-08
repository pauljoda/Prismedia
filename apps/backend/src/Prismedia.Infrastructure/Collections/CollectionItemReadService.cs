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
                entity.KindCode == EntityKindRegistry.Collection.Code,
                cancellationToken);

        if (!collectionExists) {
            return new CollectionItemsResponse([]);
        }

        var rows = await (
            from item in db.CollectionItemDetails.AsNoTracking()
            join entity in db.Entities.AsNoTracking() on item.ItemEntityId equals entity.Id
            where item.CollectionEntityId == collectionId &&
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
                    row.KindCode.DecodeAs<EntityKind>(),
                    row.ItemEntityId,
                    row.Source,
                    row.SortOrder,
                    row.AddedAt,
                    thumbnail)
                : null)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();

        return new CollectionItemsResponse(items);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ResolveCoverPathsAsync(
        IReadOnlyList<Guid> collectionIds,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (collectionIds.Count == 0) {
            return new Dictionary<Guid, string>();
        }

        var ids = collectionIds.Distinct().ToArray();

        // Configured cover item wins; capture it per collection so it can override the
        // first-member fallback resolved below.
        var coverItemByCollection = await db.CollectionDetails.AsNoTracking()
            .Where(detail => ids.Contains(detail.EntityId) && detail.CoverItemEntityId != null)
            .ToDictionaryAsync(detail => detail.EntityId, detail => detail.CoverItemEntityId!.Value, cancellationToken);

        // First visible member per collection, in collection sort order, as the fallback cover.
        var memberRows = await (
            from item in db.CollectionItemDetails.AsNoTracking()
            join entity in db.Entities.AsNoTracking() on item.ItemEntityId equals entity.Id
            where ids.Contains(item.CollectionEntityId) &&
                  (!hideNsfw || !entity.IsNsfw)
            orderby item.SortOrder, entity.Title, item.Id
            select new { item.CollectionEntityId, item.ItemEntityId }).ToArrayAsync(cancellationToken);
        var firstMemberByCollection = memberRows
            .GroupBy(row => row.CollectionEntityId)
            .ToDictionary(group => group.Key, group => group.First().ItemEntityId);

        // Representative entity per collection: configured cover item, else first member.
        var representativeByCollection = new Dictionary<Guid, Guid>();
        foreach (var collectionId in ids) {
            if (coverItemByCollection.TryGetValue(collectionId, out var coverItemId)) {
                representativeByCollection[collectionId] = coverItemId;
            } else if (firstMemberByCollection.TryGetValue(collectionId, out var memberId)) {
                representativeByCollection[collectionId] = memberId;
            }
        }

        if (representativeByCollection.Count == 0) {
            return new Dictionary<Guid, string>();
        }

        // Reuse the batched thumbnail projection so the cover we report matches the one the
        // representative entity would render with everywhere else.
        var thumbnails = await entities.GetThumbnailsAsync(
            representativeByCollection.Values.Distinct().ToArray(),
            hideNsfw,
            cancellationToken);
        var coverByEntity = thumbnails.Items
            .Where(thumbnail => thumbnail.CoverUrl is not null)
            .ToDictionary(thumbnail => thumbnail.Id, thumbnail => thumbnail.CoverUrl!);

        var result = new Dictionary<Guid, string>();
        foreach (var pair in representativeByCollection) {
            if (coverByEntity.TryGetValue(pair.Value, out var coverUrl)) {
                result[pair.Key] = coverUrl;
            }
        }

        return result;
    }
}
