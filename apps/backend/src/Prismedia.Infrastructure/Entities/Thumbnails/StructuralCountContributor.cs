using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Thumbnails;

/// <summary>
/// Contributes compact membership counts for structural media containers. One grouped aggregate
/// covers the page's visible descendants at depths one through three, which is sufficient for the
/// deepest supported shape (book → volume → chapter → page) without hydrating any descendant row.
/// </summary>
internal sealed class StructuralCountContributor : IThumbnailContributor {
    private static readonly string[] RootKindCodes = [
        EntityKindRegistry.VideoSeries.Code,
        EntityKindRegistry.VideoSeason.Code,
        EntityKindRegistry.Book.Code,
        EntityKindRegistry.BookVolume.Code,
        EntityKindRegistry.BookChapter.Code,
        EntityKindRegistry.BookAuthor.Code,
        EntityKindRegistry.MusicArtist.Code,
        EntityKindRegistry.AudioLibrary.Code,
        EntityKindRegistry.Gallery.Code
    ];

    private static readonly string[] CountedKindCodes = [
        EntityKindRegistry.VideoSeason.Code,
        EntityKindRegistry.Video.Code,
        EntityKindRegistry.Book.Code,
        EntityKindRegistry.BookVolume.Code,
        EntityKindRegistry.BookChapter.Code,
        EntityKindRegistry.BookPage.Code,
        EntityKindRegistry.AudioLibrary.Code,
        EntityKindRegistry.AudioTrack.Code,
        EntityKindRegistry.Image.Code
    ];

    /// <summary>Creates the contributor for the scoped persistence context.</summary>
    public StructuralCountContributor(PrismediaDbContext db) => ArgumentNullException.ThrowIfNull(db);

    /// <inheritdoc />
    public async Task ContributeAsync(
        ThumbnailContributions contributions,
        CancellationToken cancellationToken) {
        var roots = contributions.Rows
            .Where(row => RootKindCodes.Contains(row.KindCode, StringComparer.Ordinal))
            .ToArray();
        if (roots.Length == 0) {
            return;
        }

        var maxDepth = roots.Any(root => root.KindCode == EntityKindRegistry.Book.Code)
            ? 3
            : roots.Any(root =>
                root.KindCode == EntityKindRegistry.VideoSeries.Code ||
                root.KindCode == EntityKindRegistry.BookVolume.Code ||
                root.KindCode == EntityKindRegistry.MusicArtist.Code)
                ? 2
                : 1;
        var counts = await BuildQuery(
                contributions.VisibleEntities,
                roots.Select(row => row.Id).ToArray(),
                maxDepth)
            .ToArrayAsync(cancellationToken);
        var countByRootKindAndDepth = counts.ToDictionary(
            count => (count.RootEntityId, count.KindCode, count.Depth),
            count => count.Count);

        foreach (var root in roots) {
            var rootId = root.Id;
            int AtDepth(string kindCode, int depth) =>
                countByRootKindAndDepth.GetValueOrDefault((rootId, kindCode, depth));
            int ThroughDepth(string kindCode, int maxDepth) =>
                Enumerable.Range(1, maxDepth).Sum(depth => AtDepth(kindCode, depth));

            if (root.KindCode == EntityKindRegistry.VideoSeries.Code) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Season, AtDepth(EntityKindRegistry.VideoSeason.Code, 1));
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Episode, ThroughDepth(EntityKindRegistry.Video.Code, 2));
            } else if (root.KindCode == EntityKindRegistry.VideoSeason.Code) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Episode, AtDepth(EntityKindRegistry.Video.Code, 1));
            } else if (root.KindCode == EntityKindRegistry.Book.Code) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Volume, AtDepth(EntityKindRegistry.BookVolume.Code, 1));
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Chapter, ThroughDepth(EntityKindRegistry.BookChapter.Code, 2));
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Page, ThroughDepth(EntityKindRegistry.BookPage.Code, 3));
            } else if (root.KindCode == EntityKindRegistry.BookVolume.Code) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Chapter, AtDepth(EntityKindRegistry.BookChapter.Code, 1));
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Page, ThroughDepth(EntityKindRegistry.BookPage.Code, 2));
            } else if (root.KindCode == EntityKindRegistry.BookChapter.Code) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Page, AtDepth(EntityKindRegistry.BookPage.Code, 1));
            } else if (root.KindCode == EntityKindRegistry.BookAuthor.Code) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Book, AtDepth(EntityKindRegistry.Book.Code, 1));
            } else if (root.KindCode == EntityKindRegistry.MusicArtist.Code) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Album, AtDepth(EntityKindRegistry.AudioLibrary.Code, 1));
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Track, ThroughDepth(EntityKindRegistry.AudioTrack.Code, 2));
            } else if (root.KindCode == EntityKindRegistry.AudioLibrary.Code) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Track, AtDepth(EntityKindRegistry.AudioTrack.Code, 1));
            } else if (root.KindCode == EntityKindRegistry.Gallery.Code) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Image, AtDepth(EntityKindRegistry.Image.Code, 1));
            }
        }
    }

    /// <summary>
    /// Builds the single server-side aggregate used by this contributor. Each union branch follows
    /// indexed <c>parent_entity_id</c> links for one fixed depth; grouping returns only a handful of
    /// count rows per page root instead of transferring descendants.
    /// </summary>
    internal static IQueryable<StructuralDescendantCount> BuildQuery(
        IQueryable<EntityRow> visibleEntities,
        Guid[] rootIds,
        int maxDepth) {
        var direct = visibleEntities
            .Where(child => child.ParentEntityId != null && rootIds.Contains(child.ParentEntityId.Value))
            .Select(child => new {
                RootEntityId = child.ParentEntityId!.Value,
                child.KindCode,
                Depth = 1
            });
        var grandchildren =
            from parent in visibleEntities
            where parent.ParentEntityId != null && rootIds.Contains(parent.ParentEntityId.Value)
            join child in visibleEntities.Where(child => child.ParentEntityId != null)
                on parent.Id equals child.ParentEntityId!.Value
            select new {
                RootEntityId = parent.ParentEntityId!.Value,
                child.KindCode,
                Depth = 2
            };
        var greatGrandchildren =
            from parent in visibleEntities
            where parent.ParentEntityId != null && rootIds.Contains(parent.ParentEntityId.Value)
            join child in visibleEntities.Where(child => child.ParentEntityId != null)
                on parent.Id equals child.ParentEntityId!.Value
            join grandchild in visibleEntities.Where(grandchild => grandchild.ParentEntityId != null)
                on child.Id equals grandchild.ParentEntityId!.Value
            select new {
                RootEntityId = parent.ParentEntityId!.Value,
                grandchild.KindCode,
                Depth = 3
            };

        var descendants = direct;
        if (maxDepth >= 2) {
            descendants = descendants.Concat(grandchildren);
        }
        if (maxDepth >= 3) {
            descendants = descendants.Concat(greatGrandchildren);
        }

        return descendants
            .Where(descendant => CountedKindCodes.Contains(descendant.KindCode))
            .GroupBy(descendant => new {
                descendant.RootEntityId,
                descendant.KindCode,
                descendant.Depth
            })
            .Select(group => new StructuralDescendantCount(
                group.Key.RootEntityId,
                group.Key.KindCode,
                group.Key.Depth,
                group.Count()));
    }

    private static void AddCount(
        ThumbnailContributions contributions,
        Guid entityId,
        string icon,
        int count) {
        if (count > 0) {
            contributions.AddMeta(entityId, icon, count.ToString(CultureInfo.InvariantCulture));
        }
    }
}

/// <summary>One grouped descendant count returned by the structural aggregate.</summary>
internal sealed record StructuralDescendantCount(Guid RootEntityId, string KindCode, int Depth, int Count);
