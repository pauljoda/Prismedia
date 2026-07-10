using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Entities;

/// <summary>
/// Application port for entity read use cases. Infrastructure implements this with a
/// row-optimized browse/thumbnail projection plus a domain-hydration projection for
/// card and detail reads.
/// </summary>
public interface IEntityReadService {
    /// <summary>
    /// Lists active entities as thumbnail read models, optionally scoped by kind,
    /// search text, NSFW visibility, and cursor. Sorting, the seeded full-library
    /// shuffle, and the library filters are all applied server-side so that they
    /// span the entire matching set rather than a single loaded page.
    /// </summary>
    /// <param name="sort">
    /// Sort key: <c>title</c> (default), <c>added</c>/<c>date</c> (creation time),
    /// <c>rating</c>, or <c>random</c> for a seeded shuffle of the whole result set.
    /// </param>
    /// <param name="sortDir"><c>asc</c> (default) or <c>desc</c>. Ignored for <c>random</c>.</param>
    /// <param name="seed">
    /// Stable seed for the <c>random</c> sort. The same seed reproduces the same
    /// shuffle across every page so cursor paging stays consistent.
    /// </param>
    /// <param name="favorite">When true, only entities flagged as favorites.</param>
    /// <param name="organized">When set, filters to organized (true) or unorganized (false) entities.</param>
    /// <param name="ratingMin">Inclusive minimum rating (excludes unrated).</param>
    /// <param name="ratingMax">Inclusive maximum rating (excludes unrated).</param>
    /// <param name="unrated">When true, only entities with no rating.</param>
    /// <param name="status">
    /// Engagement status filter that adapts to the entity: <c>watched</c>/<c>read</c>
    /// (completed), <c>unwatched</c>/<c>unread</c> (no engagement), or <c>in-progress</c>.
    /// </param>
    /// <param name="bookType">
    /// Comma-separated book type codes (<c>book</c>, <c>comic</c>, <c>manga</c>, <c>novel</c>).
    /// Books matching any listed type are kept; unrecognized codes are ignored.
    /// </param>
    /// <param name="bookFormat">
    /// Comma-separated book format codes (<c>image-archive</c>, <c>epub</c>, <c>pdf</c>).
    /// Books matching any listed format are kept; unrecognized codes are ignored.
    /// </param>
    /// <param name="nsfw">
    /// When set, keeps only NSFW (true) or only non-NSFW (false) entities. Independent of
    /// <paramref name="hideNsfw"/>, which enforces the viewer's privacy setting; this is the
    /// explicit "Is NSFW" / "Not NSFW" library filter and only ever narrows what privacy allows.
    /// </param>
    /// <param name="hasFile">
    /// When set, keeps only entities whose structural subtree has (true) or lacks (false) a source file.
    /// </param>
    /// <param name="played">
    /// When set, keeps only entities that have been played/read (true) or never engaged (false),
    /// resolved against the playback (videos/audio) and progress (books/comics) records.
    /// </param>
    /// <param name="orphaned">
    /// When true, keeps only entities that nothing references (no inbound relationship links) — the
    /// orphaned/empty tags, people, and studios. When false, keeps only referenced entities.
    /// </param>
    /// <param name="wanted">
    /// When set, keeps only wanted placeholders (true) or excludes them (false). External projections
    /// (Jellyfin, OPDS) pass false so fileless request placeholders never reach external clients.
    /// </param>
    /// <param name="acquisitionStatus">When set, keeps entities whose latest linked acquisition has this status.</param>
    Task<EntityListResponse> ListAsync(
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
        bool? wanted = null,
        AcquisitionStatus? acquisitionStatus = null);

    /// <summary>
    /// Lists active entities using the generated query contract shared by the API and frontend.
    /// </summary>
    Task<EntityListResponse> ListAsync(EntityListQuery query, CancellationToken cancellationToken) =>
        ListAsync(
            query.Kind,
            query.Query,
            query.Cursor,
            query.HideNsfw,
            query.Limit,
            cancellationToken,
            query.ReferencedBy,
            query.RelationshipCode,
            query.Sort,
            query.SortDir,
            query.Seed,
            query.Favorite,
            query.Organized,
            query.RatingMin,
            query.RatingMax,
            query.Unrated,
            query.Status,
            query.BookType,
            query.BookFormat,
            query.Nsfw,
            query.HasFile,
            query.Played,
            query.Orphaned,
            query.Wanted,
            query.AcquisitionStatus);

    /// <summary>
    /// Gets one active entity as the shared entity card read model.
    /// </summary>
    Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken);

    /// <summary>
    /// Gets thumbnails for the requested identifiers while preserving the caller's
    /// requested order.
    /// </summary>
    Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
        IReadOnlyList<Guid> ids,
        bool hideNsfw,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets one active entity as its kind-specific detail contract. Returns null when
    /// the entity does not exist or does not match the requested kind.
    /// </summary>
    Task<IEntityCard?> GetDetailAsync(Guid id, string kind, bool hideNsfw, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the folder-list context (visible child count, description, dates, lifetime, external
    /// ids) for a batch of container entities. Sized for external catalog list pages: one grouped
    /// query per collection across the whole batch, never a full detail hydration per row.
    /// The default returns no contexts (rows pass through unenriched), so read-model fakes and
    /// partial implementations stay source-compatible.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, EntityFolderListContext>> GetFolderListContextsAsync(
        IReadOnlyList<Guid> ids,
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, EntityFolderListContext>>(
            new Dictionary<Guid, EntityFolderListContext>());
}
