using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Thumbnails;

/// <summary>
/// Counts caller-visible collection item rows for collection thumbnails. Dynamic and hybrid
/// collections use their already-materialized rows; this projection never evaluates rule trees.
/// </summary>
internal sealed class CollectionMembershipCountContributor(PrismediaDbContext db) : IThumbnailContributor {
    /// <inheritdoc />
    public int MetaPriority => -100;

    /// <inheritdoc />
    public async Task ContributeAsync(
        ThumbnailContributions contributions,
        CancellationToken cancellationToken) {
        var collectionIds = contributions.Rows
            .Where(row => row.KindCode == EntityKind.Collection.ToCode())
            .Select(row => row.Id)
            .ToArray();
        if (collectionIds.Length == 0) {
            return;
        }

        var counts = contributions.ReadsPersistedRollups
            // Root-keyed rows from the trigger-maintained membership projection, summed over the
            // viewer's allowed roots with NSFW sub-counts subtracted for NSFW-hiding viewers.
            ? (await db.EntityCollectionMemberCounts.AsNoTracking()
                .Where(count => collectionIds.Contains(count.EntityId) &&
                    !contributions.HiddenLibraryRootIds.Contains(count.LibraryRootId))
                .GroupBy(count => count.EntityId)
                .Select(group => new {
                    EntityId = group.Key,
                    Total = group.Sum(count => count.CountTotal),
                    Nsfw = group.Sum(count => count.CountNsfw)
                })
                .ToArrayAsync(cancellationToken))
                .Select(row => new CollectionMembershipCount(
                    row.EntityId,
                    contributions.HideNsfw ? row.Total - row.Nsfw : row.Total))
                .Where(count => count.Count > 0)
                .ToArray()
            : await BuildQuery(db, contributions.VisibleEntities, collectionIds)
                .ToArrayAsync(cancellationToken);
        foreach (var count in counts) {
            contributions.AddMeta(
                count.CollectionEntityId,
                EntityThumbnailMetaIcons.Collection,
                count.Count.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Builds the contributor's single indexed, grouped membership query.</summary>
    internal static IQueryable<CollectionMembershipCount> BuildQuery(
        PrismediaDbContext db,
        IQueryable<EntityRow> visibleEntities,
        Guid[] collectionIds) =>
        from item in db.CollectionItemDetails.AsNoTracking()
        where collectionIds.Contains(item.CollectionEntityId)
        join visibleItem in visibleEntities on item.ItemEntityId equals visibleItem.Id
        group item by item.CollectionEntityId into items
        select new CollectionMembershipCount(items.Key, items.Count());
}

/// <summary>One materialized collection membership count.</summary>
internal sealed record CollectionMembershipCount(Guid CollectionEntityId, int Count);
