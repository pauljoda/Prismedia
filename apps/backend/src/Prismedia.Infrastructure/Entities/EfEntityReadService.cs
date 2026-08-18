using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// EF Core adapter for <see cref="IEntityReadService"/>. Card and detail reads flow
/// through the hydrated domain entity and <see cref="EntityCardProjector"/>; the
/// browse and thumbnail path stays a deliberate row-optimized projection so list
/// pages do not pay the full hydration cost. Kind-specific construction is delegated
/// to discovered entity mappers, while immutable kind-specific document capabilities
/// are projected by the Entity's definition. This service stays the coordinator for
/// visibility, bounded related-Entity queries, and final document enrichment.
/// </summary>
public sealed partial class EfEntityReadService : IEntityReadService {
    private const int DefaultPageSize = 250;
    private const int MaxPageSize = 1000;
    private const int DetailChildPageSize = 250;
    private const int MaxHoverImages = 5;
    private const int MaxHoverImageSearchDepth = 3;
    private const int MaxThumbnailMeta = 5;
    private static readonly string[] DefaultWantedExcludedKindCodes = EntityKindRegistry.All
        .Where(definition => definition.Browse.ExcludesWantedByDefault)
        .Select(definition => definition.Code)
        .ToArray();

    private readonly PrismediaDbContext _db;
    private readonly Prismedia.Application.Security.ICurrentUserContext _currentUser;
    private readonly EfEntityRepository _repository;
    private readonly IEntityProgressTopologyResolver _progressTopology;
    private readonly IReadOnlyList<Thumbnails.IThumbnailContributor> _thumbnailContributors;
    private readonly IEntitySourceOwnershipReader _sourceOwnership;
    private readonly IEntityFileDeletionRecoveryReader _deletionRecovery;
    private readonly EfEntitySourceOwnershipProjection _sourceOwnershipFilter;
    private readonly EfEntityAcquisitionStatusProjection _acquisitionStatuses;
    private readonly EfEntityLibraryVisibilityFilter _libraryVisibility;
    private readonly AssetPathService? _assets;

    public EfEntityReadService(
        PrismediaDbContext db,
        Prismedia.Application.Security.ICurrentUserContext currentUser,
        EfEntityRepository repository,
        IEnumerable<Thumbnails.IThumbnailContributor> thumbnailContributors,
        IEntityProgressTopologyResolver progressTopology,
        AssetPathService? assets = null,
        IEntitySourceOwnershipReader? sourceOwnership = null,
        IEntityFileDeletionRecoveryReader? deletionRecovery = null,
        EfEntityLibraryVisibilityFilter? libraryVisibility = null) {
        _db = db;
        _currentUser = currentUser;
        _repository = repository;
        _progressTopology = progressTopology;
        _thumbnailContributors = thumbnailContributors.ToArray();
        _sourceOwnershipFilter = sourceOwnership as EfEntitySourceOwnershipProjection
            ?? new EfEntitySourceOwnershipProjection(db);
        _sourceOwnership = sourceOwnership ?? _sourceOwnershipFilter;
        _deletionRecovery = deletionRecovery ?? new EfEntityFileDeletionRecoveryProjection(db);
        _acquisitionStatuses = new EfEntityAcquisitionStatusProjection(db);
        _libraryVisibility = libraryVisibility ?? new EfEntityLibraryVisibilityFilter(db, currentUser);
        _assets = assets;
    }

    private Guid CurrentUserId => _currentUser.UserId;

    public async Task<EntityListResponse> ListAsync(
        string? kind,
        string? query,
        string? cursor,
        bool? hideNsfw,
        int? limit,
        CancellationToken cancellationToken,
        Guid? referencedBy = null,
        string? relationshipCode = null,
        EntityListSort? sort = null,
        EntitySortDirection? sortDirection = null,
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
        bool? engaged = null,
        bool? orphaned = null,
        bool? wanted = null,
        AcquisitionStatus? acquisitionStatus = null) {
        var page = await ListPageAsync(
            new EntityListQuery {
                Kind = kind,
                Query = query,
                Cursor = cursor,
                HideNsfw = hideNsfw,
                Limit = limit,
                ReferencedBy = referencedBy,
                RelationshipCode = relationshipCode,
                Sort = sort,
                SortDirection = sortDirection,
                Seed = seed,
                Favorite = favorite,
                Organized = organized,
                RatingMin = ratingMin,
                RatingMax = ratingMax,
                Unrated = unrated,
                Status = status,
                BookType = bookType,
                BookFormat = bookFormat,
                Nsfw = nsfw,
                HasFile = hasFile,
                Engaged = engaged,
                Orphaned = orphaned,
                Wanted = wanted,
                AcquisitionStatus = acquisitionStatus,
            },
            includeTotalCount: true,
            ThumbnailProjectionMode.Full,
            cancellationToken);
        return new EntityListResponse(
            page.Items,
            page.NextCursor,
            page.TotalCount ?? throw new InvalidOperationException("A browse list must include its total count."));
    }

    /// <inheritdoc />
    public async Task<EntityShelfResponse> ListShelfAsync(
        EntityListQuery request,
        CancellationToken cancellationToken) {
        var page = await ListPageAsync(
            request,
            includeTotalCount: false,
            ThumbnailProjectionMode.Compact,
            cancellationToken);
        return new EntityShelfResponse(page.Items, page.NextCursor);
    }

    private async Task<EntityListPage> ListPageAsync(
        EntityListQuery request,
        bool includeTotalCount,
        ThumbnailProjectionMode projectionMode,
        CancellationToken cancellationToken) {
        var kind = request.Kind;
        var query = request.Query;
        var cursor = request.Cursor;
        var hideNsfw = request.HideNsfw;
        var limit = request.Limit;
        var referencedBy = request.ReferencedBy;
        var relationshipCode = request.RelationshipCode;
        var sort = request.Sort;
        var sortDirection = request.SortDirection;
        var seed = request.Seed;
        var favorite = request.Favorite;
        var organized = request.Organized;
        var ratingMin = request.RatingMin;
        var ratingMax = request.RatingMax;
        var unrated = request.Unrated;
        var status = request.Status;
        var bookType = request.BookType;
        var bookFormat = request.BookFormat;
        var nsfw = request.Nsfw;
        var hasFile = request.HasFile;
        var engaged = request.Engaged;
        var orphaned = request.Orphaned;
        var wanted = request.Wanted;
        var acquisitionStatus = request.AcquisitionStatus;
        var pageSize = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var sortKey = sort ?? EntityListSort.Title;
        var activityShelfStatus = projectionMode == ThumbnailProjectionMode.Compact &&
            sortKey == EntityListSort.LastActive
                ? ParseActivityShelfStatus(status)
                : null;
        var kindCodes = ParseKindCodes(kind);
        var normalizedRelationshipCode = string.IsNullOrWhiteSpace(relationshipCode)
            ? null
            : relationshipCode.Trim();
        var allEntities = _db.Entities.AsNoTracking();
        var entityQuery = allEntities;

        if (kindCodes.Length > 0) {
            entityQuery = entityQuery.Where(entity => kindCodes.Contains(entity.KindCode));
            entityQuery = EntityCatalogQueryPolicy.Apply(
                entityQuery,
                allEntities,
                EntityCatalogSurface.KindBrowse,
                kindCodes);
        } else {
            entityQuery = EntityCatalogQueryPolicy.Apply(
                entityQuery,
                allEntities,
                EntityCatalogSurface.Discovery);
        }

        entityQuery = ApplyCollectionVisibility(entityQuery);

        // Some wanted kinds are acquisition placeholders rather than library entries. Their
        // definitions keep them available to explicit wanted queries and parent-detail children
        // while excluding them from ordinary browse/search results.
        if (wanted is null) {
            entityQuery = entityQuery.Where(entity =>
                !DefaultWantedExcludedKindCodes.Contains(entity.KindCode) || !entity.IsWanted);
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

        var enforceLibraryVisibility = await RequiresLibraryVisibilityAsync(cancellationToken);
        if (enforceLibraryVisibility) {
            var knownKindCode = kindCodes.Length == 1 ? kindCodes[0] : null;
            entityQuery = ApplyEnabledLibraryVisibility(entityQuery, knownKindCode);
        }
        entityQuery = ApplyNsfwVisibility(entityQuery, hideNsfw == true);
        entityQuery = ApplyListFilters(
            entityQuery,
            favorite,
            organized,
            ratingMin,
            ratingMax,
            unrated,
            activityShelfStatus is null ? status : null,
            bookType,
            bookFormat,
            nsfw,
            engaged,
            orphaned,
            wanted);
        entityQuery = await _acquisitionStatuses.ApplyFilterAsync(entityQuery, acquisitionStatus, cancellationToken);
        entityQuery = await _sourceOwnershipFilter.ApplyFilterAsync(entityQuery, hasFile, cancellationToken);

        // Snapshot the unbounded filtered total before applying the cursor; this is what
        // drives the client's page-of-pages and seek-to-end behaviour and must stay
        // independent of where in the cursor sequence we currently are.
        var totalCount = includeTotalCount
            ? await entityQuery.CountAsync(cancellationToken)
            : (int?)null;

        var offset = DecodeOffsetCursor(cursor);
        var descending = sortDirection == EntitySortDirection.Descending;

        EntityRow[] rows;
        if (sortKey == EntityListSort.Random) {
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
        } else if (sortKey == EntityListSort.LastActive) {
            var ordered = activityShelfStatus is { } shelfStatus
                ? ApplyActivityShelfOrdering(entityQuery, shelfStatus, descending)
                : ApplyLastActiveOrdering(entityQuery, descending);
            rows = await ordered
                .Skip(offset)
                .Take(pageSize + 1)
                .ToArrayAsync(cancellationToken);
        } else if (sortKey == EntityListSort.References) {
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
        var thumbnails = await ProjectThumbnailsAsync(
            page,
            hideNsfw == true,
            enforceLibraryVisibility,
            cancellationToken,
            projectionMode: projectionMode);
        var nextCursor = rows.Length > pageSize ? EncodeOffsetCursor(offset + pageSize) : null;
        return new EntityListPage(thumbnails, nextCursor, totalCount);
    }

    private sealed record EntityListPage(
        IReadOnlyList<EntityThumbnail> Items,
        string? NextCursor,
        int? TotalCount);

    private enum ActivityShelfStatus {
        Completed,
        InProgress,
    }

    private static ActivityShelfStatus? ParseActivityShelfStatus(string? status) =>
        status?.Trim().ToLowerInvariant() switch {
            "watched" or "read" or "completed" or "finished" => ActivityShelfStatus.Completed,
            "in-progress" or "inprogress" or "in_progress" or "reading" or "watching" =>
                ActivityShelfStatus.InProgress,
            _ => null,
        };

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

    private static string[] ParseKindCodes(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    /// <summary>
    /// Applies a deterministic ORDER BY for the non-random sorts. Each strategy ends
    /// with a stable identifier tiebreaker so offset paging never skips or repeats a
    /// row, and rating always pushes unrated entities to the end regardless of
    /// direction. Ratings are the current user's opinion, resolved per row from the
    /// user-state table.
    /// </summary>
    private IQueryable<EntityRow> ApplyOrdering(IQueryable<EntityRow> query, EntityListSort sort, bool descending) {
        if (sort == EntityListSort.Rating) {
            var states = _db.UserEntityStates;
            var userId = CurrentUserId;
            var keyed = query.Select(entity => new {
                entity,
                rating = states
                    .Where(state => state.UserId == userId && state.EntityId == entity.Id)
                    .Select(state => state.RatingValue)
                    .FirstOrDefault()
            });
            var ordered = descending
                ? keyed.OrderBy(item => item.rating == null)
                    .ThenByDescending(item => item.rating)
                    .ThenBy(item => item.entity.SortName)
                    .ThenBy(item => item.entity.Id)
                : keyed.OrderBy(item => item.rating == null)
                    .ThenBy(item => item.rating)
                    .ThenBy(item => item.entity.SortName)
                    .ThenBy(item => item.entity.Id);
            return ordered.Select(item => item.entity);
        }

        return sort switch {
            EntityListSort.DateAdded => descending
                ? query.OrderByDescending(entity => entity.CreatedAt).ThenByDescending(entity => entity.Id)
                : query.OrderBy(entity => entity.CreatedAt).ThenBy(entity => entity.Id),
            _ => descending
                ? query.OrderByDescending(entity => entity.SortName).ThenByDescending(entity => entity.Id)
                : query.OrderBy(entity => entity.SortName).ThenBy(entity => entity.Id),
        };
    }

    /// <summary>
    /// Orders entities by the current user's most recent consumption or progress signal.
    /// Entities with no engagement sort last regardless of direction, so the "recently
    /// recently active surfaces only lead with things the user has actually touched.
    /// </summary>
    private IQueryable<EntityRow> ApplyLastActiveOrdering(IQueryable<EntityRow> query, bool descending) {
        var states = _db.UserEntityStates;
        var userId = CurrentUserId;
        var keyed =
            from entity in query
            join state in states.Where(state => state.UserId == userId)
                on entity.Id equals state.EntityId into stateRows
            from state in stateRows.DefaultIfEmpty()
            select new {
                entity,
                recency = state == null
                    ? null
                    : state.LastActiveAt ??
                        (state.ProgressCurrentEntityId != null || state.ProgressIndex > 0 || state.ProgressCompletedAt != null
                            ? state.ProgressUpdatedAt ?? state.UpdatedAt
                            : null)
            };

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
    /// Drives compact activity shelves from the current user's small state set. The ordinary browse
    /// pipeline applies the status predicate and recency order separately, which joins the same wide
    /// state row twice. A shelf has a recognized activity status and never needs untouched entities,
    /// so one filtered inner join can both select and order the candidates.
    /// </summary>
    private IQueryable<EntityRow> ApplyActivityShelfOrdering(
        IQueryable<EntityRow> query,
        ActivityShelfStatus status,
        bool descending) {
        var userId = CurrentUserId;
        var states = _db.UserEntityStates.Where(state => state.UserId == userId);
        states = status == ActivityShelfStatus.Completed
            ? states.Where(state => state.CompletedAt != null || state.ProgressCompletedAt != null)
            : states.Where(state =>
                state.CompletedAt == null && state.ResumeSeconds > 0 ||
                state.ProgressCompletedAt == null &&
                (state.ProgressCurrentEntityId != null || state.ProgressIndex > 0) &&
                state.ProgressIndex < state.ProgressTotal);
        var keyed =
            from state in states
            join entity in query on state.EntityId equals entity.Id
            select new {
                entity,
                recency = state.LastActiveAt ??
                    (state.ProgressCurrentEntityId != null || state.ProgressIndex > 0 || state.ProgressCompletedAt != null
                        ? state.ProgressUpdatedAt ?? state.UpdatedAt
                        : (DateTimeOffset?)null)
            };
        var ordered = descending
            ? keyed.OrderByDescending(item => item.recency)
                .ThenByDescending(item => item.entity.CreatedAt)
                .ThenBy(item => item.entity.Id)
            : keyed.OrderBy(item => item.recency)
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
    /// case), and the adaptive engagement status. Favorite, rating, and engagement are
    /// the current user's state; status is resolved against both playback (videos/audio)
    /// and reading progress (books/comics) so a single control reads correctly for every
    /// kind that records engagement.
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
        bool? engaged = null,
        bool? orphaned = null,
        bool? wanted = null) {
        var userId = CurrentUserId;
        var states = _db.UserEntityStates;
        if (favorite == true) {
            query = query.Where(entity => states.Any(state =>
                state.UserId == userId && state.EntityId == entity.Id && state.IsFavorite));
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

        if (engaged is { } wantsEngaged) {
            var engagedStates = states.Where(state =>
                state.UserId == userId &&
                (state.CompletedAt != null || state.AccessCount > 0 || state.ResumeSeconds > 0 ||
                 state.ProgressCompletedAt != null || state.ProgressCurrentEntityId != null || state.ProgressIndex > 0));
            query = wantsEngaged
                ? query.Join(engagedStates, entity => entity.Id, state => state.EntityId, (entity, _) => entity)
                : query.Where(entity => !engagedStates.Any(state => state.EntityId == entity.Id));
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
            query = query.Where(entity => !states.Any(state =>
                state.UserId == userId && state.EntityId == entity.Id && state.RatingValue != null));
        }

        if (ratingMin is { } min) {
            query = query.Where(entity => states.Any(state =>
                state.UserId == userId && state.EntityId == entity.Id &&
                state.RatingValue != null && state.RatingValue >= min));
        }

        if (ratingMax is { } max) {
            query = query.Where(entity => states.Any(state =>
                state.UserId == userId && state.EntityId == entity.Id &&
                state.RatingValue != null && state.RatingValue <= max));
        }

        var normalizedStatus = status?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalizedStatus)) {
            return query;
        }

        return normalizedStatus switch {
            "watched" or "read" or "completed" or "finished" =>
                query.Join(
                    states.Where(state => state.UserId == userId &&
                        (state.CompletedAt != null || state.ProgressCompletedAt != null)),
                    entity => entity.Id,
                    state => state.EntityId,
                    (entity, _) => entity),
            "unwatched" or "unread" or "unstarted" or "new" =>
                query.Where(entity =>
                    !states.Any(state => state.UserId == userId && state.EntityId == entity.Id &&
                        (state.CompletedAt != null || state.AccessCount > 0 || state.ResumeSeconds > 0 ||
                         state.ProgressCompletedAt != null || state.ProgressCurrentEntityId != null || state.ProgressIndex > 0))),
            "in-progress" or "inprogress" or "in_progress" or "reading" or "watching" =>
                query.Join(
                    states.Where(state => state.UserId == userId &&
                        (state.CompletedAt == null && state.ResumeSeconds > 0 ||
                         state.ProgressCompletedAt == null &&
                         (state.ProgressCurrentEntityId != null || state.ProgressIndex > 0) &&
                         state.ProgressIndex < state.ProgressTotal)),
                    entity => entity.Id,
                    state => state.EntityId,
                    (entity, _) => entity),
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
        var enforceLibraryVisibility = await RequiresLibraryVisibilityAsync(cancellationToken);
        if (!await IsCollectionVisibleAsync(id, cancellationToken) ||
            (enforceLibraryVisibility && !await IsEntityVisibleInEnabledLibraryAsync(id, cancellationToken)) ||
            hideNsfw && await IsEntityHiddenAsync(id, cancellationToken)) {
            return null;
        }

        var entity = await _repository.FindShallowAsync(id, cancellationToken);
        if (entity is null) {
            return null;
        }

        var fileManagementState = await ResolveFileManagementStateAsync(id, cancellationToken);
        var creditMetadata = await ProjectCreditMetadataAsync(id, hideNsfw, cancellationToken);
        var projected = SanitizeLocalAssets(
            await EnrichBorrowedParentCoverAsync(
                EntityCardProjector.ToCard(entity, fileManagementState, CurrentUserId, creditMetadata),
                hideNsfw,
                enforceLibraryVisibility,
                cancellationToken));
        var detailGroups = await ProjectDetailGroupsAsync(
            id,
            hideNsfw,
            enforceLibraryVisibility,
            cancellationToken);
        var card = projected with {
            ChildrenByKind = detailGroups.Children,
            Relationships = detailGroups.Relationships
        };
        return await EnrichProgressAsync(card, hideNsfw, cancellationToken);
    }

    // Generated-asset rows are kept truthful off the request path: producers write the file
    // before the row, and the background asset-row sweep removes rows whose files vanished.
    // Detail reads therefore trust the rows instead of stat-ing every image per request.
    private static EntityCard SanitizeLocalAssets(EntityCard card) => card;

    public async Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
        IReadOnlyList<Guid> ids,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var query = _db.Entities.AsNoTracking()
            .Where(entity => ids.Contains(entity.Id));
        query = ApplyCollectionVisibility(query);
        var enforceLibraryVisibility = await RequiresLibraryVisibilityAsync(cancellationToken);
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

    /// <inheritdoc />
    public async Task<EntityHoverImagesResponse> GetHoverImagesAsync(
        IReadOnlyList<Guid> ids,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (ids.Count == 0) {
            return new EntityHoverImagesResponse([]);
        }

        var query = _db.Entities.AsNoTracking()
            .Where(entity => ids.Contains(entity.Id));
        query = ApplyCollectionVisibility(query);
        var enforceLibraryVisibility = await RequiresLibraryVisibilityAsync(cancellationToken);
        if (enforceLibraryVisibility) {
            query = ApplyEnabledLibraryVisibility(query);
        }
        query = ApplyNsfwVisibility(query, hideNsfw);
        var rows = await query.ToArrayAsync(cancellationToken);
        if (rows.Length == 0) {
            return new EntityHoverImagesResponse([]);
        }

        var collectionCode = EntityKind.Collection.ToCode();
        var collectionRows = rows.Where(row => row.KindCode == collectionCode).ToArray();
        var structuralRows = rows.Where(row => row.KindCode != collectionCode).ToArray();
        var byEntity = new Dictionary<Guid, IReadOnlyList<EntityThumbnailHoverImage>>();
        if (structuralRows.Length > 0) {
            foreach (var pair in await ProjectHoverImagesAsync(
                         structuralRows, hideNsfw, enforceLibraryVisibility, cancellationToken)) {
                byEntity[pair.Key] = pair.Value;
            }
        }

        if (collectionRows.Length > 0) {
            foreach (var pair in await ProjectCollectionArtworkAsync(
                         collectionRows, hideNsfw, enforceLibraryVisibility, cancellationToken)) {
                byEntity[pair.Key] = pair.Value.HoverImages;
            }
        }

        return new EntityHoverImagesResponse(ids
            .Where(id => byEntity.TryGetValue(id, out var images) && images.Count > 0)
            .Select(id => new EntityHoverImageSet(id, byEntity[id]))
            .ToArray());
    }

    private async Task<EntityFileManagementState> ResolveFileManagementStateAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var sourceBackedIds = await _sourceOwnership.ResolveAsync([entityId], cancellationToken);
        var recoverableDeletionIds = await _deletionRecovery.ResolveAsync([entityId], cancellationToken);
        return new EntityFileManagementState(
            sourceBackedIds.Contains(entityId),
            recoverableDeletionIds.Contains(entityId));
    }

    public async Task<IReadOnlyDictionary<Guid, EntityFolderListContext>> GetFolderListContextsAsync(
        IReadOnlyList<Guid> ids,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (ids.Count == 0) {
            return new Dictionary<Guid, EntityFolderListContext>();
        }

        var idSet = ids.Distinct().ToArray();

        // Direct visible children: wanted phantoms are excluded (external catalogs never see them),
        // and hidden-NSFW children don't count toward what the viewer can actually browse into.
        var childQuery = _db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId != null && idSet.Contains(row.ParentEntityId.Value) && !row.IsWanted);
        if (hideNsfw) {
            childQuery = childQuery.Where(row => !row.IsNsfw);
        }

        var childCounts = await childQuery
            .GroupBy(row => row.ParentEntityId!.Value)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Id, group => group.Count, cancellationToken);

        var descriptions = await _db.EntityDescriptions.AsNoTracking()
            .Where(row => idSet.Contains(row.EntityId))
            .ToDictionaryAsync(row => row.EntityId, row => row.Value, cancellationToken);

        var dates = (await _db.EntityDates.AsNoTracking()
                .Where(row => idSet.Contains(row.EntityId))
                .ToArrayAsync(cancellationToken))
            .GroupBy(row => row.EntityId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<EntityDate>)group
                    .Select(row => new EntityDate(row.Code, row.Value, row.SortableValue, row.Precision))
                    .ToArray());

        var lifetimes = (await _db.EntityLifetimes.AsNoTracking()
                .Where(row => idSet.Contains(row.EntityId))
                .ToArrayAsync(cancellationToken))
            .ToDictionary(row => row.EntityId);

        var externalIds = (await _db.EntityExternalIds.AsNoTracking()
                .Where(row => idSet.Contains(row.EntityId))
                .ToArrayAsync(cancellationToken))
            .GroupBy(row => row.EntityId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Contracts.Entities.EntityExternalId>)group
                    .Select(row => new Contracts.Entities.EntityExternalId(row.Provider, row.Value, row.Url))
                    .ToArray());

        var contexts = new Dictionary<Guid, EntityFolderListContext>(idSet.Length);
        foreach (var id in idSet) {
            var lifetime = lifetimes.GetValueOrDefault(id);
            contexts[id] = new EntityFolderListContext(
                childCounts.GetValueOrDefault(id),
                descriptions.GetValueOrDefault(id),
                dates.GetValueOrDefault(id, []),
                LifetimeStart: ToLifetimeDate(lifetime?.StartCode, lifetime?.StartValue, lifetime?.StartSortableValue, lifetime?.StartPrecision),
                LifetimeEnd: ToLifetimeDate(lifetime?.EndCode, lifetime?.EndValue, lifetime?.EndSortableValue, lifetime?.EndPrecision),
                externalIds.GetValueOrDefault(id, []));
        }

        return contexts;
    }

    /// <summary>Reconstructs a lifetime edge date from its flattened columns; null when no value was stored.</summary>
    private static EntityDate? ToLifetimeDate(string? code, string? value, DateOnly? sortableValue, string? precision) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : new EntityDate(code ?? string.Empty, value, sortableValue, precision);

    private IQueryable<EntityRow> ApplyNsfwVisibility(IQueryable<EntityRow> query, bool hideNsfw) =>
        hideNsfw
            ? query.Where(entity => !entity.IsNsfw)
            : query;

    private Task<bool> RequiresLibraryVisibilityAsync(CancellationToken cancellationToken) =>
        _libraryVisibility.RequiresCurrentUserVisibilityAsync(cancellationToken);

    private IQueryable<EntityRow> ApplyEnabledLibraryVisibility(
        IQueryable<EntityRow> query,
        string? knownKindCode = null) =>
        _libraryVisibility.ApplyCurrentUserVisibility(query, knownKindCode);

    /// <summary>
    /// Library-visibility check for mutation/streaming guards: true when the entity
    /// exists and no hidden-root rule (disabled or not granted to this user) hides it.
    /// </summary>
    internal async Task<bool> IsEntityVisibleToCurrentUserAsync(Guid id, CancellationToken cancellationToken) {
        if (!await RequiresLibraryVisibilityAsync(cancellationToken)) {
            return await _db.Entities.AsNoTracking().AnyAsync(entity => entity.Id == id, cancellationToken);
        }

        return await IsEntityVisibleInEnabledLibraryAsync(id, cancellationToken);
    }

    private async Task<bool> IsEntityHiddenAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.Entities.AsNoTracking()
            .AnyAsync(entity => entity.Id == id && entity.IsNsfw, cancellationToken);

    private async Task<bool> IsEntityVisibleInEnabledLibraryAsync(Guid id, CancellationToken cancellationToken) =>
        await ApplyEnabledLibraryVisibility(_db.Entities.AsNoTracking())
            .AnyAsync(entity => entity.Id == id, cancellationToken);

    private async Task<EntityCard> EnrichProgressAsync(
        EntityCard card,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var progress = card.Capabilities.OfType<ProgressCapability>().FirstOrDefault();
        if (progress?.CurrentEntityId is not { } currentEntityId) {
            return card;
        }

        var owner = await _progressTopology.ResolveOwnerAsync(card.Id, cancellationToken);
        if (owner is null || owner.OwnerId != card.Id) {
            return RemoveProgress(card);
        }

        var cursorVisible = !hideNsfw || !await IsEntityHiddenAsync(currentEntityId, cancellationToken);
        if (cursorVisible && await RequiresLibraryVisibilityAsync(cancellationToken)) {
            cursorVisible = await ApplyEnabledLibraryVisibility(_db.Entities.AsNoTracking())
                .AnyAsync(entity => entity.Id == currentEntityId, cancellationToken);
        }

        if (!cursorVisible) {
            return RemoveProgress(card);
        }

        var cursor = await _progressTopology.ResolveCursorAsync(card.Id, currentEntityId, cancellationToken);
        if (cursor is null) {
            return RemoveProgress(card);
        }

        var position = await _progressTopology.ResolveWorkPositionAsync(
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
                        ConsumedTotal = position.Total,
                        ConsumedPercent = position.Total > 0
                            ? Math.Clamp(progressCapability.ConsumedCount / (double)position.Total, 0, 1)
                            : 0,
                    }
                    : capability).ToArray()
        };
    }

    private static EntityCard RemoveProgress(EntityCard card) => card with {
        Capabilities = card.Capabilities.Where(capability => capability is not ProgressCapability).ToArray()
    };

    private async Task<EntityCard> EnrichBorrowedParentCoverAsync(
        EntityCard card,
        bool hideNsfw,
        bool enforceLibraryVisibility,
        CancellationToken cancellationToken) {
        if (card.ParentEntityId is not { } parentId) {
            return card;
        }

        var ownCovers = await LoadCoverPathsAsync([card.Id], cancellationToken);
        if (ownCovers.ContainsKey(card.Id)) {
            return card;
        }

        var parentQuery = _db.Entities.AsNoTracking()
            .Where(entity => entity.Id == parentId);
        if (enforceLibraryVisibility) {
            parentQuery = ApplyEnabledLibraryVisibility(parentQuery);
        }
        parentQuery = ApplyNsfwVisibility(parentQuery, hideNsfw);
        var parent = await parentQuery.SingleOrDefaultAsync(cancellationToken);
        if (parent is null || !CanBorrowParentCover(card.Kind.ToCode(), parent.KindCode)) {
            return card;
        }

        var parentCovers = await LoadCoverPathsAsync([parentId], cancellationToken);
        if (!parentCovers.TryGetValue(parentId, out var parentCover)) {
            return card;
        }

        return card with {
            Capabilities = WithImageCoverFallback(card.Capabilities, parentCover)
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
                    EntityFileRole.Thumbnail,
                    EntityFileRole.Poster,
                    EntityFileRole.Backdrop,
                    EntityFileRole.Cover,
                    EntityFileRole.Logo
                ],
                [],
                coverUrl,
                null,
                coverUrl))
            .ToArray();
    }

    /// <summary>
    /// Hydrates the structural children and relationship targets for one detail document through a
    /// single thumbnail page. Detail documents often contain several kinds in each section; sending
    /// each group through the thumbnail pipeline separately multiplied its batched contributor and
    /// asset queries by the number of groups.
    /// </summary>
    private async Task<(IReadOnlyList<EntityGroup> Children, IReadOnlyList<EntityGroup> Relationships)> ProjectDetailGroupsAsync(
        Guid entityId,
        bool hideNsfw,
        bool enforceLibraryVisibility,
        CancellationToken cancellationToken) {
        var childQuery = _db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId == entityId);
        if (enforceLibraryVisibility) {
            childQuery = ApplyEnabledLibraryVisibility(childQuery);
        }
        childQuery = ApplyNsfwVisibility(childQuery, hideNsfw);
        var childRows = await childQuery
            .OrderBy(row => row.KindCode)
            .ThenBy(row => row.SortOrder)
            .ThenBy(row => row.Title)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var links = await _db.EntityRelationshipLinks.AsNoTracking()
            .Where(link => link.EntityId == entityId)
            .OrderBy(link => link.RelationshipCode)
            .ThenBy(link => link.SortOrder)
            .ThenBy(link => link.TargetEntityId)
            .ToArrayAsync(cancellationToken);
        var targetIds = links.Select(link => link.TargetEntityId).Distinct().ToArray();
        var targetRows = new Dictionary<Guid, EntityRow>();
        if (targetIds.Length > 0) {
            var targetQuery = _db.Entities.AsNoTracking()
                .Where(entity => targetIds.Contains(entity.Id));
            if (enforceLibraryVisibility) {
                targetQuery = ApplyEnabledLibraryVisibility(targetQuery);
            }
            targetQuery = ApplyNsfwVisibility(targetQuery, hideNsfw);
            targetRows = await targetQuery.ToDictionaryAsync(entity => entity.Id, cancellationToken);
        }

        var relationshipRows = links
            .Select(link => targetRows.GetValueOrDefault(link.TargetEntityId))
            .Where(row => row is not null)
            .Select(row => row!);
        // Large containers project only the first page of each child kind through the thumbnail
        // pipeline; the group's TotalCount tells clients to load the remainder through the
        // children batch reads instead of the detail paying for every member up front.
        var pagedChildRows = childRows
            .GroupBy(row => row.KindCode)
            .SelectMany(group => group.Take(DetailChildPageSize))
            .ToArray();
        var childTotalsByKind = childRows
            .GroupBy(row => row.KindCode)
            .ToDictionary(group => group.Key, group => group.Count());
        var thumbnailRows = pagedChildRows
            .Concat(relationshipRows)
            .DistinctBy(row => row.Id)
            .ToArray();
        var thumbnailsById = thumbnailRows.Length == 0
            ? new Dictionary<Guid, EntityThumbnail>()
            : (await ProjectThumbnailsAsync(
                    thumbnailRows,
                    hideNsfw,
                    enforceLibraryVisibility,
                    cancellationToken))
                .ToDictionary(thumbnail => thumbnail.Id);

        var children = new List<EntityGroup>();
        foreach (var group in pagedChildRows.GroupBy(row => row.KindCode)) {
            if (!group.Key.TryDecodeAs<EntityKind>(out var childKind)) {
                continue;
            }

            var totalCount = childTotalsByKind.GetValueOrDefault(group.Key);
            var thumbnails = MapProjectedThumbnails(group, thumbnailsById);
            children.Add(new EntityGroup(
                childKind,
                EntityKindRegistry.Describe(childKind).GroupLabel,
                thumbnails) {
                TotalCount = totalCount > thumbnails.Count ? totalCount : null
            });
        }

        var relationships = new List<EntityGroup>();
        foreach (var group in links.GroupBy(link => new { link.RelationshipCode, link.TargetKindCode })) {
            var orderedRows = group
                .Select(link => targetRows.GetValueOrDefault(link.TargetEntityId))
                .Where(row => row is not null)
                .Select(row => row!)
                .ToArray();
            if (orderedRows.Length == 0) {
                continue;
            }

            relationships.Add(new EntityGroup(
                group.Key.TargetKindCode.DecodeAs<EntityKind>(),
                RelationshipLabel(group.Key.RelationshipCode),
                MapProjectedThumbnails(orderedRows, thumbnailsById)) {
                Code = group.Key.RelationshipCode is { Length: > 0 } relationshipCode
                    && relationshipCode.TryDecodeAs<RelationshipKind>(out var relationshipKind)
                        ? relationshipKind
                        : null
            });
        }

        return (children, relationships);
    }

    private static IReadOnlyList<EntityThumbnail> MapProjectedThumbnails(
        IEnumerable<EntityRow> rows,
        IReadOnlyDictionary<Guid, EntityThumbnail> thumbnailsById) =>
        rows.Select(row => thumbnailsById.GetValueOrDefault(row.Id))
            .Where(thumbnail => thumbnail is not null)
            .Select(thumbnail => thumbnail!)
            .ToArray();

    private async Task<IReadOnlyList<EntityCreditMetadata>> ProjectCreditMetadataAsync(
        Guid entityId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var castCode = RelationshipKind.Cast.ToCode();
        var creditsCode = RelationshipKind.Credits.ToCode();
        var linksQuery = _db.EntityRelationshipLinks.AsNoTracking()
            .Where(link => link.EntityId == entityId &&
                           (link.RelationshipCode == castCode || link.RelationshipCode == creditsCode) &&
                           link.TargetKindCode == EntityKind.Person.ToCode());
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
