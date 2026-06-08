using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Entities.Thumbnails;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Thumbnail, cover, and hover-image projection helpers for <see cref="EfEntityReadService"/>.
/// </summary>
public sealed partial class EfEntityReadService {
    private async Task<IReadOnlyList<EntityThumbnail>> ProjectThumbnailsAsync(
        IReadOnlyList<EntityRow> rows,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (rows.Count == 0) {
            return [];
        }

        var ids = rows.Select(entity => entity.Id).ToArray();
        var parentIds = rows
            .Select(entity => entity.ParentEntityId)
            .Where(parentId => parentId is not null)
            .Select(parentId => parentId!.Value)
            .Distinct()
            .ToArray();
        var parentRowsByEntity = parentIds.Length == 0
            ? new Dictionary<Guid, EntityRow>()
            : await _db.Entities.AsNoTracking()
                .Where(parent => parentIds.Contains(parent.Id))
                .ToDictionaryAsync(parent => parent.Id, cancellationToken);
        var parentKindByEntity = parentRowsByEntity.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.KindCode);
        var albumParentIds = rows
            .Where(row =>
                row.KindCode == EntityKindRegistry.AudioTrack.Code &&
                row.ParentEntityId is { } parentId &&
                parentRowsByEntity.TryGetValue(parentId, out var parent) &&
                parent.KindCode == EntityKindRegistry.AudioLibrary.Code &&
                (!hideNsfw || !parent.IsNsfw))
            .Select(row => row.ParentEntityId!.Value)
            .Distinct()
            .ToArray();
        var coverByEntity = await LoadCoverPathsAsync(ids, cancellationToken);
        var coverByAlbumParent = albumParentIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await LoadCoverPathsAsync(albumParentIds, cancellationToken);
        var hoverFiles = await _db.EntityFiles.AsNoTracking()
            .Where(file => ids.Contains(file.EntityId) && file.Role == EntityFileRole.Trickplay)
            .Where(file => file.Path.EndsWith(".m3u8") || file.Path.EndsWith(".vtt"))
            .OrderByDescending(file => file.Path.EndsWith(".m3u8"))
            .ThenBy(file => file.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var hoverByEntity = hoverFiles
            .GroupBy(file => file.EntityId)
            .ToDictionary(group => group.Key, group => group.First().Path);
        var hoverImagesByEntity = await ProjectHoverImagesAsync(rows, hideNsfw, cancellationToken);
        var technicalByEntity = await _db.EntityTechnical.AsNoTracking()
            .Where(technical => ids.Contains(technical.EntityId))
            .ToDictionaryAsync(technical => technical.EntityId, cancellationToken);
        // Section (disc) labels for audio tracks, surfaced as a thumbnail chip so album track
        // lists can group multi-disc albums and restart numbering per section.
        var sectionByEntity = rows.Any(row => row.KindCode == EntityKindRegistry.AudioTrack.Code)
            ? await _db.AudioTrackDetails.AsNoTracking()
                .Where(detail => ids.Contains(detail.EntityId) && detail.SectionLabel != null)
                .ToDictionaryAsync(detail => detail.EntityId, detail => detail.SectionLabel!, cancellationToken)
            : new Dictionary<Guid, string>();
        var bookTypeByEntity = rows.Any(row => row.KindCode == EntityKindRegistry.Book.Code)
            ? await _db.BookDetails.AsNoTracking()
                .Where(detail => ids.Contains(detail.EntityId))
                .ToDictionaryAsync(detail => detail.EntityId, detail => detail.BookType, cancellationToken)
            : new Dictionary<Guid, BookType>();
        var gridThumbByEntity = await _db.EntityFiles.AsNoTracking()
            .Where(file => ids.Contains(file.EntityId) && file.Role == EntityFileRole.GridThumbnail)
            .ToDictionaryAsync(file => file.EntityId, file => file.Path, cancellationToken);
        var playbackByEntity = await _db.Set<EntityPlaybackRow>().AsNoTracking()
            .Where(row => ids.Contains(row.EntityId))
            .ToDictionaryAsync(row => row.EntityId, cancellationToken);
        var movieIds = rows
            .Where(row => row.KindCode == EntityKindRegistry.Movie.Code)
            .Select(row => row.Id)
            .ToArray();
        var childPlaybackByMovie = movieIds.Length == 0
            ? new Dictionary<Guid, EntityPlaybackRow>()
            : await LoadMovieChildPlaybackAsync(movieIds, cancellationToken);
        var progressByEntity = await _db.Set<EntityProgressRow>().AsNoTracking()
            .Where(row => ids.Contains(row.EntityId))
            .ToDictionaryAsync(row => row.EntityId, cancellationToken);

        // Tag names per entity, resolved through the tag relationship links and their target titles,
        // so list rows can surface tags (used as genres on the Jellyfin surface) without a detail load.
        var tagLinks = await _db.EntityRelationshipLinks.AsNoTracking()
            .Where(link => ids.Contains(link.EntityId) && link.RelationshipCode == "tags")
            .OrderBy(link => link.SortOrder)
            .Select(link => new { link.EntityId, link.TargetEntityId })
            .ToArrayAsync(cancellationToken);
        var tagTitleById = tagLinks.Length == 0
            ? new Dictionary<Guid, string>()
            : await _db.Entities.AsNoTracking()
                .Where(entity => tagLinks.Select(link => link.TargetEntityId).Contains(entity.Id))
                .ToDictionaryAsync(entity => entity.Id, entity => entity.Title, cancellationToken);
        var tagsByEntity = tagLinks
            .GroupBy(link => link.EntityId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(link => tagTitleById.GetValueOrDefault(link.TargetEntityId))
                    .Where(title => !string.IsNullOrWhiteSpace(title))
                    .Select(title => title!)
                    .ToArray());

        var baseThumbnails = rows.Select(row => {
            playbackByEntity.TryGetValue(row.Id, out var ownPlayback);
            childPlaybackByMovie.TryGetValue(row.Id, out var childPlayback);
            var playback = ownPlayback ?? childPlayback;
            var hoverUrl = hoverByEntity.GetValueOrDefault(row.Id);
            var hoverImages = hoverImagesByEntity.GetValueOrDefault(row.Id) ?? [];
            var coverUrl = coverByEntity.GetValueOrDefault(row.Id);
            var bookType = bookTypeByEntity.TryGetValue(row.Id, out var value) ? value : (BookType?)null;
            if (coverUrl is null &&
                row.KindCode == EntityKindRegistry.AudioTrack.Code &&
                row.ParentEntityId is { } albumId) {
                coverUrl = coverByAlbumParent.GetValueOrDefault(albumId);
            }

            if (coverUrl is null && UsesRepresentativeCover(row.KindCode) && hoverImages.Count > 0) {
                coverUrl = hoverImages[0].Path;
            }

            return new EntityThumbnail(
                row.Id,
                row.KindCode,
                row.Title,
                row.ParentEntityId,
                row.SortOrder,
                coverUrl,
                gridThumbByEntity.GetValueOrDefault(row.Id),
                hoverUrl is null ? "none" : "sprite",
                hoverUrl,
                hoverImages,
                ProjectThumbnailMeta(
                    row,
                    technicalByEntity.GetValueOrDefault(row.Id),
                    sectionByEntity.GetValueOrDefault(row.Id),
                    bookType),
                row.RatingValue,
                row.IsFavorite,
                row.IsNsfw,
                row.IsOrganized) {
                ParentKind = row.ParentEntityId is { } parentId
                    ? parentKindByEntity.GetValueOrDefault(parentId)
                    : null,
                CreatedAt = row.CreatedAt,
                PlayCount = playback?.PlayCount,
                Genres = tagsByEntity.GetValueOrDefault(row.Id),
                Progress = ResolveThumbnailProgress(
                    playback,
                    progressByEntity.GetValueOrDefault(row.Id),
                    technicalByEntity.GetValueOrDefault(row.Id)?.DurationSeconds)
            };
        }).ToArray();

        // Let registered contributors fold in extra, kind-scoped data (e.g. taxonomy reference
        // counts) over the whole page. Each self-filters and runs at most one batched query, and
        // they share the scoped DbContext so they run sequentially. Their extra chips append after
        // the base technical chips and the combined list is capped at MaxThumbnailMeta.
        var contributions = new ThumbnailContributions(rows);
        foreach (var contributor in _thumbnailContributors) {
            await contributor.ContributeAsync(contributions, cancellationToken);
        }

        return baseThumbnails.Select(thumbnail => {
            var extraMeta = contributions.ExtraMetaFor(thumbnail.Id);
            var referenceCounts = contributions.ReferenceCountsFor(thumbnail.Id);
            if (extraMeta.Count == 0 && referenceCounts is null) {
                return thumbnail;
            }

            var meta = extraMeta.Count == 0
                ? thumbnail.Meta
                : thumbnail.Meta.Concat(extraMeta).Take(MaxThumbnailMeta).ToArray();
            return thumbnail with { Meta = meta, ReferenceCounts = referenceCounts };
        }).ToArray();
    }

    private async Task<Dictionary<Guid, EntityPlaybackRow>> LoadMovieChildPlaybackAsync(
        IReadOnlyCollection<Guid> movieIds,
        CancellationToken cancellationToken) {
        var childRows = await _db.Entities.AsNoTracking()
            .Where(child => child.ParentEntityId != null && movieIds.Contains(child.ParentEntityId.Value))
            .Select(child => new { child.Id, ParentId = child.ParentEntityId!.Value })
            .ToArrayAsync(cancellationToken);
        if (childRows.Length == 0) {
            return new Dictionary<Guid, EntityPlaybackRow>();
        }

        var parentByChild = childRows.ToDictionary(child => child.Id, child => child.ParentId);
        var childIds = parentByChild.Keys.ToArray();
        var playbackRows = await _db.Set<EntityPlaybackRow>().AsNoTracking()
            .Where(row => childIds.Contains(row.EntityId))
            .ToArrayAsync(cancellationToken);

        return playbackRows
            .GroupBy(row => parentByChild[row.EntityId])
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(row => row.CompletedAt is not null)
                    .ThenByDescending(row => row.PlayCount)
                    .ThenByDescending(row => row.ResumeSeconds)
                    .First());
    }

    /// <summary>
    /// Computes the 0..1 progress meter fraction for a thumbnail from its playback and reading
    /// progress rows. Playback (video/audio) takes precedence: a completed item reads 1.0, an
    /// item with a stored resume position reads its fraction of the known runtime, and anything
    /// else reads <c>null</c>. Reading progress (books) falls back to completed → 1.0 or the
    /// current index over the total. Returns <c>null</c> when there is nothing meaningful to show.
    /// </summary>
    private static double? ResolveThumbnailProgress(
        EntityPlaybackRow? playback,
        EntityProgressRow? progress,
        double? durationSeconds) {
        if (playback is not null) {
            if (playback.CompletedAt is not null) {
                return 1.0;
            }

            if (playback.ResumeSeconds > 0 && durationSeconds is > 0) {
                return Math.Clamp(playback.ResumeSeconds / durationSeconds.Value, 0, 1);
            }

            return null;
        }

        if (progress is not null) {
            if (progress.CompletedAt is not null) {
                return 1.0;
            }

            if (progress.Total > 0 && progress.Index > 0) {
                return Math.Clamp((double)progress.Index / progress.Total, 0, 1);
            }
        }

        return null;
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadCoverPathsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) {
        if (ids.Count == 0) {
            return new Dictionary<Guid, string>();
        }

        var covers = await _db.EntityFiles.AsNoTracking()
            .Where(file => ids.Contains(file.EntityId))
            .Where(file => file.Role == EntityFileRole.Thumbnail ||
                file.Role == EntityFileRole.Poster ||
                file.Role == EntityFileRole.Cover ||
                file.Role == EntityFileRole.Logo ||
                file.Role == EntityFileRole.Backdrop)
            .ToArrayAsync(cancellationToken);

        return covers
            .GroupBy(file => file.EntityId)
            .ToDictionary(
                group => group.Key,
                group => EntityCoverSelection.Select(group)!.Path);
    }

    private static IReadOnlyList<EntityThumbnailMeta> ProjectThumbnailMeta(
        EntityRow row,
        EntityTechnicalRow? technical,
        string? sectionLabel = null,
        BookType? bookType = null) {
        var meta = new List<EntityThumbnailMeta>(MaxThumbnailMeta);
        // Lead with the disc/section chip so it survives the meta cap and clients can group tracks.
        Add(meta, "disc", sectionLabel);
        Add(meta, "book", FormatBookType(bookType));

        if (technical is null) {
            return meta;
        }

        Add(meta, "duration", FormatDuration(technical.DurationSeconds));
        if (technical.Width is { } width && technical.Height is { } height) {
            Add(meta, row.KindCode == EntityKindRegistry.Video.Code ? "video" : "image", FormatResolution(width, height));
        }

        if (row.KindCode == EntityKindRegistry.Video.Code) {
            Add(meta, "video", technical.Codec?.ToUpperInvariant());
            Add(meta, "video", technical.Container?.ToUpperInvariant());
        } else if (row.KindCode == EntityKindRegistry.AudioTrack.Code) {
            Add(meta, "audio", technical.Codec?.ToUpperInvariant());
        }

        return meta.Take(MaxThumbnailMeta).ToArray();
    }

    private static string? FormatBookType(BookType? bookType) =>
        bookType switch {
            BookType.Book => "Book",
            BookType.Comic => "Comic",
            BookType.Manga => "Manga",
            BookType.Novel => "Novel",
            _ => null
        };

    private static void Add(List<EntityThumbnailMeta> meta, string icon, string? label) {
        if (!string.IsNullOrWhiteSpace(label)) {
            meta.Add(new EntityThumbnailMeta(icon, label));
        }
    }

    private static string? FormatDuration(double? seconds) {
        if (seconds is not { } value || !double.IsFinite(value) || value <= 0) {
            return null;
        }

        var duration = TimeSpan.FromSeconds(Math.Round(value));
        if (duration.TotalHours >= 1) {
            return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}";
        }

        return $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private static string FormatResolution(int width, int height) {
        if (height >= 2160) return "4K";
        if (height >= 1440) return "1440p";
        if (height >= 1080) return "1080p";
        if (height >= 720) return "720p";
        if (height >= 480) return "480p";
        return $"{width}x{height}";
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<EntityThumbnailHoverImage>>> ProjectHoverImagesAsync(
        IReadOnlyList<EntityRow> rows,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (rows.Count == 0) {
            return new Dictionary<Guid, IReadOnlyList<EntityThumbnailHoverImage>>();
        }

        var rootIds = rows.Select(row => row.Id).ToArray();
        var directChildQuery = _db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId != null && rootIds.Contains(row.ParentEntityId.Value));
        directChildQuery = ApplyNsfwVisibility(directChildQuery, hideNsfw);
        var directChildren = await directChildQuery
            .OrderBy(row => row.ParentEntityId)
            .ThenBy(row => row.SortOrder)
            .ThenBy(row => row.Title)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        if (directChildren.Length == 0) {
            return new Dictionary<Guid, IReadOnlyList<EntityThumbnailHoverImage>>();
        }

        var sampledByRoot = directChildren
            .GroupBy(row => row.ParentEntityId!.Value)
            .ToDictionary(group => group.Key, group => PickSpread(group.ToArray(), MaxHoverImages));
        var sampledChildren = sampledByRoot.Values.SelectMany(children => children).ToArray();
        var representatives = await ResolveRepresentativeHoverImagesAsync(sampledChildren, hideNsfw, cancellationToken);

        return sampledByRoot.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<EntityThumbnailHoverImage>)pair.Value
                .Select(child => representatives.GetValueOrDefault(child.Id))
                .Where(image => image is not null)
                .Select(image => image!)
                .ToArray());
    }

    private async Task<IReadOnlyDictionary<Guid, EntityThumbnailHoverImage>> ResolveRepresentativeHoverImagesAsync(
        IReadOnlyList<EntityRow> candidates,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (candidates.Count == 0) {
            return new Dictionary<Guid, EntityThumbnailHoverImage>();
        }

        var results = new Dictionary<Guid, EntityThumbnailHoverImage>();
        var chains = candidates.ToDictionary(row => row.Id, row => new List<EntityRow> { row });
        var frontier = candidates.ToDictionary(row => row.Id, row => row);

        for (var depth = 0; depth < MaxHoverImageSearchDepth && frontier.Count > 0; depth++) {
            var parentIds = frontier.Values.Select(row => row.Id).ToArray();
            var childQuery = _db.Entities.AsNoTracking()
                .Where(row => row.ParentEntityId != null && parentIds.Contains(row.ParentEntityId.Value));
            childQuery = ApplyNsfwVisibility(childQuery, hideNsfw);
            var children = await childQuery
                .OrderBy(row => row.ParentEntityId)
                .ThenBy(row => row.SortOrder)
                .ThenBy(row => row.Title)
                .ThenBy(row => row.Id)
                .ToArrayAsync(cancellationToken);
            if (children.Length == 0) {
                break;
            }

            var firstChildByParent = children
                .GroupBy(row => row.ParentEntityId!.Value)
                .ToDictionary(group => group.Key, group => group.First());
            var nextFrontier = new Dictionary<Guid, EntityRow>();
            foreach (var rootId in frontier.Keys.ToArray()) {
                var current = frontier[rootId];
                if (firstChildByParent.TryGetValue(current.Id, out var child)) {
                    chains[rootId].Add(child);
                    nextFrontier[rootId] = child;
                }
            }

            frontier = nextFrontier;
        }

        var coverByEntity = await LoadCoverPathsAsync(
            chains.Values.SelectMany(chain => chain).Select(row => row.Id).Distinct().ToArray(),
            cancellationToken);

        foreach (var pair in chains) {
            var representative = pair.Value.FirstOrDefault(row => coverByEntity.ContainsKey(row.Id));
            if (representative is not null) {
                results[pair.Key] = new EntityThumbnailHoverImage(
                    representative.Id,
                    representative.Title,
                    coverByEntity[representative.Id]);
            }
        }

        return results;
    }

    private static IReadOnlyList<T> PickSpread<T>(IReadOnlyList<T> items, int limit) {
        if (items.Count <= limit) {
            return items;
        }

        var selected = new List<T>(limit);
        var usedIndexes = new HashSet<int>();
        for (var index = 0; index < limit; index++) {
            var sourceIndex = (int)Math.Round(index * (items.Count - 1) / (double)(limit - 1));
            if (usedIndexes.Add(sourceIndex)) {
                selected.Add(items[sourceIndex]);
            }
        }

        return selected;
    }

    private static bool UsesRepresentativeCover(string kindCode) =>
        kindCode == EntityKindRegistry.Book.Code ||
        kindCode == EntityKindRegistry.BookVolume.Code ||
        kindCode == EntityKindRegistry.BookChapter.Code ||
        kindCode == EntityKindRegistry.Gallery.Code;
}
