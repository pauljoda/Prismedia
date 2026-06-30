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
/// Closed set of media categories exposed by the request workflow. Prismedia fulfils requests through its
/// own acquisition pipeline, so the requestable kinds are books and the author container that groups them.
/// </summary>
public enum RequestMediaKind {
    /// <summary>Book request target (fulfilled by Prismedia-direct acquisition).</summary>
    [Code("book")]
    Book,

    /// <summary>
    /// Author request target: a container kind that surfaces an author's books as toggleable children.
    /// Requesting an author fans each selected work out into its own <see cref="Book"/> acquisition; the
    /// author itself is never acquired as an entity.
    /// </summary>
    [Code("author")]
    Author,

    /// <summary>Plugin-defined request target.</summary>
    [Code("plugin")]
    Plugin
}
