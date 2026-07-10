using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>
/// Describes one requestable media kind: how it is presented, what plugins are asked for, what library
/// entity a commit creates, and how its children behave. The whole request flow (search, review, commit)
/// and its generated frontend metadata are driven by these descriptors, so adding a media kind to
/// Discover/Request means adding a descriptor here — plus the acquisition-side engine that knows how
/// to find, grade, and import its releases.
/// </summary>
/// <param name="Kind">The wire kind this descriptor defines.</param>
/// <param name="Label">Singular user-facing name.</param>
/// <param name="Plural">Plural user-facing name.</param>
/// <param name="ChildNoun">User-facing noun for selectable direct children, when they are exposed.</param>
/// <param name="PluginEntityKind">
/// Entity kind plugins are asked to search / lookup for this request kind (e.g. an author request
/// queries plugins for a <see cref="EntityKind.Person"/>).
/// </param>
/// <param name="WantedEntityKind">Library entity kind a commit creates as the wanted placeholder.</param>
/// <param name="ProfileEntityKind">Acquisition-profile kind that governs downloads for this request.</param>
/// <param name="LibraryRootMediaCapability">Library-root media flag required by its import target.</param>
/// <param name="ReviewSelection">How a plugin proposal maps to selectable commit targets.</param>
/// <param name="IsContainer">
/// True when this kind groups works (author, series, artist): its review lists children as toggleable
/// options and a commit parents the picked children under the created container entity. False for a
/// leaf (book, movie, album), which is acquired directly.
/// </param>
/// <param name="ChildKind">
/// Request kind of the review's child options, when the kind surfaces children — an author's books, an
/// artist's albums, or a book's sibling series volumes. Null when children are not offered (yet).
/// </param>
/// <param name="Committable">
/// Whether a commit is accepted for this kind. False while the kind's acquisition engine hasn't landed,
/// in which case Discover/review still work but Request is unavailable.
/// </param>
/// <param name="AcquisitionKind">Media kind stamped on acquisitions started for this kind's leaves.</param>
/// <param name="Discoverable">
/// Whether the kind is offered in Discover search directly. False for unit kinds that only exist inside
/// a parent's flow (a season inside a series, an episode inside a season) — they are committed as
/// children or from their own wanted-placeholder pages, never searched for standalone.
/// </param>
/// <param name="AcquireFromEntity">
/// True when requesting an existing entity of this kind must build the acquisition from the entity
/// graph (its positions and ancestor titles) instead of a provider round-trip — TV units, whose search
/// context (the series name, S01E05) lives on their parents and whose providers cannot resolve them
/// standalone. False kinds re-resolve through their provider and fall back to the graph only when no
/// provider answers.
/// </param>
/// <param name="MaterializeChildPhantoms">
/// True when acquiring this unit should hydrate its structural children as wanted phantoms. This is an
/// explicit structural rule, distinct from sibling-option children such as a book series' other volumes.
/// </param>
public sealed record RequestKindDescriptor(
    RequestMediaKind Kind,
    string Label,
    string Plural,
    string? ChildNoun,
    EntityKind PluginEntityKind,
    EntityKind WantedEntityKind,
    EntityKind? ProfileEntityKind,
    LibraryRootMediaCapability? LibraryRootMediaCapability,
    RequestReviewSelection ReviewSelection,
    bool IsContainer,
    RequestMediaKind? ChildKind,
    bool Committable,
    EntityKind AcquisitionKind,
    bool Discoverable = true,
    bool AcquireFromEntity = false,
    bool MaterializeChildPhantoms = false) {
    /// <summary>The plugin-protocol kind code for <see cref="PluginEntityKind"/>.</summary>
    public string PluginKindCode => PluginEntityKind.ToCode();
}

/// <summary>
/// The closed set of request kind descriptors. One source of truth for per-kind request behavior:
/// generalize once, expand cheaply — a new medium is a new row, not a new flow.
/// </summary>
public static class RequestKindRegistry {
    /// <summary>Every requestable kind, in Discover display order.</summary>
    public static readonly IReadOnlyList<RequestKindDescriptor> All = [
        // Books: the proven vertical. A book's review may list its series' sibling volumes as children.
        new(RequestMediaKind.Book, "Book", "Books", "volume", EntityKind.Book, EntityKind.Book,
            ProfileEntityKind: EntityKind.Book, LibraryRootMediaCapability: LibraryRootMediaCapability.ScanBooks,
            ReviewSelection: RequestReviewSelection.DirectChildrenWhenPresent,
            IsContainer: false, ChildKind: RequestMediaKind.Book, Committable: true, AcquisitionKind: EntityKind.Book),
        // An author is a person to plugins but a BookAuthor grouping in the library.
        new(RequestMediaKind.Author, "Author", "Authors", "book", EntityKind.Person, EntityKind.BookAuthor,
            ProfileEntityKind: EntityKind.Book, LibraryRootMediaCapability: LibraryRootMediaCapability.ScanBooks,
            ReviewSelection: RequestReviewSelection.DirectChildren,
            IsContainer: true, ChildKind: RequestMediaKind.Book, Committable: true, AcquisitionKind: EntityKind.Book),

        // Movies: a single wanted Movie entity; the acquisition delivers its video file.
        new(RequestMediaKind.Movie, "Movie", "Movies", null, EntityKind.Movie, EntityKind.Movie,
            ProfileEntityKind: EntityKind.Movie, LibraryRootMediaCapability: LibraryRootMediaCapability.ScanVideos,
            ReviewSelection: RequestReviewSelection.Root,
            IsContainer: false, ChildKind: null, Committable: true, AcquisitionKind: EntityKind.Movie),

        // TV: the deepest container chain — a series fans out into per-season acquisitions (season
        // packs), and each season materializes its episodes as wanted phantoms requested individually
        // from their own pages. Seasons and episodes are unit kinds, not Discover entries.
        new(RequestMediaKind.Series, "Series", "Series", "season", EntityKind.VideoSeries, EntityKind.VideoSeries,
            ProfileEntityKind: EntityKind.VideoSeries, LibraryRootMediaCapability: LibraryRootMediaCapability.ScanVideos,
            ReviewSelection: RequestReviewSelection.DirectChildren,
            IsContainer: true, ChildKind: RequestMediaKind.Season, Committable: true, AcquisitionKind: EntityKind.VideoSeason),
        new(RequestMediaKind.Season, "Season", "Seasons", "episode", EntityKind.VideoSeason, EntityKind.VideoSeason,
            ProfileEntityKind: EntityKind.VideoSeries, LibraryRootMediaCapability: LibraryRootMediaCapability.ScanVideos,
            ReviewSelection: RequestReviewSelection.Root,
            IsContainer: false, ChildKind: RequestMediaKind.Episode, Committable: true, AcquisitionKind: EntityKind.VideoSeason,
            Discoverable: false, AcquireFromEntity: true, MaterializeChildPhantoms: true),
        new(RequestMediaKind.Episode, "Episode", "Episodes", null, EntityKind.Video, EntityKind.Video,
            ProfileEntityKind: EntityKind.VideoSeries, LibraryRootMediaCapability: LibraryRootMediaCapability.ScanVideos,
            ReviewSelection: RequestReviewSelection.Root,
            IsContainer: false, ChildKind: null, Committable: true, AcquisitionKind: EntityKind.Video,
            Discoverable: false, AcquireFromEntity: true),

        // Music: the album is the acquisition unit; an artist fans out into album acquisitions.
        new(RequestMediaKind.Artist, "Artist", "Artists", "album", EntityKind.MusicArtist, EntityKind.MusicArtist,
            ProfileEntityKind: EntityKind.AudioLibrary, LibraryRootMediaCapability: LibraryRootMediaCapability.ScanAudio,
            ReviewSelection: RequestReviewSelection.DirectChildren,
            IsContainer: true, ChildKind: RequestMediaKind.Album, Committable: true, AcquisitionKind: EntityKind.AudioLibrary),
        new(RequestMediaKind.Album, "Album", "Albums", null, EntityKind.AudioLibrary, EntityKind.AudioLibrary,
            ProfileEntityKind: EntityKind.AudioLibrary, LibraryRootMediaCapability: LibraryRootMediaCapability.ScanAudio,
            ReviewSelection: RequestReviewSelection.Root,
            IsContainer: false, ChildKind: null, Committable: true, AcquisitionKind: EntityKind.AudioLibrary),
    ];

    private static readonly IReadOnlyDictionary<RequestMediaKind, RequestKindDescriptor> ByKind =
        All.ToDictionary(descriptor => descriptor.Kind);

    /// <summary>The descriptor for a kind, or null when the kind isn't part of the request flow (e.g. the plugin passthrough).</summary>
    public static RequestKindDescriptor? Find(RequestMediaKind kind) =>
        ByKind.GetValueOrDefault(kind);

    /// <summary>
    /// The child descriptor a container fans out into (an author's books, an artist's albums), or null
    /// when the kind has no child options.
    /// </summary>
    public static RequestKindDescriptor? ChildOf(RequestKindDescriptor descriptor) =>
        descriptor.ChildKind is { } childKind ? Find(childKind) : null;

    /// <summary>
    /// Whether the descriptor exposes a direct child kind the request flow can actually commit. This is
    /// deliberately independent of <see cref="RequestKindDescriptor.IsContainer"/>: books and seasons can
    /// search missing children without running provider-container discovery, while an album currently has
    /// neither even when requestable child Entities happen to be present in its library graph.
    /// </summary>
    public static bool CanSearchMissingChildren(RequestKindDescriptor descriptor) =>
        ChildOf(descriptor) is { Committable: true };

    /// <summary>
    /// Resolves a structural acquisition unit whose import must be checked for still-wanted direct
    /// children. This is descriptor-driven so the monitored fallback used by seasons also applies when a
    /// future album, volume, or other Entity kind opts into child phantom materialization.
    /// </summary>
    public static RequestKindDescriptor? FindChildMaterializingUnit(EntityKind entityKind) =>
        All.FirstOrDefault(descriptor =>
            descriptor.MaterializeChildPhantoms
            && descriptor.WantedEntityKind == entityKind
            && ChildOf(descriptor) is { Committable: true });
}
