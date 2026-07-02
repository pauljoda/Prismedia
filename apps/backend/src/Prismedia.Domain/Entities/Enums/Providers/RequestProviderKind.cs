namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of request upstream provider families supported by Prismedia. Prismedia fulfils requests
/// itself through its plugin-backed acquisition pipeline; external-app handoffs are not supported.
/// </summary>
public enum RequestProviderKind {
    /// <summary>Prismedia plugin-backed request provider.</summary>
    [Code("plugin")]
    Plugin
}

/// <summary>
/// Closed set of rating sources surfaced on request detail pages.
/// </summary>
public enum RequestRatingSource {
    /// <summary>The Movie Database community score (0–10).</summary>
    [Code("tmdb")]
    Tmdb,

    /// <summary>IMDb user rating (0–10).</summary>
    [Code("imdb")]
    Imdb,

    /// <summary>Rotten Tomatoes critics score (0–100).</summary>
    [Code("rotten-tomatoes")]
    RottenTomatoes,

    /// <summary>Metacritic metascore (0–100).</summary>
    [Code("metacritic")]
    Metacritic
}

/// <summary>
/// Closed set of media categories exposed by the request workflow. Prismedia fulfils requests through
/// its own acquisition pipeline. Leaf kinds (book, movie, album) are acquired directly; container kinds
/// (author, series, artist) surface their works as toggleable children and are created as wanted library
/// entities that group them. Per-kind behavior lives in <c>RequestKindRegistry</c> — this enum is only
/// the wire vocabulary.
/// </summary>
public enum RequestMediaKind {
    /// <summary>Book request target (fulfilled by Prismedia-direct acquisition).</summary>
    [Code("book")]
    Book,

    /// <summary>
    /// Author request target: a container kind that surfaces an author's books as toggleable children.
    /// Committing an author request creates the author as a wanted library entity and fans each selected
    /// work out into its own <see cref="Book"/> acquisition beneath it; the author itself is never
    /// downloaded, only its books are.
    /// </summary>
    [Code("author")]
    Author,

    /// <summary>Movie request target: a wanted Movie entity whose video file arrives by acquisition.</summary>
    [Code("movie")]
    Movie,

    /// <summary>
    /// TV series request target: a container kind. Discover and detail are available; committing waits
    /// for the per-episode acquisition engine (the richest container case, deliberately last).
    /// </summary>
    [Code("series")]
    Series,

    /// <summary>
    /// Music artist request target: a container kind that surfaces the artist's albums as toggleable
    /// children, each fanned out into its own <see cref="Album"/> acquisition.
    /// </summary>
    [Code("artist")]
    Artist,

    /// <summary>Album request target: a wanted album entity — the acquisition unit for music.</summary>
    [Code("album")]
    Album,

    /// <summary>Plugin-defined request target.</summary>
    [Code("plugin")]
    Plugin
}

/// <summary>
/// Closed set of per-item outcomes of committing a request. A commit is item-granular: an author
/// request reports one outcome per selected book, so partial success stays transparent.
/// </summary>
public enum RequestCommitOutcome {
    /// <summary>A wanted library entity was created (or reused) and an acquisition was started for it.</summary>
    [Code("requested")]
    Requested,

    /// <summary>The library already owns this item with a real file; nothing was created.</summary>
    [Code("already-owned")]
    AlreadyOwned,

    /// <summary>The item is already a wanted entity with an acquisition in flight; nothing new was started.</summary>
    [Code("already-requested")]
    AlreadyRequested
}
