using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Collections;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Collections;

/// <summary>
/// EF-backed persistence adapter for collection command use cases. Application owns
/// orchestration and domain decisions; this adapter only reads and writes row state.
/// </summary>
public sealed class CollectionCommandPersistence(PrismediaDbContext db) : ICollectionCommandPersistence {
    /// <inheritdoc />
    public async Task<Guid> CreateAsync(
        Collection collection,
        string? description,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = collection.Id,
            KindCode = EntityKindRegistry.Collection.Code,
            Title = collection.Title,
            IsNsfw = collection.IsNsfw ?? false,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CollectionDetails.Add(new CollectionDetailRow {
            EntityId = collection.Id,
            Mode = collection.Mode,
            RuleTreeJson = collection.RuleTreeJson,
            CoverMode = collection.CoverMode,
            CoverItemEntityId = collection.CoverItemId,
            LastRefreshedAt = collection.LastRefreshedAt
        });
        await UpsertDescriptionAsync(collection.Id, description, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return collection.Id;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        Collection collection,
        string? description,
        CancellationToken cancellationToken) {
        var entity = await db.Entities
            .FirstOrDefaultAsync(row =>
                row.Id == collection.Id &&
                row.KindCode == EntityKindRegistry.Collection.Code,
                cancellationToken);
        if (entity is null) {
            return false;
        }

        var detail = await db.CollectionDetails.FindAsync([collection.Id], cancellationToken)
            ?? TrackCollectionDetail(collection.Id);
        var now = DateTimeOffset.UtcNow;
        entity.Title = collection.Title;
        entity.IsNsfw = collection.IsNsfw ?? false;
        entity.UpdatedAt = now;
        detail.Mode = collection.Mode;
        detail.RuleTreeJson = collection.RuleTreeJson;
        detail.CoverMode = collection.CoverMode;
        detail.CoverItemEntityId = collection.CoverItemId;
        detail.LastRefreshedAt = collection.LastRefreshedAt;
        await UpsertDescriptionAsync(collection.Id, description, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid collectionId, CancellationToken cancellationToken) {
        var entity = await db.Entities
            .FirstOrDefaultAsync(row =>
                row.Id == collectionId &&
                row.KindCode == EntityKindRegistry.Collection.Code,
                cancellationToken);
        if (entity is null) {
            return false;
        }

        // Hard-delete the collection row. Its detail and item-detail rows cascade with it; the
        // member media the collection pointed at is left untouched.
        db.Entities.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<CollectionMode?> GetModeAsync(
        Guid collectionId,
        CancellationToken cancellationToken) {
        var row = await db.Entities
            .Where(entity =>
                entity.Id == collectionId &&
                entity.KindCode == EntityKindRegistry.Collection.Code)
            .Join(
                db.CollectionDetails,
                entity => entity.Id,
                detail => detail.EntityId,
                (_, detail) => new { detail.Mode })
            .FirstOrDefaultAsync(cancellationToken);
        return row?.Mode;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, CollectionItemCandidate>> GetActiveItemsAsync(
        IReadOnlyList<Guid> entityIds,
        CancellationToken cancellationToken) {
        var allEntities = db.Entities.AsNoTracking();
        var rows = await allEntities
            .ExcludeBookOwnedAudioTracks(allEntities)
            .Where(row => entityIds.Contains(row.Id))
            .Select(row => new { row.Id, row.KindCode })
            .ToArrayAsync(cancellationToken);

        return rows.ToDictionary(
            row => row.Id,
            row => new CollectionItemCandidate(row.Id, EntityKindRegistry.Require(row.KindCode)));
    }

    /// <inheritdoc />
    public async Task<int> AddManualItemsAsync(
        Guid collectionId,
        IReadOnlyList<Guid> entityIds,
        CancellationToken cancellationToken) {
        var existingIds = await db.CollectionItemDetails
            .Where(row => row.CollectionEntityId == collectionId)
            .Select(row => row.ItemEntityId)
            .ToHashSetAsync(cancellationToken);
        var sortOrder = await NextSortOrderAsync(collectionId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var added = 0;

        foreach (var entityId in entityIds) {
            if (existingIds.Contains(entityId)) {
                continue;
            }

            db.CollectionItemDetails.Add(new CollectionItemDetailRow {
                Id = Guid.NewGuid(),
                CollectionEntityId = collectionId,
                ItemEntityId = entityId,
                Source = CollectionItemSource.Manual,
                SortOrder = sortOrder++,
                AddedAt = now,
            });
            existingIds.Add(entityId);
            added++;
        }

        await TouchCollectionAsync(collectionId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    /// <inheritdoc />
    public async Task<int> RemoveItemsAsync(
        Guid collectionId,
        IReadOnlyList<Guid> itemIds,
        CancellationToken cancellationToken) {
        var rows = await db.CollectionItemDetails
            .Where(row => row.CollectionEntityId == collectionId && itemIds.Contains(row.Id))
            .ToArrayAsync(cancellationToken);
        db.CollectionItemDetails.RemoveRange(rows);
        await TouchCollectionAsync(collectionId, DateTimeOffset.UtcNow, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return rows.Length;
    }

    /// <inheritdoc />
    public async Task<int> ReorderItemsAsync(
        Guid collectionId,
        IReadOnlyList<Guid> itemIds,
        CancellationToken cancellationToken) {
        var rows = await db.CollectionItemDetails
            .Where(row => row.CollectionEntityId == collectionId)
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        if (rows.Length == 0) {
            return 0;
        }

        var requested = itemIds.Distinct().ToArray();
        var byId = rows.ToDictionary(row => row.Id);
        var ordered = requested
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .Concat(rows.Where(row => !requested.Contains(row.Id)))
            .ToArray();

        for (var i = 0; i < ordered.Length; i++) {
            ordered[i].SortOrder = i;
        }

        await TouchCollectionAsync(collectionId, DateTimeOffset.UtcNow, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ordered.Length;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(Guid collectionId, CancellationToken cancellationToken) =>
        await db.Entities.AnyAsync(row =>
            row.Id == collectionId &&
            row.KindCode == EntityKindRegistry.Collection.Code,
            cancellationToken);

    /// <inheritdoc />
    public async Task<int> CountItemsAsync(Guid collectionId, CancellationToken cancellationToken) {
        var allEntities = db.Entities.AsNoTracking();
        var catalogEntities = allEntities.ExcludeBookOwnedAudioTracks(allEntities);
        return await (
            from item in db.CollectionItemDetails.AsNoTracking()
            join entity in catalogEntities on item.ItemEntityId equals entity.Id
            where item.CollectionEntityId == collectionId
            select item.Id)
            .CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectionVisibleRuleMatch>> FilterVisibleRuleMatchesAsync(
        IReadOnlyList<CollectionRuleMatch> matches,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var ids = matches.Select(match => match.EntityId).Distinct().ToArray();
        if (ids.Length == 0) {
            return [];
        }

        var allEntities = db.Entities.AsNoTracking();
        var query = allEntities
            .ExcludeBookOwnedAudioTracks(allEntities)
            .Where(row => ids.Contains(row.Id));
        if (hideNsfw) {
            query = query.Where(row => !row.IsNsfw);
        }

        var rows = await query.ToDictionaryAsync(row => row.Id, cancellationToken);
        var seen = new HashSet<Guid>();
        return matches
            .Where(match => seen.Add(match.EntityId))
            .Select(match => rows.TryGetValue(match.EntityId, out var row)
                ? new CollectionVisibleRuleMatch(row.KindCode, row.Id)
                : null)
            .Where(match => match is not null)
            .Select(match => match!)
            .ToArray();
    }

    private async Task UpsertDescriptionAsync(
        Guid collectionId,
        string? description,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var row = await db.EntityDescriptions.FindAsync([collectionId], cancellationToken);
        if (string.IsNullOrWhiteSpace(description)) {
            if (row is not null) {
                db.EntityDescriptions.Remove(row);
            }
            return;
        }

        if (row is null) {
            db.EntityDescriptions.Add(new EntityDescriptionRow {
                EntityId = collectionId,
                Value = description.Trim(),
                UpdatedAt = now,
            });
            return;
        }

        row.Value = description.Trim();
        row.UpdatedAt = now;
    }

    private async Task TouchCollectionAsync(
        Guid collectionId,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var entity = await db.Entities.FindAsync([collectionId], cancellationToken);
        if (entity is not null) {
            entity.UpdatedAt = now;
        }
    }

    private async Task<int> NextSortOrderAsync(Guid collectionId, CancellationToken cancellationToken) =>
        (await db.CollectionItemDetails
            .Where(row => row.CollectionEntityId == collectionId)
            .Select(row => (int?)row.SortOrder)
            .MaxAsync(cancellationToken) ?? -1) + 1;

    private CollectionDetailRow TrackCollectionDetail(Guid collectionId) {
        var detail = new CollectionDetailRow { EntityId = collectionId };
        db.CollectionDetails.Add(detail);
        return detail;
    }
}
