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
        EntityKind.VideoSeries.ToCode(),
        EntityKind.VideoSeason.ToCode(),
        EntityKind.Book.ToCode(),
        EntityKind.BookVolume.ToCode(),
        EntityKind.BookChapter.ToCode(),
        EntityKind.BookAuthor.ToCode(),
        EntityKind.MusicArtist.ToCode(),
        EntityKind.AudioLibrary.ToCode(),
        EntityKind.Gallery.ToCode()
    ];

    private static readonly string[] CountedKindCodes = [
        EntityKind.VideoSeason.ToCode(),
        EntityKind.Video.ToCode(),
        EntityKind.Book.ToCode(),
        EntityKind.BookVolume.ToCode(),
        EntityKind.BookChapter.ToCode(),
        EntityKind.BookPage.ToCode(),
        EntityKind.AudioLibrary.ToCode(),
        EntityKind.AudioTrack.ToCode(),
        EntityKind.Image.ToCode()
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

        var maxDepth = roots.Any(root => root.KindCode == EntityKind.Book.ToCode())
            ? 3
            : roots.Any(root =>
                root.KindCode == EntityKind.VideoSeries.ToCode() ||
                root.KindCode == EntityKind.BookVolume.ToCode() ||
                root.KindCode == EntityKind.MusicArtist.ToCode())
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

            if (root.KindCode == EntityKind.VideoSeries.ToCode()) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Season, AtDepth(EntityKind.VideoSeason.ToCode(), 1));
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Episode, ThroughDepth(EntityKind.Video.ToCode(), 2));
            } else if (root.KindCode == EntityKind.VideoSeason.ToCode()) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Episode, AtDepth(EntityKind.Video.ToCode(), 1));
            } else if (root.KindCode == EntityKind.Book.ToCode()) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Volume, AtDepth(EntityKind.BookVolume.ToCode(), 1));
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Chapter, ThroughDepth(EntityKind.BookChapter.ToCode(), 2));
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Page, ThroughDepth(EntityKind.BookPage.ToCode(), 3));
            } else if (root.KindCode == EntityKind.BookVolume.ToCode()) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Chapter, AtDepth(EntityKind.BookChapter.ToCode(), 1));
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Page, ThroughDepth(EntityKind.BookPage.ToCode(), 2));
            } else if (root.KindCode == EntityKind.BookChapter.ToCode()) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Page, AtDepth(EntityKind.BookPage.ToCode(), 1));
            } else if (root.KindCode == EntityKind.BookAuthor.ToCode()) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Book, AtDepth(EntityKind.Book.ToCode(), 1));
            } else if (root.KindCode == EntityKind.MusicArtist.ToCode()) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Album, AtDepth(EntityKind.AudioLibrary.ToCode(), 1));
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Track, ThroughDepth(EntityKind.AudioTrack.ToCode(), 2));
            } else if (root.KindCode == EntityKind.AudioLibrary.ToCode()) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Track, AtDepth(EntityKind.AudioTrack.ToCode(), 1));
            } else if (root.KindCode == EntityKind.Gallery.ToCode()) {
                AddCount(contributions, rootId, EntityThumbnailMetaIcons.Image, AtDepth(EntityKind.Image.ToCode(), 1));
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
