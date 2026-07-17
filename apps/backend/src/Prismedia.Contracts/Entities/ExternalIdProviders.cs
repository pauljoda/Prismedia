namespace Prismedia.Contracts.Entities;

/// <summary>
/// Stable keys for well-known external metadata providers stored on
/// <c>EntityExternalId.Provider</c>. The set of providers is intentionally open — plugins
/// may introduce arbitrary providers — so this is a constants class of the commonly
/// referenced keys rather than a closed enum. Use these constants instead of retyping
/// the keys when matching or labeling known providers.
/// </summary>
public static class ExternalIdProviders {
    /// <summary>AniDB anime database.</summary>
    public const string AniDb = "anidb";

    /// <summary>Internet Movie Database.</summary>
    public const string Imdb = "imdb";

    /// <summary>The Movie Database.</summary>
    public const string Tmdb = "tmdb";

    /// <summary>TheTVDB.</summary>
    public const string Tvdb = "tvdb";

    /// <summary>Stash / StashBox.</summary>
    public const string Stash = "stash";

    /// <summary>MusicBrainz music database.</summary>
    public const string MusicBrainz = "musicbrainz";
}
