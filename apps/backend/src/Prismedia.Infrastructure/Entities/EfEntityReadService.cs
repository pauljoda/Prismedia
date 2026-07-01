using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// EF Core adapter for <see cref="IEntityReadService"/>. Card and detail reads flow
/// through the hydrated domain entity and <see cref="EntityCardProjector"/>; the
/// browse and thumbnail path stays a deliberate row-optimized projection so list
/// pages do not pay the full hydration cost. Kind-specific detail DTO projection is
/// delegated to <see cref="IEntityKindMapper.ProjectDetail"/> so this service stays a
/// coordinator and never branches on a concrete entity kind.
/// </summary>
public sealed partial class EfEntityReadService : IEntityReadService {
    private const int DefaultPageSize = 250;
    private const int MaxPageSize = 1000;
    private const int MaxHoverImages = 5;
    private const int MaxHoverImageSearchDepth = 3;
    private const int MaxThumbnailMeta = 5;

    private readonly PrismediaDbContext _db;
    private readonly EfEntityRepository _repository;
    private readonly IReadOnlyDictionary<EntityKind, IEntityKindMapper> _kindMappers;
    private readonly IReadOnlyList<Thumbnails.IThumbnailContributor> _thumbnailContributors;
    private readonly AssetPathService? _assets;

    public EfEntityReadService(
        PrismediaDbContext db,
        EfEntityRepository repository,
        IEnumerable<IEntityKindMapper> kindMappers,
        IEnumerable<Thumbnails.IThumbnailContributor> thumbnailContributors,
        AssetPathService? assets = null) {
        _db = db;
        _repository = repository;
        _kindMappers = kindMappers.ToDictionary(mapper => mapper.Kind);
        _thumbnailContributors = thumbnailContributors.ToArray();
        _assets = assets;
    }

    public async Task<EntityListResponse> ListAsync(
        string? kind,
        string? query,
        string? cursor,
        bool? hideNsfw,
        int? limit,
        CancellationToken cancellationToken,
        Guid? referencedBy = null,
        string? relationshipCode = null,
        string? sort = null,
        string? sortDir = null,
        int? seed = null,
        bool? favorite = null,
        bool? organized = null,
        int? ratingMin = null,
        int? ratingMax = null,
        bool? unrated = null,
        string? status = null,
        string? bookType = null,
        string? bookFormat = null,
        bool? nsfw = null,
        bool? hasFile = null,
        bool? played = null,
        bool? orphaned = null,
        bool? wanted = null) {
        var pageSize = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var normalizedRelationshipCode = string.IsNullOrWhiteSpace(relationshipCode)
            ? null
            : relationshipCode.Trim();
        var entityQuery = _db.Entities.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(kind)) {
            var kindCode = kind.Trim();
            entityQuery = entityQuery.Where(entity => entity.KindCode == kindCode);
            if (ListBrowseShowsOnlyTopLevel(kindCode)) {
                entityQuery = entityQuery.Where(entity => entity.ParentEntityId == null);
            }
        }

        if (!string.IsNullOrWhiteSpace(query)) {
            var normalized = query.Trim().ToLower();
            entityQuery = entityQuery.Where(entity => entity.Title.ToLower().Contains(normalized));
        }

        if (referencedBy is { } targetEntityId) {
            entityQuery = entityQuery.Where(entity =>
                _db.EntityRelationshipLinks.Any(link =>
                    link.TargetEntityId == targetEntityId &&
                    link.EntityId == entity.Id &&
                    (normalizedRelationshipCode == null || link.RelationshipCode == normalizedRelationshipCode)));
        }

        if (ShouldSuppressMovieChildVideos(kind, query, referencedBy)) {
            entityQuery = SuppressMovieChildVideos(entityQuery);
        }

        var enforceLibraryVisibility = await HasDisabledLibraryRootsAsync(cancellationToken);
        if (enforceLibraryVisibility) {
            entityQuery = ApplyEnabledLibraryVisibility(entityQuery, kind);
        }
        entityQuery = ApplyNsfwVisibility(entityQuery, hideNsfw == true);
        entityQuery = ApplyListFilters(entityQuery, favorite, organized, ratingMin, ratingMax, unrated, status, bookType, bookFormat, nsfw, hasFile, played, orphaned, wanted);

        // Snapshot the unbounded filtered total before applying the cursor; this is what
        // drives the client's page-of-pages and seek-to-end behaviour and must stay
        // independent of where in the cursor sequence we currently are.
        var totalCount = await entityQuery.CountAsync(cancellationToken);

        var offset = DecodeOffsetCursor(cursor);
        var sortKey = ParseSort(sort);
        var descending = string.Equals(sortDir?.Trim(), "desc", StringComparison.OrdinalIgnoreCase);

        EntityRow[] rows;
        if (sortKey == ListSort.Random) {
            // Random must shuffle the entire matching set, not just the loaded page,
            // and stay stable across paged requests with the same seed. We pull the
            // matching identifiers (cheap), order them by a deterministic seed-mixed
            // hash in memory, then hydrate only the page slice. This is provider
            // agnostic (PostgreSQL in production, SQLite under test) and avoids
            // depending on database-specific random/hash functions.
            var ids = await entityQuery
                .OrderBy(entity => entity.Id)
                .Select(entity => entity.Id)
                .ToArrayAsync(cancellationToken);
            var shuffled = DeterministicShuffle(ids, seed ?? 0);
            var pageIds = shuffled.Skip(offset).Take(pageSize + 1).ToArray();
            var rowsById = await _db.Entities.AsNoTracking()
                .Where(entity => pageIds.Contains(entity.Id))
                .ToDictionaryAsync(entity => entity.Id, cancellationToken);
            rows = pageIds
                .Where(rowsById.ContainsKey)
                .Select(id => rowsById[id])
                .ToArray();
        } else if (sortKey == ListSort.LastPlayed) {
            rows = await ApplyLastPlayedOrdering(entityQuery, descending)
                .Skip(offset)
                .Take(pageSize + 1)
                .ToArrayAsync(cancellationToken);
        } else if (sortKey == ListSort.References) {
            rows = await ApplyReferenceCountOrdering(entityQuery, descending)
                .Skip(offset)
                .Take(pageSize + 1)
                .ToArrayAsync(cancellationToken);
        } else {
            rows = await ApplyOrdering(entityQuery, sortKey, descending)
                .Skip(offset)
                .Take(pageSize + 1)
                .ToArrayAsync(cancellationToken);
        }

        var page = rows.Take(pageSize).ToArray();
        var thumbnails = await ProjectThumbnailsAsync(page, hideNsfw == true, enforceLibraryVisibility, cancellationToken);
        var nextCursor = rows.Length > pageSize ? EncodeOffsetCursor(offset + pageSize) : null;
        return new EntityListResponse(thumbnails, nextCursor, totalCount);
    }

    /// <summary>Sort strategies supported by the list/browse projection.</summary>
    private enum ListSort {
        Title,
        DateAdded,
        Rating,
        Random,
        LastPlayed,
        References,
    }

    /// <summary>
    /// Parses a comma-separated list of stable enum codes into the recognized enum values,
    /// silently dropping blanks and unknown codes. Used to turn filter query parameters such as
    /// <c>comic,manga</c> into the closed-set values applied to the book detail filter.
    /// </summary>
    private static List<TValue> ParseCodeList<TValue>(string? value)
        where TValue : struct, Enum {
        if (string.IsNullOrWhiteSpace(value)) {
            return [];
        }

        var parsed = new List<TValue>();
        foreach (var code in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (code.TryDecodeAs<TValue>(out var decoded) && !parsed.Contains(decoded)) {
                parsed.Add(decoded);
            }
        }

        return parsed;
    }

    private static ListSort ParseSort(string? sort) =>
        sort?.Trim().ToLowerInvariant() switch {
            "added" or "date" or "date-added" or "dateadded" or "createdat" or "created" or "recent" => ListSort.DateAdded,
            "rating" => ListSort.Rating,
            "random" or "shuffle" => ListSort.Random,
            "last-played" or "lastplayed" or "recently-played" or "recently-watched" or "played" => ListSort.LastPlayed,
            "references" or "reference-count" or "referencecount" or "refs" => ListSort.References,
            _ => ListSort.Title,
        };

    /// <summary>
    /// Applies a deterministic ORDER BY for the non-random sorts. Each strategy ends
    /// with a stable identifier tiebreaker so offset paging never skips or repeats a
    /// row, and rating always pushes unrated entities to the end regardless of
    /// direction.
    /// </summary>
    private static IQueryable<EntityRow> ApplyOrdering(IQueryable<EntityRow> query, ListSort sort, bool descending) =>
        sort switch {
            ListSort.DateAdded => descending
                ? query.OrderByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id)
                : query.OrderBy(entity => entity.CreatedAt).ThenBy(entity => entity.Id),
            ListSort.Rating => descending
                ? query.OrderBy(entity => entity.RatingValue == null)
                    .ThenByDescending(entity => entity.RatingValue)
                    .ThenBy(entity => entity.SortName)
                    .ThenBy(entity => entity.Id)
                : query.OrderBy(entity => entity.RatingValue == null)
                    .ThenBy(entity => entity.RatingValue)
                    .ThenBy(entity => entity.SortName)
                    .ThenBy(entity => entity.Id),
            _ => descending
                ? query.OrderByDescending(entity => entity.SortName).ThenByDescending(entity => entity.Id)
                : query.OrderBy(entity => entity.SortName).ThenBy(entity => entity.Id),
        };

    /// <summary>
    /// Orders entities by most recent engagement — the playback last-played time (videos/audio,
    /// falling back to the playback row's update time) or the reading-progress update time
    /// (books/comics). Entities with no engagement sort last regardless of direction, so the
    /// "recently played/watched" surfaces only lead with things the user has actually touched.
    /// </summary>
    private IQueryable<EntityRow> ApplyLastPlayedOrdering(IQueryable<EntityRow> query, bool descending) {
        var playback = _db.Set<EntityPlaybackRow>();
        var progress = _db.Set<EntityProgressRow>();
        var keyed = query.Select(entity => new {
            entity,
            recency = playback
                          .Where(row => row.EntityId == entity.Id)
                          .Select(row => (DateTimeOffset?)(row.LastPlayedAt ?? row.UpdatedAt))
                          .FirstOrDefault()
                      ?? progress
                          .Where(row => row.EntityId == entity.Id)
                          .Select(row => (DateTimeOffset?)row.UpdatedAt)
                          .FirstOrDefault()
        });

        var ordered = descending
            ? keyed.OrderByDescending(item => item.recency != null)
                .ThenByDescending(item => item.recency)
                .ThenByDescending(item => item.entity.CreatedAt)
                .ThenBy(item => item.entity.Id)
            : keyed.OrderByDescending(item => item.recency != null)
                .ThenBy(item => item.recency)
                .ThenBy(item => item.entity.CreatedAt)
                .ThenBy(item => item.entity.Id);

        return ordered.Select(item => item.entity);
    }

    /// <summary>
    /// Orders entities by how many distinct source entities reference them — the same count the
    /// reference-count chips show (a person's crediting media, a tag's tagged media). Used to sort
    /// taxonomy grids by usage; descending leads with the most-used entries. Ties break by title then
    /// id so offset paging stays stable. Entities with no references (count 0) sort to the end when
    /// descending and to the front when ascending, naturally.
    /// </summary>
    private IQueryable<EntityRow> ApplyReferenceCountOrdering(IQueryable<EntityRow> query, bool descending) {
        var links = _db.EntityRelationshipLinks;
        var keyed = query.Select(entity => new {
            entity,
            references = links
                .Where(link => link.TargetEntityId == entity.Id)
                .Select(link => link.EntityId)
                .Distinct()
                .Count()
        });

        var ordered = descending
            ? keyed.OrderByDescending(item => item.references)
                .ThenBy(item => item.entity.SortName)
                .ThenBy(item => item.entity.Id)
            : keyed.OrderBy(item => item.references)
                .ThenBy(item => item.entity.SortName)
                .ThenBy(item => item.entity.Id);

        return ordered.Select(item => item.entity);
    }

    /// <summary>
    /// Applies the server-side library filters that span the whole matching set:
    /// favorite and organized flags, rating bounds (including the explicit unrated
    /// case), and the adaptive engagement status. Status is resolved against both the
    /// playback capability (videos/audio) and the progress capability (books/comics)
    /// so a single control reads correctly for every kind that records engagement.
    /// </summary>
    private IQueryable<EntityRow> ApplyListFilters(
        IQueryable<EntityRow> query,
        bool? favorite,
        bool? organized,
        int? ratingMin,
        int? ratingMax,
        bool? unrated,
        string? status,
        string? bookType = null,
        string? bookFormat = null,
        bool? nsfw = null,
        bool? hasFile = null,
        bool? played = null,
        bool? orphaned = null,
        bool? wanted = null) {
        if (favorite == true) {
            query = query.Where(entity => entity.IsFavorite);
        }

        if (wanted is { } wantsWanted) {
            query = wantsWanted
                ? query.Where(entity => entity.IsWanted)
                : query.Where(entity => !entity.IsWanted);
        }

        if (orphaned is { } wantsOrphaned) {
            var links = _db.EntityRelationshipLinks;
            // Orphaned = nothing references this entity (no inbound relationship link).
            query = wantsOrphaned
                ? query.Where(entity => !links.Any(link => link.TargetEntityId == entity.Id))
                : query.Where(entity => links.Any(link => link.TargetEntityId == entity.Id));
        }

        if (nsfw is { } wantsNsfw) {
            query = wantsNsfw
                ? query.Where(entity => entity.IsNsfw)
                : query.Where(entity => !entity.IsNsfw);
        }

        if (hasFile is { } wantsFile) {
            var files = _db.EntityFiles;
            query = wantsFile
                ? query.Where(entity => files.Any(file => file.EntityId == entity.Id))
                : query.Where(entity => !files.Any(file => file.EntityId == entity.Id));
        }

        if (played is { } wantsPlayed) {
            var playbackRows = _db.Set<EntityPlaybackRow>();
            var progressRows = _db.Set<EntityProgressRow>();
            var entities = _db.Entities;
            // "Played" means any recorded engagement: a play/resume/completion (videos/audio) or
            // started/completed reading progress (books/comics). Mirrors the unwatched status logic.
            // Movies also honor direct child playback because a Prismedia movie is browsed as the
            // movie entity but can stream through its child video entity.
            query = wantsPlayed
                ? query.Where(entity =>
                    playbackRows.Any(row => row.EntityId == entity.Id &&
                        (row.CompletedAt != null || row.PlayCount > 0 || row.ResumeSeconds > 0)) ||
                    (entity.KindCode == EntityKindRegistry.Movie.Code &&
                        entities.Any(child => child.ParentEntityId == entity.Id &&
                            playbackRows.Any(row => row.EntityId == child.Id &&
                                (row.CompletedAt != null || row.PlayCount > 0 || row.ResumeSeconds > 0)))) ||
                    progressRows.Any(row => row.EntityId == entity.Id &&
                        (row.CompletedAt != null || row.Index > 0)))
                : query.Where(entity =>
                    !playbackRows.Any(row => row.EntityId == entity.Id &&
                        (row.CompletedAt != null || row.PlayCount > 0 || row.ResumeSeconds > 0)) &&
                    !(entity.KindCode == EntityKindRegistry.Movie.Code &&
                        entities.Any(child => child.ParentEntityId == entity.Id &&
                            playbackRows.Any(row => row.EntityId == child.Id &&
                                (row.CompletedAt != null || row.PlayCount > 0 || row.ResumeSeconds > 0)))) &&
                    !progressRows.Any(row => row.EntityId == entity.Id &&
                        (row.CompletedAt != null || row.Index > 0)));
        }

        var bookTypes = ParseCodeList<BookType>(bookType);
        if (bookTypes.Count > 0) {
            query = query.Where(entity =>
                _db.BookDetails.Any(detail => detail.EntityId == entity.Id && bookTypes.Contains(detail.BookType)));
        }

        var bookFormats = ParseCodeList<BookFormat>(bookFormat);
        if (bookFormats.Count > 0) {
            query = query.Where(entity =>
                _db.BookDetails.Any(detail => detail.EntityId == entity.Id && bookFormats.Contains(detail.Format)));
        }

        if (organized is { } wantsOrganized) {
            query = wantsOrganized
                ? query.Where(entity => entity.IsOrganized)
                : query.Where(entity => !entity.IsOrganized);
        }

        if (unrated == true) {
            query = query.Where(entity => entity.RatingValue == null);
        }

        if (ratingMin is { } min) {
            query = query.Where(entity => entity.RatingValue != null && entity.RatingValue >= min);
        }

        if (ratingMax is { } max) {
            query = query.Where(entity => entity.RatingValue != null && entity.RatingValue <= max);
        }

        var normalizedStatus = status?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalizedStatus)) {
            return query;
        }

        var playback = _db.Set<EntityPlaybackRow>();
        var progress = _db.Set<EntityProgressRow>();
        var entityRows = _db.Entities;
        return normalizedStatus switch {
            "watched" or "read" or "completed" or "finished" =>
                query.Where(entity =>
                    playback.Any(row => row.EntityId == entity.Id && row.CompletedAt != null) ||
                    (entity.KindCode == EntityKindRegistry.Movie.Code &&
                        entityRows.Any(child => child.ParentEntityId == entity.Id &&
                            playback.Any(row => row.EntityId == child.Id && row.CompletedAt != null))) ||
                    progress.Any(row => row.EntityId == entity.Id && row.CompletedAt != null)),
            "unwatched" or "unread" or "unstarted" or "new" =>
                query.Where(entity =>
                    !playback.Any(row => row.EntityId == entity.Id &&
                        (row.CompletedAt != null || row.PlayCount > 0 || row.ResumeSeconds > 0)) &&
                    !(entity.KindCode == EntityKindRegistry.Movie.Code &&
                        entityRows.Any(child => child.ParentEntityId == entity.Id &&
                            playback.Any(row => row.EntityId == child.Id &&
                                (row.CompletedAt != null || row.PlayCount > 0 || row.ResumeSeconds > 0)))) &&
                    !progress.Any(row => row.EntityId == entity.Id &&
                        (row.CompletedAt != null || row.Index > 0))),
            "in-progress" or "inprogress" or "in_progress" or "reading" or "watching" =>
                query.Where(entity =>
                    playback.Any(row => row.EntityId == entity.Id &&
                        row.CompletedAt == null && row.ResumeSeconds > 0) ||
                    (entity.KindCode == EntityKindRegistry.Movie.Code &&
                        entityRows.Any(child => child.ParentEntityId == entity.Id &&
                            playback.Any(row => row.EntityId == child.Id &&
                                row.CompletedAt == null && row.ResumeSeconds > 0))) ||
                    progress.Any(row => row.EntityId == entity.Id &&
                        row.CompletedAt == null && row.Index > 0 && row.Index < row.Total)),
            _ => query,
        };
    }

    /// <summary>
    /// Orders the supplied identifiers by a deterministic, seed-mixed FNV-1a hash so
    /// the same seed always produces the same shuffle. The shuffle is stable across
    /// paged requests and across process restarts, and does not depend on any
    /// database-specific random function.
    /// </summary>
    private static Guid[] DeterministicShuffle(Guid[] ids, int seed) {
        var seedMix = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL + 0x9E3779B97F4A7C15UL);
        return ids
            .OrderBy(id => ShuffleKey(id, seedMix))
            .ThenBy(id => id)
            .ToArray();
    }

    private static ulong ShuffleKey(Guid id, ulong seedMix) {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        var hash = seedMix ^ 0xCBF29CE484222325UL;
        foreach (var value in bytes) {
            hash ^= value;
            hash *= 0x100000001B3UL;
        }

        return hash;
    }

    public async Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) {
        var enforceLibraryVisibility = await HasDisabledLibraryRootsAsync(cancellationToken);
        if ((enforceLibraryVisibility && !await IsEntityVisibleInEnabledLibraryAsync(id, cancellationToken)) ||
            hideNsfw && await IsEntityHiddenAsync(id, cancellationToken)) {
            return null;
        }

        var entity = await _repository.FindShallowAsync(id, cancellationToken);
        if (entity is null) {
            return null;
        }

        var projected = SanitizeLocalAssets(
            await EnrichAudioTrackAlbumCoverAsync(EntityCardProjector.ToCard(entity), hideNsfw, cancellationToken));
        var card = projected with {
            ChildrenByKind = await ProjectDirectChildGroupsAsync(id, hideNsfw, enforceLibraryVisibility, cancellationToken),
            Relationships = await ProjectRelationshipGroupsAsync(id, hideNsfw, enforceLibraryVisibility, cancellationToken)
        };
        return await EnrichBookProgressAsync(card, hideNsfw, cancellationToken);
    }

    private EntityCard SanitizeLocalAssets(EntityCard card) {
        if (_assets is null) {
            return card;
        }

        var capabilities = card.Capabilities
            .Select(SanitizeLocalAssetCapability)
            .ToArray();
        return card with { Capabilities = capabilities };
    }

    private EntityCapability SanitizeLocalAssetCapability(EntityCapability capability) {
        if (capability is not ImagesCapability images) {
            return capability;
        }

        var items = images.Items
            .Where(item => HasUsableAssetPath(item.Path))
            .ToArray();

        return images with {
            Items = items,
            ThumbnailUrl = UsableImageUrl(images.ThumbnailUrl, items),
            CoverUrl = UsableImageUrl(images.CoverUrl, items)
        };
    }

    private string? UsableImageUrl(string? current, IReadOnlyList<EntityImageAsset> items) =>
        !string.IsNullOrWhiteSpace(current) && HasUsableAssetPath(current)
            ? current
            : items.FirstOrDefault()?.Path;

    public async Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
        IReadOnlyList<Guid> ids,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var query = _db.Entities.AsNoTracking()
            .Where(entity => ids.Contains(entity.Id));
        var enforceLibraryVisibility = await HasDisabledLibraryRootsAsync(cancellationToken);
        if (enforceLibraryVisibility) {
            query = ApplyEnabledLibraryVisibility(query);
        }
        query = ApplyNsfwVisibility(query, hideNsfw);
        var rows = await query
            .ToArrayAsync(cancellationToken);
        var thumbnails = await ProjectThumbnailsAsync(rows, hideNsfw, enforceLibraryVisibility, cancellationToken);
        var byId = thumbnails.ToDictionary(item => item.Id);
        return new EntityThumbnailBatchResponse(ids.Where(byId.ContainsKey).Select(id => byId[id]).ToArray());
    }

    public async Task<IEntityCard?> GetDetailAsync(Guid id, string kind, bool hideNsfw, CancellationToken cancellationToken) {
        var enforceLibraryVisibility = await HasDisabledLibraryRootsAsync(cancellationToken);
        if ((enforceLibraryVisibility && !await IsEntityVisibleInEnabledLibraryAsync(id, cancellationToken)) ||
            hideNsfw && await IsEntityHiddenAsync(id, cancellationToken)) {
            return null;
        }

        var entity = await _repository.FindShallowAsync(id, cancellationToken);
        if (entity is null) {
            return null;
        }

        var projected = SanitizeLocalAssets(
            await EnrichAudioTrackAlbumCoverAsync(EntityCardProjector.ToCard(entity), hideNsfw, cancellationToken));
        var card = await EnrichBookProgressAsync(projected with {
            ChildrenByKind = await ProjectDirectChildGroupsAsync(id, hideNsfw, enforceLibraryVisibility, cancellationToken),
            Relationships = await ProjectRelationshipGroupsAsync(id, hideNsfw, enforceLibraryVisibility, cancellationToken)
        }, hideNsfw, cancellationToken);
        if (!string.Equals(EntityKindRegistry.ToCode(card.Kind), kind, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var creditMetadata = await ProjectCreditMetadataAsync(id, hideNsfw, cancellationToken);
        return _kindMappers.TryGetValue(entity.Kind, out var mapper)
            ? mapper.ProjectDetail(entity, card, creditMetadata)
            : card;
    }

    private IQueryable<EntityRow> ApplyNsfwVisibility(IQueryable<EntityRow> query, bool hideNsfw) =>
        hideNsfw
            ? query.Where(entity => !entity.IsNsfw)
            : query;

    private async Task<bool> HasDisabledLibraryRootsAsync(CancellationToken cancellationToken) =>
        await _db.LibraryRoots.AsNoTracking().AnyAsync(root => !root.Enabled, cancellationToken);

    private IQueryable<Guid> DisabledRootedEntityIds() {
        var disabledRootIds = _db.LibraryRoots
            .Where(root => !root.Enabled)
            .Select(root => root.Id);

        return _db.VideoDetails
            .Where(detail => detail.LibraryRootId != null && disabledRootIds.Contains(detail.LibraryRootId.Value))
            .Select(detail => detail.EntityId)
            .Concat(_db.GalleryDetails
                .Where(detail => detail.LibraryRootId != null && disabledRootIds.Contains(detail.LibraryRootId.Value))
                .Select(detail => detail.EntityId))
            .Concat(_db.BookDetails
                .Where(detail => detail.LibraryRootId != null && disabledRootIds.Contains(detail.LibraryRootId.Value))
                .Select(detail => detail.EntityId))
            .Concat(_db.MusicArtistDetails
                .Where(detail => detail.LibraryRootId != null && disabledRootIds.Contains(detail.LibraryRootId.Value))
                .Select(detail => detail.EntityId))
            .Concat(_db.AudioLibraryDetails
                .Where(detail => detail.LibraryRootId != null && disabledRootIds.Contains(detail.LibraryRootId.Value))
                .Select(detail => detail.EntityId));
    }

    private IQueryable<EntityRow> ApplyEnabledLibraryVisibility(IQueryable<EntityRow> query, string? knownKindCode = null) {
        var entities = _db.Entities;
        var disabledRootedEntityIds = DisabledRootedEntityIds();

        if (KnownKindHasDirectLibraryRoot(knownKindCode)) {
            return query.Where(entity => !disabledRootedEntityIds.Contains(entity.Id));
        }

        if (KnownKindInheritsLibraryRoot(knownKindCode)) {
            return ApplyInheritedEnabledLibraryVisibility(query, entities, disabledRootedEntityIds);
        }

        if (KindEquals(knownKindCode, EntityKindRegistry.Movie.Code)) {
            return query.Where(entity =>
                !disabledRootedEntityIds.Contains(entity.Id) &&
                (!entities.Any(child =>
                     child.ParentEntityId == entity.Id &&
                     child.KindCode == EntityKindRegistry.Video.Code) ||
                 entities.Any(child =>
                     child.ParentEntityId == entity.Id &&
                     child.KindCode == EntityKindRegistry.Video.Code &&
                     !disabledRootedEntityIds.Contains(child.Id))));
        }

        if (KindEquals(knownKindCode, EntityKindRegistry.VideoSeason.Code)) {
            return query.Where(entity =>
                !disabledRootedEntityIds.Contains(entity.Id) &&
                (!entities.Any(child =>
                     child.ParentEntityId == entity.Id &&
                     child.KindCode == EntityKindRegistry.Video.Code) ||
                 entities.Any(child =>
                     child.ParentEntityId == entity.Id &&
                     child.KindCode == EntityKindRegistry.Video.Code &&
                     !disabledRootedEntityIds.Contains(child.Id))));
        }

        if (KindEquals(knownKindCode, EntityKindRegistry.VideoSeries.Code)) {
            return query.Where(entity =>
                !disabledRootedEntityIds.Contains(entity.Id) &&
                (!entities.Any(candidate =>
                     candidate.KindCode == EntityKindRegistry.Video.Code &&
                     (candidate.ParentEntityId == entity.Id ||
                      entities.Any(parent => parent.Id == candidate.ParentEntityId && parent.ParentEntityId == entity.Id))) ||
                 entities.Any(candidate =>
                     candidate.KindCode == EntityKindRegistry.Video.Code &&
                     (candidate.ParentEntityId == entity.Id ||
                      entities.Any(parent => parent.Id == candidate.ParentEntityId && parent.ParentEntityId == entity.Id)) &&
                     !disabledRootedEntityIds.Contains(candidate.Id))));
        }

        return query.Where(entity =>
            !disabledRootedEntityIds.Contains(entity.Id) &&
            !entities.Any(parent =>
                parent.Id == entity.ParentEntityId &&
                disabledRootedEntityIds.Contains(parent.Id)) &&
            !entities.Any(parent =>
                parent.Id == entity.ParentEntityId &&
                entities.Any(grandparent =>
                    grandparent.Id == parent.ParentEntityId &&
                    disabledRootedEntityIds.Contains(grandparent.Id))) &&
            !entities.Any(parent =>
                parent.Id == entity.ParentEntityId &&
                entities.Any(grandparent =>
                    grandparent.Id == parent.ParentEntityId &&
                    entities.Any(rootParent =>
                        rootParent.Id == grandparent.ParentEntityId &&
                        disabledRootedEntityIds.Contains(rootParent.Id)))) &&
            (entity.KindCode != EntityKindRegistry.Movie.Code ||
                !entities.Any(child =>
                    child.ParentEntityId == entity.Id &&
                    child.KindCode == EntityKindRegistry.Video.Code) ||
                entities.Any(child =>
                    child.ParentEntityId == entity.Id &&
                    child.KindCode == EntityKindRegistry.Video.Code &&
                    !disabledRootedEntityIds.Contains(child.Id))) &&
            (entity.KindCode != EntityKindRegistry.VideoSeason.Code ||
                !entities.Any(child =>
                    child.ParentEntityId == entity.Id &&
                    child.KindCode == EntityKindRegistry.Video.Code) ||
                entities.Any(child =>
                    child.ParentEntityId == entity.Id &&
                    child.KindCode == EntityKindRegistry.Video.Code &&
                    !disabledRootedEntityIds.Contains(child.Id))) &&
            (entity.KindCode != EntityKindRegistry.VideoSeries.Code ||
                !entities.Any(candidate =>
                    candidate.KindCode == EntityKindRegistry.Video.Code &&
                    (candidate.ParentEntityId == entity.Id ||
                     entities.Any(parent => parent.Id == candidate.ParentEntityId && parent.ParentEntityId == entity.Id))) ||
                entities.Any(candidate =>
                    candidate.KindCode == EntityKindRegistry.Video.Code &&
                    (candidate.ParentEntityId == entity.Id ||
                     entities.Any(parent => parent.Id == candidate.ParentEntityId && parent.ParentEntityId == entity.Id)) &&
                    !disabledRootedEntityIds.Contains(candidate.Id))));
    }

    private static bool KnownKindHasDirectLibraryRoot(string? kind) =>
        kind is not null && (
            kind.Equals(EntityKindRegistry.Video.Code, StringComparison.OrdinalIgnoreCase) ||
            kind.Equals(EntityKindRegistry.Gallery.Code, StringComparison.OrdinalIgnoreCase) ||
            kind.Equals(EntityKindRegistry.Book.Code, StringComparison.OrdinalIgnoreCase) ||
            kind.Equals(EntityKindRegistry.MusicArtist.Code, StringComparison.OrdinalIgnoreCase) ||
            kind.Equals(EntityKindRegistry.AudioLibrary.Code, StringComparison.OrdinalIgnoreCase));

    private static bool KnownKindInheritsLibraryRoot(string? kind) =>
        kind is not null && (
            kind.Equals(EntityKindRegistry.Image.Code, StringComparison.OrdinalIgnoreCase) ||
            kind.Equals(EntityKindRegistry.BookChapter.Code, StringComparison.OrdinalIgnoreCase) ||
            kind.Equals(EntityKindRegistry.AudioTrack.Code, StringComparison.OrdinalIgnoreCase));

    private static bool KindEquals(string? actual, string expected) =>
        actual is not null && actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static IQueryable<EntityRow> ApplyInheritedEnabledLibraryVisibility(
        IQueryable<EntityRow> query,
        IQueryable<EntityRow> entities,
        IQueryable<Guid> disabledRootedEntityIds) =>
        query.Where(entity =>
            !disabledRootedEntityIds.Contains(entity.Id) &&
            !entities.Any(parent =>
                parent.Id == entity.ParentEntityId &&
                disabledRootedEntityIds.Contains(parent.Id)) &&
            !entities.Any(parent =>
                parent.Id == entity.ParentEntityId &&
                entities.Any(grandparent =>
                    grandparent.Id == parent.ParentEntityId &&
                    disabledRootedEntityIds.Contains(grandparent.Id))) &&
            !entities.Any(parent =>
                parent.Id == entity.ParentEntityId &&
                entities.Any(grandparent =>
                    grandparent.Id == parent.ParentEntityId &&
                    entities.Any(rootParent =>
                        rootParent.Id == grandparent.ParentEntityId &&
                        disabledRootedEntityIds.Contains(rootParent.Id)))));

    // Audio libraries (albums) are intentionally excluded: an album's only parent is now its
    // artist grouping (a different kind), so every album is a browsable top-level item in the
    // Audio view — filtering to ParentEntityId == null would hide every album that has an artist.
    private static bool ListBrowseShowsOnlyTopLevel(string kind) =>
        kind.Equals(EntityKindRegistry.Gallery.Code, StringComparison.OrdinalIgnoreCase) ||
        kind.Equals(EntityKindRegistry.Book.Code, StringComparison.OrdinalIgnoreCase);

    private static bool ShouldSuppressMovieChildVideos(string? kind, string? query, Guid? referencedBy) =>
        kind is null ||
        !string.IsNullOrWhiteSpace(query) ||
        referencedBy is not null;

    private IQueryable<EntityRow> SuppressMovieChildVideos(IQueryable<EntityRow> query) =>
        query.Where(entity =>
            entity.KindCode != EntityKindRegistry.Video.Code ||
            entity.ParentEntityId == null ||
            !_db.Entities.Any(parent =>
                parent.Id == entity.ParentEntityId &&
                parent.KindCode == EntityKindRegistry.Movie.Code));

    private async Task<bool> IsEntityHiddenAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.Entities.AsNoTracking()
            .AnyAsync(entity => entity.Id == id && entity.IsNsfw, cancellationToken);

    private async Task<bool> IsEntityVisibleInEnabledLibraryAsync(Guid id, CancellationToken cancellationToken) =>
        await ApplyEnabledLibraryVisibility(_db.Entities.AsNoTracking())
            .AnyAsync(entity => entity.Id == id, cancellationToken);

    private async Task<EntityCard> EnrichBookProgressAsync(
        EntityCard card,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (card.Kind != EntityKind.Book) {
            return card;
        }

        var progress = card.Capabilities.OfType<ProgressCapability>().FirstOrDefault();
        if (progress?.CurrentEntityId is not { } currentEntityId) {
            return card;
        }

        if (hideNsfw && await IsEntityHiddenAsync(currentEntityId, cancellationToken)) {
            return card with {
                Capabilities = card.Capabilities
                    .Where(capability => capability is not ProgressCapability)
                    .ToArray()
            };
        }

        var position = await _repository.ResolveBookProgressPositionAsync(
            card.Id,
            currentEntityId,
            progress.Index,
            progress.Total,
            cancellationToken);
        if (position is null) {
            return card;
        }

        return card with {
            Capabilities = card.Capabilities.Select(capability =>
                capability is ProgressCapability progressCapability
                    ? progressCapability with {
                        WorkIndex = position.Index,
                        WorkTotal = position.Total,
                    }
                    : capability).ToArray()
        };
    }

    private async Task<EntityCard> EnrichAudioTrackAlbumCoverAsync(
        EntityCard card,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (card.Kind != EntityKind.AudioTrack ||
            card.ParentEntityId is not { } albumId) {
            return card;
        }

        var trackCovers = await LoadCoverPathsAsync([card.Id], cancellationToken);
        if (trackCovers.ContainsKey(card.Id)) {
            return card;
        }

        var albumExists = await _db.Entities.AsNoTracking()
            .AnyAsync(entity =>
                entity.Id == albumId &&
                entity.KindCode == EntityKindRegistry.AudioLibrary.Code &&
                (!hideNsfw || !entity.IsNsfw),
                cancellationToken);
        if (!albumExists) {
            return card;
        }

        var albumCovers = await LoadCoverPathsAsync([albumId], cancellationToken);
        if (!albumCovers.TryGetValue(albumId, out var albumCover)) {
            return card;
        }

        return card with {
            Capabilities = WithImageCoverFallback(card.Capabilities, albumCover)
        };
    }

    private static IReadOnlyList<EntityCapability> WithImageCoverFallback(
        IReadOnlyList<EntityCapability> capabilities,
        string coverUrl) {
        var result = capabilities.ToArray();
        var index = Array.FindIndex(result, capability => capability is ImagesCapability);
        if (index >= 0) {
            var images = (ImagesCapability)result[index];
            result[index] = images with {
                ThumbnailUrl = images.ThumbnailUrl ?? coverUrl,
                CoverUrl = images.CoverUrl ?? coverUrl
            };
            return result;
        }

        return result
            .Append(new ImagesCapability(
                [
                    EntityFileRole.Thumbnail.ToCode(),
                    EntityFileRole.Poster.ToCode(),
                    EntityFileRole.Backdrop.ToCode(),
                    EntityFileRole.Cover.ToCode(),
                    EntityFileRole.Logo.ToCode()
                ],
                [],
                coverUrl,
                coverUrl))
            .ToArray();
    }

    private async Task<IReadOnlyList<EntityGroup>> ProjectDirectChildGroupsAsync(
        Guid entityId,
        bool hideNsfw,
        bool enforceLibraryVisibility,
        CancellationToken cancellationToken) {
        var query = _db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId == entityId);
        if (enforceLibraryVisibility) {
            query = ApplyEnabledLibraryVisibility(query);
        }
        query = ApplyNsfwVisibility(query, hideNsfw);
        var childRows = await query
            .OrderBy(row => row.KindCode)
            .ThenBy(row => row.SortOrder)
            .ThenBy(row => row.Title)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        if (childRows.Length == 0) {
            return [];
        }

        var groups = new List<EntityGroup>();
        foreach (var group in childRows.GroupBy(row => row.KindCode)) {
            if (!group.Key.TryDecodeAs<EntityKind>(out var childKind)) {
                continue;
            }

            groups.Add(new EntityGroup(
                childKind,
                EntityKindRegistry.Describe(childKind).GroupLabel,
                await ProjectThumbnailsAsync(group.ToArray(), hideNsfw, enforceLibraryVisibility, cancellationToken)));
        }

        return groups;
    }

    private async Task<IReadOnlyList<EntityGroup>> ProjectRelationshipGroupsAsync(
        Guid entityId,
        bool hideNsfw,
        bool enforceLibraryVisibility,
        CancellationToken cancellationToken) {
        var links = await _db.EntityRelationshipLinks.AsNoTracking()
            .Where(link => link.EntityId == entityId)
            .OrderBy(link => link.RelationshipCode)
            .ThenBy(link => link.SortOrder)
            .ThenBy(link => link.TargetEntityId)
            .ToArrayAsync(cancellationToken);
        if (links.Length == 0) {
            return [];
        }

        var targetIds = links.Select(link => link.TargetEntityId).Distinct().ToArray();
        var targetQuery = _db.Entities.AsNoTracking()
            .Where(entity => targetIds.Contains(entity.Id));
        if (enforceLibraryVisibility) {
            targetQuery = ApplyEnabledLibraryVisibility(targetQuery);
        }
        targetQuery = ApplyNsfwVisibility(targetQuery, hideNsfw);
        var targetRows = await targetQuery
            .ToDictionaryAsync(entity => entity.Id, cancellationToken);

        var groups = new List<EntityGroup>();
        foreach (var group in links.GroupBy(link => new { link.RelationshipCode, link.TargetKindCode })) {
            var orderedRows = group
                .Select(link => targetRows.GetValueOrDefault(link.TargetEntityId))
                .Where(row => row is not null)
                .Select(row => row!)
                .ToArray();
            if (orderedRows.Length == 0) {
                continue;
            }

            groups.Add(new EntityGroup(
                group.Key.TargetKindCode.DecodeAs<EntityKind>(),
                RelationshipLabel(group.Key.RelationshipCode),
                await ProjectThumbnailsAsync(orderedRows, hideNsfw, enforceLibraryVisibility, cancellationToken)) {
                Code = group.Key.RelationshipCode is { Length: > 0 } relationshipCode
                    && relationshipCode.TryDecodeAs<RelationshipKind>(out var relationshipKind)
                        ? relationshipKind
                        : null
            });
        }

        return groups;
    }

    private async Task<IReadOnlyList<EntityCreditMetadata>> ProjectCreditMetadataAsync(
        Guid entityId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var castCode = RelationshipKind.Cast.ToCode();
        var creditsCode = RelationshipKind.Credits.ToCode();
        var linksQuery = _db.EntityRelationshipLinks.AsNoTracking()
            .Where(link => link.EntityId == entityId &&
                           (link.RelationshipCode == castCode || link.RelationshipCode == creditsCode) &&
                           link.TargetKindCode == EntityKindRegistry.Person.Code);
        if (hideNsfw) {
            linksQuery = linksQuery.Where(link =>
                !_db.Entities.Any(entity => entity.Id == link.TargetEntityId && entity.IsNsfw));
        }

        var links = await linksQuery
            .OrderBy(link => link.SortOrder)
            .ThenBy(link => link.TargetEntityId)
            .ToArrayAsync(cancellationToken);

        return links
            .Select(link => {
                var metadata = DecodeCreditMetadata(link.MetadataJson);
                return new EntityCreditMetadata(
                    link.TargetEntityId,
                    metadata.Role,
                    metadata.Character,
                    metadata.Roles,
                    metadata.Characters);
            })
            .ToArray();
    }

    private static (string? Role, string? Character, IReadOnlyList<string> Roles, IReadOnlyList<string> Characters) DecodeCreditMetadata(string? metadataJson) {
        if (string.IsNullOrWhiteSpace(metadataJson)) {
            return (null, null, [], []);
        }

        try {
            using var document = JsonDocument.Parse(metadataJson);
            var root = document.RootElement;
            var role = TryGetString(root, "role");
            var character = TryGetString(root, "character");
            return (
                role,
                character,
                WithPrimaryFirst(TryGetStringArray(root, "roles"), role),
                WithPrimaryFirst(TryGetStringArray(root, "characters"), character));
        } catch (JsonException) {
            return (null, null, [], []);
        }
    }

    /// <summary>
    /// Normalizes a stored distinct-value list so the primary value (when known) is always the
    /// first element, giving editors a stable list they can round-trip losslessly.
    /// </summary>
    private static IReadOnlyList<string> WithPrimaryFirst(IReadOnlyList<string> values, string? primary) {
        if (string.IsNullOrWhiteSpace(primary)) {
            return values;
        }

        if (values.Count > 0 && string.Equals(values[0], primary, StringComparison.OrdinalIgnoreCase)) {
            return values;
        }

        return [primary, .. values.Where(value => !string.Equals(value, primary, StringComparison.OrdinalIgnoreCase))];
    }

    private static string? TryGetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static IReadOnlyList<string> TryGetStringArray(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray()
            : [];

    private static string RelationshipLabel(string code) =>
        code.TryDecodeAs<RelationshipKind>(out var kind)
            ? kind switch {
                RelationshipKind.Cast => "Cast",
                RelationshipKind.Credits => "Credits",
                RelationshipKind.Studio => "Studios",
                RelationshipKind.Tags => "Tags",
                RelationshipKind.Related => "Related",
                _ => code.Replace('-', ' ')
            }
            : code.Replace('-', ' ');

    private static string EncodeOffsetCursor(int offset) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"offset:{offset}"));

    private static int DecodeOffsetCursor(string? cursor) {
        if (string.IsNullOrWhiteSpace(cursor)) {
            return 0;
        }

        try {
            var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            const string prefix = "offset:";
            if (text.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(text.AsSpan(prefix.Length), out var offset) &&
                offset >= 0) {
                return offset;
            }
        } catch (FormatException) {
            // Fall through to the start of the result set on an unparseable cursor.
        }

        return 0;
    }
}
