using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>
/// Describes one requestable media kind: what plugins are asked for, what library entity a commit
/// creates, and how its children behave. The whole request flow (search, detail, commit) is driven by
/// these descriptors, so adding a media kind to Discover/Request means adding a descriptor here — plus
/// the acquisition-side engine that knows how to find, grade, and import its releases.
/// </summary>
/// <param name="Kind">The wire kind this descriptor defines.</param>
/// <param name="PluginEntityKind">
/// Entity kind plugins are asked to search / lookup for this request kind (e.g. an author request
/// queries plugins for a <see cref="EntityKind.Person"/>).
/// </param>
/// <param name="WantedEntityKind">Library entity kind a commit creates as the wanted placeholder.</param>
/// <param name="IsContainer">
/// True when this kind groups works (author, series, artist): its detail lists children as toggleable
/// options and a commit parents the picked children under the created container entity. False for a
/// leaf (book, movie, album), which is acquired directly.
/// </param>
/// <param name="ChildKind">
/// Request kind of the detail's child options, when the kind surfaces children — an author's books, an
/// artist's albums, or a book's sibling series volumes. Null when children are not offered (yet).
/// </param>
/// <param name="Committable">
/// Whether a commit is accepted for this kind. False while the kind's acquisition engine hasn't landed,
/// in which case Discover/detail still work but Request is unavailable.
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
public sealed record RequestKindDescriptor(
    RequestMediaKind Kind,
    EntityKind PluginEntityKind,
    EntityKind WantedEntityKind,
    bool IsContainer,
    RequestMediaKind? ChildKind,
    bool Committable,
    EntityKind AcquisitionKind,
    bool Discoverable = true,
    bool AcquireFromEntity = false) {
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
        // Books: the proven vertical. A book's detail may list its series' sibling volumes as children.
        new(RequestMediaKind.Book, EntityKind.Book, EntityKind.Book,
            IsContainer: false, ChildKind: RequestMediaKind.Book, Committable: true, AcquisitionKind: EntityKind.Book),
        // An author is a person to plugins but a BookAuthor grouping in the library.
        new(RequestMediaKind.Author, EntityKind.Person, EntityKind.BookAuthor,
            IsContainer: true, ChildKind: RequestMediaKind.Book, Committable: true, AcquisitionKind: EntityKind.Book),

        // Movies: a single wanted Movie entity; the acquisition delivers its video file.
        new(RequestMediaKind.Movie, EntityKind.Movie, EntityKind.Movie,
            IsContainer: false, ChildKind: null, Committable: true, AcquisitionKind: EntityKind.Movie),

        // TV: the deepest container chain — a series fans out into per-season acquisitions (season
        // packs), and each season materializes its episodes as wanted phantoms requested individually
        // from their own pages. Seasons and episodes are unit kinds, not Discover entries.
        new(RequestMediaKind.Series, EntityKind.VideoSeries, EntityKind.VideoSeries,
            IsContainer: true, ChildKind: RequestMediaKind.Season, Committable: true, AcquisitionKind: EntityKind.VideoSeason),
        new(RequestMediaKind.Season, EntityKind.VideoSeason, EntityKind.VideoSeason,
            IsContainer: false, ChildKind: RequestMediaKind.Episode, Committable: true, AcquisitionKind: EntityKind.VideoSeason,
            Discoverable: false, AcquireFromEntity: true),
        new(RequestMediaKind.Episode, EntityKind.Video, EntityKind.Video,
            IsContainer: false, ChildKind: null, Committable: true, AcquisitionKind: EntityKind.Video,
            Discoverable: false, AcquireFromEntity: true),

        // Music: the album is the acquisition unit; an artist fans out into album acquisitions.
        new(RequestMediaKind.Artist, EntityKind.MusicArtist, EntityKind.MusicArtist,
            IsContainer: true, ChildKind: RequestMediaKind.Album, Committable: true, AcquisitionKind: EntityKind.AudioLibrary),
        new(RequestMediaKind.Album, EntityKind.AudioLibrary, EntityKind.AudioLibrary,
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
}
