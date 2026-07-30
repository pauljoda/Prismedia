using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Entities.Thumbnails;
using Prismedia.Infrastructure.Media.Processing;
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
        bool enforceLibraryVisibility,
        CancellationToken cancellationToken,
        bool resolveCollectionArtwork = true) {
        if (rows.Count == 0) {
            return [];
        }

        var ids = rows.Select(entity => entity.Id).ToArray();
        // Contributors and owning-child technical fallback aggregate only over entities the caller
        // can see. Wanted placeholders are intentionally excluded from membership facts, matching
        // direct child projections that reserve wanted rows for explicit acquisition surfaces.
        var visibleContributionEntities = _db.Entities.AsNoTracking()
            .Where(entity => !entity.IsWanted);
        if (enforceLibraryVisibility) {
            visibleContributionEntities = ApplyEnabledLibraryVisibility(visibleContributionEntities);
        }
        visibleContributionEntities = ApplyNsfwVisibility(visibleContributionEntities, hideNsfw);
        var parentIds = rows
            .Select(entity => entity.ParentEntityId)
            .Where(parentId => parentId is not null)
            .Select(parentId => parentId!.Value)
            .Distinct()
            .ToArray();
        var parentQuery = _db.Entities.AsNoTracking()
            .Where(parent => parentIds.Contains(parent.Id));
        if (enforceLibraryVisibility) {
            parentQuery = ApplyEnabledLibraryVisibility(parentQuery);
        }
        parentQuery = ApplyNsfwVisibility(parentQuery, hideNsfw);
        var parentRowsByEntity = parentIds.Length == 0
            ? new Dictionary<Guid, EntityRow>()
            : await parentQuery.ToDictionaryAsync(parent => parent.Id, cancellationToken);
        var parentKindByEntity = parentRowsByEntity.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.KindCode);
        var coverParentIds = rows
            .Where(row =>
                row.ParentEntityId is { } parentId &&
                parentRowsByEntity.TryGetValue(parentId, out var parent) &&
                CanBorrowParentCover(row.KindCode, parent.KindCode))
            .Select(row => row.ParentEntityId!.Value)
            .Distinct()
            .ToArray();
        var coverByEntity = await LoadCoverPathsAsync(ids, cancellationToken);
        var coverByParent = coverParentIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await LoadCoverPathsAsync(coverParentIds, cancellationToken);
        var hoverFiles = await _db.EntityFiles.AsNoTracking()
            .Where(file => ids.Contains(file.EntityId) && file.Role == EntityFileRole.Trickplay)
            .Where(file => file.Path.EndsWith(".m3u8") || file.Path.EndsWith(".vtt"))
            .OrderByDescending(file => file.Path.EndsWith(".m3u8"))
            .ThenBy(file => file.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var hoverByEntity = hoverFiles
            .GroupBy(file => file.EntityId)
            .ToDictionary(group => group.Key, group => group.First().Path);
        var hoverImagesByEntity = await ProjectHoverImagesAsync(rows, hideNsfw, enforceLibraryVisibility, cancellationToken);
        var collectionRowsNeedingArtwork = rows
            .Where(row => row.KindCode == EntityKind.Collection.ToCode() && !coverByEntity.ContainsKey(row.Id))
            .ToArray();
        var collectionArtworkByEntity = resolveCollectionArtwork
            ? await ProjectCollectionArtworkAsync(collectionRowsNeedingArtwork, hideNsfw, enforceLibraryVisibility, cancellationToken)
            : new Dictionary<Guid, CollectionArtwork>();
        var technicalByEntity = await _db.EntityTechnical.AsNoTracking()
            .Where(technical => ids.Contains(technical.EntityId))
            .ToDictionaryAsync(technical => technical.EntityId, cancellationToken);
        var movieIds = rows
            .Where(row => resolveCollectionArtwork && row.KindCode == EntityKind.Movie.ToCode())
            .Select(row => row.Id)
            .ToArray();
        var movieIdsMissingTechnical = movieIds
            .Where(movieId => !technicalByEntity.ContainsKey(movieId))
            .ToArray();
        // A movie owns exactly one playable video in the canonical model. When that single visible
        // child holds the probe row, project it onto the movie without hydrating the child. NOT EXISTS
        // enforces unambiguous ownership in one batched query; an own movie technical row still wins.
        var movieChildTechnicalRows = movieIdsMissingTechnical.Length == 0
            ? []
            : await (
                from child in visibleContributionEntities
                where child.ParentEntityId != null &&
                    movieIdsMissingTechnical.Contains(child.ParentEntityId.Value) &&
                    child.KindCode == EntityKind.Video.ToCode() &&
                    !visibleContributionEntities.Any(other =>
                        other.ParentEntityId == child.ParentEntityId &&
                        other.KindCode == EntityKind.Video.ToCode() &&
                        other.Id != child.Id)
                join technical in _db.EntityTechnical.AsNoTracking()
                    on child.Id equals technical.EntityId
                select new { MovieId = child.ParentEntityId!.Value, Technical = technical })
                .ToArrayAsync(cancellationToken);
        var movieChildTechnicalByEntity = movieChildTechnicalRows.ToDictionary(
            row => row.MovieId,
            row => row.Technical);
        // Section (disc) labels for audio tracks, surfaced as a thumbnail chip so album track
        // lists can group multi-disc albums and restart numbering per section.
        var sectionByEntity = rows.Any(row => row.KindCode == EntityKind.AudioTrack.ToCode())
            ? await _db.AudioTrackDetails.AsNoTracking()
                .Where(detail => ids.Contains(detail.EntityId) && detail.SectionLabel != null)
                .ToDictionaryAsync(detail => detail.EntityId, detail => detail.SectionLabel!, cancellationToken)
            : new Dictionary<Guid, string>();
        var bookTypeByEntity = rows.Any(row => row.KindCode == EntityKind.Book.ToCode())
            ? await _db.BookDetails.AsNoTracking()
                .Where(detail => ids.Contains(detail.EntityId))
                .ToDictionaryAsync(detail => detail.EntityId, detail => detail.BookType, cancellationToken)
            : new Dictionary<Guid, BookType>();
        // Grid variants are loaded for the page's entities plus every entity whose cover a
        // row can borrow (owning album/movie parents and representative-cover children for
        // galleries and collections), so a card that inherits another entity's cover also
        // inherits that entity's small variants instead of downloading the full original.
        var borrowableCoverSourceIds = ids
            .Concat(coverParentIds)
            .Concat(hoverImagesByEntity.Values.SelectMany(images => images.Select(image => image.EntityId)))
            .Concat(collectionArtworkByEntity.Values.SelectMany(artwork => artwork.HoverImages.Select(image => image.EntityId)))
            .Distinct()
            .ToArray();
        var gridThumbRows = await _db.EntityFiles.AsNoTracking()
            .Where(file => borrowableCoverSourceIds.Contains(file.EntityId) &&
                (file.Role == EntityFileRole.GridThumbnail || file.Role == EntityFileRole.GridThumbnail2x))
            .Select(file => new { file.EntityId, file.Role, file.Path })
            .ToArrayAsync(cancellationToken);
        var usableGridThumbRows = gridThumbRows
            .Where(file => HasUsableAssetPath(file.Path))
            .ToArray();
        var gridThumbByEntity = usableGridThumbRows
            .Where(file => file.Role == EntityFileRole.GridThumbnail)
            .ToDictionary(file => file.EntityId, file => file.Path);
        var gridThumb2xByEntity = usableGridThumbRows
            .Where(file => file.Role == EntityFileRole.GridThumbnail2x)
            .ToDictionary(file => file.EntityId, file => file.Path);
        var currentUserId = CurrentUserId;
        var stateByEntity = currentUserId == Guid.Empty
            ? new Dictionary<Guid, UserEntityStateRow>()
            : await _db.UserEntityStates.AsNoTracking()
                .Where(state => state.UserId == currentUserId && ids.Contains(state.EntityId))
                .ToDictionaryAsync(state => state.EntityId, cancellationToken);
        var progressCurrentIds = stateByEntity.Values
            .Select(state => state.ProgressCurrentEntityId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var progressStateByEntity = progressCurrentIds.Length == 0 || currentUserId == Guid.Empty
            ? new Dictionary<Guid, UserEntityStateRow>()
            : await _db.UserEntityStates.AsNoTracking()
                .Where(state => state.UserId == currentUserId && progressCurrentIds.Contains(state.EntityId))
                .ToDictionaryAsync(state => state.EntityId, cancellationToken);
        var technicalByProgressEntity = progressCurrentIds.Length == 0
            ? new Dictionary<Guid, EntityTechnicalRow>()
            : await _db.EntityTechnical.AsNoTracking()
                .Where(technical => progressCurrentIds.Contains(technical.EntityId))
                .ToDictionaryAsync(technical => technical.EntityId, cancellationToken);
        var childStateByMovie = movieIds.Length == 0 || currentUserId == Guid.Empty
            ? new Dictionary<Guid, UserEntityStateRow>()
            : await LoadMovieChildStateAsync(movieIds, cancellationToken);
        var movieChildStateIds = childStateByMovie.Values
            .Select(state => state.EntityId)
            .Distinct()
            .ToArray();
        var technicalByMovieChild = movieChildStateIds.Length == 0
            ? new Dictionary<Guid, EntityTechnicalRow>()
            : await _db.EntityTechnical.AsNoTracking()
                .Where(technical => movieChildStateIds.Contains(technical.EntityId))
                .ToDictionaryAsync(technical => technical.EntityId, cancellationToken);

        // Compact availability facts for this page: physical source-media truth plus acquisition state
        // projected through every structural subtree. The singular direct status remains for the existing
        // badge while filters use plural membership, including child and upgrade work.
        var sourceMediaIds = await _sourceOwnership.ResolveAsync(ids, cancellationToken);
        var acquisitionStatusesByEntity = await _acquisitionStatuses.ResolveAsync(ids, cancellationToken);

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
            stateByEntity.TryGetValue(row.Id, out var ownState);
            childStateByMovie.TryGetValue(row.Id, out var childState);
            var playbackState = ownState is not null && Mappers.Capabilities.UserEntityStateColumns.HasPlayback(ownState)
                ? ownState
                : childState ?? ownState;
            var playbackDurationSeconds = playbackState is not null && playbackState.EntityId != row.Id
                ? technicalByMovieChild.GetValueOrDefault(playbackState.EntityId)?.DurationSeconds
                : technicalByEntity.GetValueOrDefault(row.Id)?.DurationSeconds;
            var progressState = ownState?.ProgressCurrentEntityId is { } progressEntityId
                ? progressStateByEntity.GetValueOrDefault(progressEntityId)
                : null;
            var progressDurationSeconds = progressState is null
                ? null
                : technicalByProgressEntity.GetValueOrDefault(progressState.EntityId)?.DurationSeconds;
            var hoverUrl = hoverByEntity.GetValueOrDefault(row.Id);
            var hoverImages = hoverImagesByEntity.GetValueOrDefault(row.Id) ?? [];
            var coverUrl = coverByEntity.GetValueOrDefault(row.Id);
            var bookType = bookTypeByEntity.TryGetValue(row.Id, out var value) ? value : (BookType?)null;
            var thumbnailTechnical = technicalByEntity.GetValueOrDefault(row.Id)
                ?? (row.KindCode == EntityKind.Movie.ToCode()
                    ? movieChildTechnicalByEntity.GetValueOrDefault(row.Id)
                    : null);
            // The entity that owns the cover image this card shows. Rows that borrow another
            // entity's cover borrow that entity's grid variants too, so the srcset pair keeps
            // matching the picture the card actually displays.
            var coverSourceId = row.Id;
            if (coverUrl is null &&
                row.ParentEntityId is { } coverParentId &&
                parentRowsByEntity.TryGetValue(coverParentId, out var parent) &&
                CanBorrowParentCover(row.KindCode, parent.KindCode)) {
                coverUrl = coverByParent.GetValueOrDefault(coverParentId);
                if (coverUrl is not null) {
                    coverSourceId = coverParentId;
                }
            }

            if (coverUrl is null &&
                row.KindCode == EntityKind.Collection.ToCode() &&
                collectionArtworkByEntity.TryGetValue(row.Id, out var collectionArtwork)) {
                coverUrl = collectionArtwork.CoverUrl ?? collectionArtwork.HoverImages.FirstOrDefault()?.Path;
                hoverImages = collectionArtwork.HoverImages;
                coverSourceId = hoverImages.FirstOrDefault(image => image.Path == coverUrl)?.EntityId ?? row.Id;
            }

            if (coverUrl is null && UsesRepresentativeCover(row.KindCode) && hoverImages.Count > 0) {
                coverUrl = hoverImages[0].Path;
                coverSourceId = hoverImages[0].EntityId;
            }

            var coverThumbUrl = gridThumbByEntity.GetValueOrDefault(coverSourceId) ?? coverUrl;
            var coverThumb2xUrl = gridThumb2xByEntity.GetValueOrDefault(coverSourceId) ?? coverThumbUrl;

            return new EntityThumbnail(
                row.Id,
                row.KindCode.DecodeAs<EntityKind>(),
                row.Title,
                row.ParentEntityId,
                row.SortOrder,
                coverUrl,
                coverThumbUrl,
                hoverUrl is null ? ThumbnailHoverKind.None : ThumbnailHoverKind.Sprite,
                hoverUrl,
                hoverImages,
                ProjectThumbnailMeta(
                    row,
                    thumbnailTechnical,
                    sectionByEntity.GetValueOrDefault(row.Id),
                    bookType),
                ownState?.RatingValue,
                ownState?.IsFavorite ?? false,
                row.IsNsfw,
                row.IsOrganized) {
                CoverThumb2xUrl = coverThumb2xUrl,
                ParentKind = row.ParentEntityId is { } parentId
                    && parentKindByEntity.TryGetValue(parentId, out var parentKindCode)
                    && parentKindCode.TryDecodeAs<EntityKind>(out var parentKind)
                        ? parentKind
                        : null,
                Subtitle = row.ParentEntityId is { } subtitleParentId
                    ? parentRowsByEntity.GetValueOrDefault(subtitleParentId)?.Title
                    : null,
                IsWanted = row.IsWanted,
                HasSourceMedia = sourceMediaIds.Contains(row.Id),
                LatestAcquisitionStatus = acquisitionStatusesByEntity.GetValueOrDefault(row.Id)?.LatestDirectStatus,
                AcquisitionStatuses = acquisitionStatusesByEntity.GetValueOrDefault(row.Id)?.Statuses ?? [],
                WantedStatus = row.IsWanted
                    && acquisitionStatusesByEntity.GetValueOrDefault(row.Id)?.LatestDirectStatus is { } wantedStatus
                    ? wantedStatus
                    : null,
                CreatedAt = row.CreatedAt,
                PlayCount = playbackState?.PlayCount,
                ResumeSeconds = playbackState is { ResumeSeconds: > 0 }
                    ? playbackState.ResumeSeconds
                    : null,
                Genres = tagsByEntity.GetValueOrDefault(row.Id),
                Progress = ResolveThumbnailProgress(
                    playbackState,
                    playbackDurationSeconds,
                    progressState,
                    progressDurationSeconds)
            };
        }).ToArray();

        // Recursive collection-artwork projection needs only cover/hover data. Skipping contributors
        // here prevents collection members that are themselves containers from adding aggregate
        // queries to the outer thumbnail page.
        if (!resolveCollectionArtwork) {
            return baseThumbnails;
        }

        // Let registered contributors fold in extra, kind-scoped data (e.g. taxonomy reference
        // counts) over the whole page. Each self-filters and runs at most one batched query, and
        // they share the scoped DbContext so they run sequentially. Contributed identity/count chips
        // lead lower-value technical chips so useful counts survive the five-chip cap.
        var contributions = new ThumbnailContributions(rows, visibleContributionEntities);
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
                : extraMeta.Concat(thumbnail.Meta).Take(MaxThumbnailMeta).ToArray();
            return thumbnail with { Meta = meta, ReferenceCounts = referenceCounts };
        }).ToArray();
    }

    private async Task<Dictionary<Guid, UserEntityStateRow>> LoadMovieChildStateAsync(
        IReadOnlyCollection<Guid> movieIds,
        CancellationToken cancellationToken) {
        var childRows = await _db.Entities.AsNoTracking()
            .Where(child => child.ParentEntityId != null && movieIds.Contains(child.ParentEntityId.Value))
            .Select(child => new { child.Id, ParentId = child.ParentEntityId!.Value })
            .ToArrayAsync(cancellationToken);
        if (childRows.Length == 0) {
            return new Dictionary<Guid, UserEntityStateRow>();
        }

        var parentByChild = childRows.ToDictionary(child => child.Id, child => child.ParentId);
        var childIds = parentByChild.Keys.ToArray();
        var userId = CurrentUserId;
        var stateRows = await _db.UserEntityStates.AsNoTracking()
            .Where(state => state.UserId == userId && childIds.Contains(state.EntityId))
            .ToArrayAsync(cancellationToken);

        return stateRows
            .Where(Mappers.Capabilities.UserEntityStateColumns.HasPlayback)
            .GroupBy(state => parentByChild[state.EntityId])
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(state => state.CompletedAt is not null)
                    .ThenByDescending(state => state.PlayCount)
                    .ThenByDescending(state => state.ResumeSeconds)
                    .First());
    }

    private async Task<IReadOnlyDictionary<Guid, CollectionArtwork>> ProjectCollectionArtworkAsync(
        IReadOnlyList<EntityRow> rows,
        bool hideNsfw,
        bool enforceLibraryVisibility,
        CancellationToken cancellationToken) {
        var collectionIds = rows
            .Where(row => row.KindCode == EntityKind.Collection.ToCode())
            .Select(row => row.Id)
            .Distinct()
            .ToArray();
        if (collectionIds.Length == 0) {
            return new Dictionary<Guid, CollectionArtwork>();
        }

        var detailsByCollection = await _db.CollectionDetails.AsNoTracking()
            .Where(detail => collectionIds.Contains(detail.EntityId))
            .ToDictionaryAsync(detail => detail.EntityId, cancellationToken);
        var visibleMembers = await LoadVisibleCollectionMembersAsync(
            collectionIds,
            hideNsfw,
            enforceLibraryVisibility,
            cancellationToken);
        var sampledMembersByCollection = visibleMembers
            .GroupBy(row => row.CollectionEntityId)
            .ToDictionary(
                group => group.Key,
                group => PickSpread(group.ToArray(), MaxHoverImages));
        var representativeByCollection = ResolveCollectionRepresentatives(
            collectionIds,
            detailsByCollection,
            visibleMembers);
        var thumbnailIds = representativeByCollection.Values
            .Concat(sampledMembersByCollection.Values.SelectMany(members => members.Select(member => member.ItemEntityId)))
            .Distinct()
            .ToArray();
        if (thumbnailIds.Length == 0) {
            return new Dictionary<Guid, CollectionArtwork>();
        }

        var thumbnailRows = await LoadVisibleThumbnailRowsAsync(
            thumbnailIds,
            hideNsfw,
            enforceLibraryVisibility,
            cancellationToken);
        var thumbnails = await ProjectThumbnailsAsync(
            thumbnailRows,
            hideNsfw,
            enforceLibraryVisibility,
            cancellationToken,
            resolveCollectionArtwork: false);
        var thumbnailsById = thumbnails.ToDictionary(thumbnail => thumbnail.Id);

        return collectionIds
            .Select(collectionId => ProjectCollectionArtwork(
                collectionId,
                detailsByCollection.GetValueOrDefault(collectionId),
                representativeByCollection,
                sampledMembersByCollection,
                thumbnailsById))
            .Where(pair => pair.Value.HasArtwork)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private async Task<IReadOnlyList<CollectionMemberRow>> LoadVisibleCollectionMembersAsync(
        IReadOnlyCollection<Guid> collectionIds,
        bool hideNsfw,
        bool enforceLibraryVisibility,
        CancellationToken cancellationToken) {
        // Resolve the small collection-membership set before applying the full library visibility
        // projection. Joining visibility against the entire entity table first produced a very large
        // anti-join plan (and a 20+ second query on the rich-data library) even for eleven members.
        var memberRows = await _db.CollectionItemDetails.AsNoTracking()
            .Where(item => collectionIds.Contains(item.CollectionEntityId))
            .Select(item => new {
                item.Id,
                item.CollectionEntityId,
                item.ItemEntityId,
                item.SortOrder
            })
            .ToArrayAsync(cancellationToken);
        if (memberRows.Length == 0) {
            return [];
        }

        var memberIds = memberRows
            .Select(item => item.ItemEntityId)
            .Distinct()
            .ToArray();
        var allEntities = _db.Entities.AsNoTracking();
        var candidateEntities = await allEntities
            .ExcludeBookOwnedAudioTracks(allEntities)
            .Where(entity => memberIds.Contains(entity.Id))
            .Select(entity => new { entity.Id, entity.KindCode, entity.Title })
            .ToArrayAsync(cancellationToken);

        // Visibility has materially smaller SQL when the kind is known (direct-root books/videos,
        // inherited audio tracks, and so on). Grouping this already-bounded member set avoids making
        // EF translate the generic all-kind hierarchy expression on every collection page request.
        var visibleIds = new HashSet<Guid>();
        foreach (var kindGroup in candidateEntities.GroupBy(entity => entity.KindCode)) {
            var idsForKind = kindGroup.Select(entity => entity.Id).ToArray();
            var visibleForKind = _db.Entities.AsNoTracking()
                .Where(entity => idsForKind.Contains(entity.Id));
            if (enforceLibraryVisibility) {
                visibleForKind = ApplyEnabledLibraryVisibility(visibleForKind, kindGroup.Key);
            }
            visibleForKind = ApplyNsfwVisibility(visibleForKind, hideNsfw);
            visibleIds.UnionWith(await visibleForKind
                .Select(entity => entity.Id)
                .ToArrayAsync(cancellationToken));
        }

        var visibleTitles = candidateEntities
            .Where(entity => visibleIds.Contains(entity.Id))
            .ToDictionary(entity => entity.Id, entity => entity.Title);

        return memberRows
            .Where(item => visibleTitles.ContainsKey(item.ItemEntityId))
            .OrderBy(item => item.CollectionEntityId)
            .ThenBy(item => item.SortOrder)
            .ThenBy(item => visibleTitles[item.ItemEntityId])
            .ThenBy(item => item.Id)
            .Select(item => new CollectionMemberRow(item.CollectionEntityId, item.ItemEntityId))
            .ToArray();
    }

    private async Task<IReadOnlyList<EntityRow>> LoadVisibleThumbnailRowsAsync(
        IReadOnlyCollection<Guid> thumbnailIds,
        bool hideNsfw,
        bool enforceLibraryVisibility,
        CancellationToken cancellationToken) {
        var query = _db.Entities.AsNoTracking()
            .Where(entity => thumbnailIds.Contains(entity.Id));
        if (enforceLibraryVisibility) {
            query = ApplyEnabledLibraryVisibility(query);
        }
        query = ApplyNsfwVisibility(query, hideNsfw);
        return await query.ToArrayAsync(cancellationToken);
    }

    private static IReadOnlyDictionary<Guid, Guid> ResolveCollectionRepresentatives(
        IReadOnlyCollection<Guid> collectionIds,
        IReadOnlyDictionary<Guid, CollectionDetailRow> detailsByCollection,
        IReadOnlyList<CollectionMemberRow> visibleMembers) {
        var firstMemberByCollection = visibleMembers
            .GroupBy(row => row.CollectionEntityId)
            .ToDictionary(group => group.Key, group => group.First().ItemEntityId);
        var representatives = new Dictionary<Guid, Guid>();
        foreach (var collectionId in collectionIds) {
            if (detailsByCollection.TryGetValue(collectionId, out var detail) &&
                detail.CoverItemEntityId is { } coverItemId) {
                representatives[collectionId] = coverItemId;
                continue;
            }

            if (firstMemberByCollection.TryGetValue(collectionId, out var memberId)) {
                representatives[collectionId] = memberId;
            }
        }

        return representatives;
    }

    private static KeyValuePair<Guid, CollectionArtwork> ProjectCollectionArtwork(
        Guid collectionId,
        CollectionDetailRow? detail,
        IReadOnlyDictionary<Guid, Guid> representativeByCollection,
        IReadOnlyDictionary<Guid, IReadOnlyList<CollectionMemberRow>> sampledMembersByCollection,
        IReadOnlyDictionary<Guid, EntityThumbnail> thumbnailsById) {
        var coverUrl = representativeByCollection.TryGetValue(collectionId, out var representativeId) &&
            thumbnailsById.TryGetValue(representativeId, out var representative)
                ? representative.CoverUrl
                : null;
        IReadOnlyList<EntityThumbnailHoverImage> hoverImages = detail?.CoverMode == CollectionCoverMode.Item
            ? []
            : ProjectCollectionHoverImages(collectionId, sampledMembersByCollection, thumbnailsById);

        return new KeyValuePair<Guid, CollectionArtwork>(
            collectionId,
            new CollectionArtwork(coverUrl, hoverImages));
    }

    private static IReadOnlyList<EntityThumbnailHoverImage> ProjectCollectionHoverImages(
        Guid collectionId,
        IReadOnlyDictionary<Guid, IReadOnlyList<CollectionMemberRow>> sampledMembersByCollection,
        IReadOnlyDictionary<Guid, EntityThumbnail> thumbnailsById) {
        if (!sampledMembersByCollection.TryGetValue(collectionId, out var members)) {
            return [];
        }

        return members
            .Select(member => thumbnailsById.GetValueOrDefault(member.ItemEntityId))
            .Where(thumbnail => thumbnail?.CoverUrl is not null)
            .Select(thumbnail => new EntityThumbnailHoverImage(thumbnail!.Id, thumbnail.Title, thumbnail.CoverUrl!))
            .DistinctBy(image => image.Path, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Computes the 0..1 progress meter fraction for a thumbnail from the user's state row.
    /// Playback (video/audio) takes precedence: a completed item reads 1.0, an item with a
    /// stored resume position reads its fraction of the known runtime, and anything else
    /// reads <c>null</c>. Reading progress (books) falls back to completed → 1.0 or the
    /// current index over the total. A container cursor also blends the current episode's playback
    /// fraction into that index. Returns <c>null</c> when there is nothing meaningful to show.
    /// </summary>
    private static double? ResolveThumbnailProgress(
        UserEntityStateRow? state,
        double? durationSeconds,
        UserEntityStateRow? progressEntityState,
        double? progressEntityDurationSeconds) {
        if (state is null) {
            return null;
        }

        if (Mappers.Capabilities.UserEntityStateColumns.HasPlayback(state)) {
            if (state.CompletedAt is not null) {
                return 1.0;
            }

            if (state.ResumeSeconds > 0 && durationSeconds is > 0) {
                return Math.Clamp(state.ResumeSeconds / durationSeconds.Value, 0, 1);
            }

            return null;
        }

        if (state.ProgressCompletedAt is not null) {
            return 1.0;
        }

        if (state.ProgressTotal > 0 && (state.ProgressCurrentEntityId is not null || state.ProgressIndex > 0)) {
            var currentFraction = progressEntityState switch {
                { CompletedAt: not null } => 1,
                { ResumeSeconds: > 0 } when progressEntityDurationSeconds is > 0 =>
                    Math.Clamp(progressEntityState.ResumeSeconds / progressEntityDurationSeconds.Value, 0, 1),
                _ => 0
            };
            var fraction = Math.Clamp(
                (state.ProgressIndex + currentFraction) / state.ProgressTotal,
                0,
                1);
            return fraction > 0 ? fraction : null;
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
            .Where(HasUsableAssetPath)
            .GroupBy(file => file.EntityId)
            .ToDictionary(
                group => group.Key,
                group => EntityCoverSelection.Select(group)!.Path);
    }

    private bool HasUsableAssetPath(EntityFileRow file) =>
        HasUsableAssetPath(file.Path);

    private bool HasUsableAssetPath(string path) {
        if (!path.StartsWith(AssetPaths.AssetsUrlPrefix, StringComparison.Ordinal)) {
            return true;
        }

        if (_assets is null) {
            return true;
        }

        var diskPath = _assets.ResolveAssetDiskPath(path);
        return diskPath is not null && File.Exists(diskPath);
    }

    private static IReadOnlyList<EntityThumbnailMeta> ProjectThumbnailMeta(
        EntityRow row,
        EntityTechnicalRow? technical,
        string? sectionLabel = null,
        BookType? bookType = null) {
        var meta = new List<EntityThumbnailMeta>(MaxThumbnailMeta);
        // Lead with the disc/section chip so it survives the meta cap and clients can group tracks.
        Add(meta, EntityThumbnailMetaIcons.Disc, sectionLabel);
        Add(meta, EntityThumbnailMetaIcons.Book, FormatBookType(bookType));

        if (technical is null) {
            return meta;
        }

        Add(meta, EntityThumbnailMetaIcons.Duration, FormatDuration(technical.DurationSeconds));
        var usesVideoTechnical = row.KindCode == EntityKind.Video.ToCode() ||
            row.KindCode == EntityKind.Movie.ToCode();
        if (technical.Width is { } width && technical.Height is { } height) {
            Add(
                meta,
                usesVideoTechnical ? EntityThumbnailMetaIcons.Video : EntityThumbnailMetaIcons.Image,
                FormatResolution(width, height));
        }

        if (usesVideoTechnical) {
            Add(meta, EntityThumbnailMetaIcons.Video, technical.Codec?.ToUpperInvariant());
            Add(meta, EntityThumbnailMetaIcons.Video, technical.Container?.ToUpperInvariant());
        } else if (row.KindCode == EntityKind.AudioTrack.ToCode()) {
            Add(meta, EntityThumbnailMetaIcons.Audio, technical.Codec?.ToUpperInvariant());
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
        // Width participates so scope and other cropped masters keep their source tier.
        // For example, a 3840x1920 frame is still a 4K source even though its shorter
        // edge sits below the conventional 2160-line threshold.
        if (width >= 7600 || height >= 4300) return "8K";
        if (width >= 3800 || height >= 2000) return "4K";
        if (width >= 2540 || height >= 1400) return "1440p";
        if (width >= 1800 || height >= 1000) return "1080p";
        if (width >= 1200 || height >= 700) return "720p";
        if (width >= 640 || height >= 480) return "480p";
        return $"{width}x{height}";
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<EntityThumbnailHoverImage>>> ProjectHoverImagesAsync(
        IReadOnlyList<EntityRow> rows,
        bool hideNsfw,
        bool enforceLibraryVisibility,
        CancellationToken cancellationToken) {
        if (rows.Count == 0) {
            return new Dictionary<Guid, IReadOnlyList<EntityThumbnailHoverImage>>();
        }

        var rootIds = rows.Select(row => row.Id).ToArray();
        var directChildQuery = _db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId != null && rootIds.Contains(row.ParentEntityId.Value));
        if (enforceLibraryVisibility) {
            directChildQuery = ApplyEnabledLibraryVisibility(directChildQuery);
        }
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
        var representatives = await ResolveRepresentativeHoverImagesAsync(sampledChildren, hideNsfw, enforceLibraryVisibility, cancellationToken);

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
        bool enforceLibraryVisibility,
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
            if (enforceLibraryVisibility) {
                childQuery = ApplyEnabledLibraryVisibility(childQuery);
            }
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
        EntityKindRegistry.TryDescribe(kindCode, out var definition) &&
        definition.Presentation.UsesRepresentativeChildArtwork;

    private static bool CanBorrowParentCover(string childKindCode, string parentKindCode) =>
        EntityKindRegistry.TryDescribe(childKindCode, out var child) &&
        EntityKindRegistry.TryDescribe(parentKindCode, out var parent) &&
        child.Presentation.BorrowArtworkFromParentKinds.Contains(parent.Kind);

    private sealed record CollectionArtwork(
        string? CoverUrl,
        IReadOnlyList<EntityThumbnailHoverImage> HoverImages) {
        public bool HasArtwork => CoverUrl is not null || HoverImages.Count > 0;
    }

    private sealed record CollectionMemberRow(Guid CollectionEntityId, Guid ItemEntityId);
}
