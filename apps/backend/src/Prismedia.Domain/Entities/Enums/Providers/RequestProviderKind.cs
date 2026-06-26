namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of request upstream provider families supported by Prismedia.
/// </summary>
public enum RequestProviderKind {
    /// <summary>Radarr movie request provider.</summary>
    [Code("radarr")]
    Radarr,

    /// <summary>Sonarr series request provider.</summary>
    [Code("sonarr")]
    Sonarr,

    /// <summary>Lidarr music request provider.</summary>
    [Code("lidarr")]
    Lidarr,

    /// <summary>Prismedia plugin-backed request provider.</summary>
    [Code("plugin")]
    Plugin
}

/// <summary>
/// Closed set of Radarr minimum-availability gates applied when a movie request is added.
/// </summary>
public enum RequestMinimumAvailability {
    /// <summary>Add the movie as soon as it is announced.</summary>
    [Code("announced")]
    Announced,

    /// <summary>Add the movie once it is in cinemas.</summary>
    [Code("inCinemas")]
    InCinemas,

    /// <summary>Add the movie once it has a physical or digital release.</summary>
    [Code("released")]
    Released
}

/// <summary>
/// Closed set of lifecycle states for a submitted media request. The stored value is a cache of
/// the last observed upstream state; viewing request history refreshes it live from the service.
/// </summary>
public enum RequestHistoryStatus {
    /// <summary>Accepted by the upstream service; no further status observed yet.</summary>
    [Code("submitted")]
    Submitted,

    /// <summary>Tracked and monitored upstream, but nothing has been downloaded yet.</summary>
    [Code("pending")]
    Pending,

    /// <summary>A related download is currently active in the upstream queue.</summary>
    [Code("downloading")]
    Downloading,

    /// <summary>Some, but not all, of the requested content has been downloaded.</summary>
    [Code("partial")]
    Partial,

    /// <summary>All requested content has been downloaded by the upstream service.</summary>
    [Code("available")]
    Available,

    /// <summary>The item is no longer present in the upstream service's library.</summary>
    [Code("removed")]
    Removed,

    /// <summary>The upstream service could not be reached to refresh the status.</summary>
    [Code("unknown")]
    Unknown
}

/// <summary>
/// Closed set of rating sources surfaced on request detail pages. Values arrive
/// from the Arr lookup's ratings block and from TMDB enrichment.
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
/// Closed set of media categories exposed by the request workflow.
/// </summary>
public enum RequestMediaKind {
    /// <summary>Movie request target.</summary>
    [Code("movie")]
    Movie,

    /// <summary>Series request target.</summary>
    [Code("series")]
    Series,

    /// <summary>Artist request target.</summary>
    [Code("artist")]
    Artist,

    /// <summary>Album request target.</summary>
    [Code("album")]
    Album,

    /// <summary>Book request target (fulfilled by Prismedia-direct acquisition or an external app).</summary>
    [Code("book")]
    Book,

    /// <summary>Plugin-defined request target.</summary>
    [Code("plugin")]
    Plugin
}

/// <summary>
/// Closed set of fulfilment modes for a content kind: Prismedia handles acquisition directly,
/// or the request is handed off to an external application (Radarr/Sonarr/Lidarr).
/// </summary>
public enum FulfillmentMode {
    /// <summary>Prismedia searches indexers and downloads the release itself.</summary>
    [Code("prismedia")]
    Prismedia,

    /// <summary>The request is handed off to a configured external service.</summary>
    [Code("external")]
    External
}
