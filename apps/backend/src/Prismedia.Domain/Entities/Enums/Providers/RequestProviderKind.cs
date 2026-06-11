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

    /// <summary>Plugin-defined request target.</summary>
    [Code("plugin")]
    Plugin
}
