namespace Prismedia.Contracts.Jellyfin;

/// <summary>
/// Stable wire vocabulary for the Jellyfin/Emby compatibility surface. These values are
/// dictated by the Jellyfin client protocol (Infuse and others depend on the exact
/// spelling and casing), so they are centralized here as the single source of truth and
/// must not be retyped inline at call sites.
/// </summary>
public static class JellyfinProtocol {
    /// <summary>HTTP header names carrying Jellyfin authentication.</summary>
    public static class Headers {
        /// <summary>Emby/Jellyfin authorization header carrying client identity and token.</summary>
        public const string EmbyAuthorization = "X-Emby-Authorization";

        /// <summary>Legacy Emby token header.</summary>
        public const string EmbyToken = "X-Emby-Token";

        /// <summary>MediaBrowser token header used by some clients.</summary>
        public const string MediaBrowserToken = "X-MediaBrowser-Token";

        /// <summary>Prismedia API key supplied via Jellyfin-style header.</summary>
        public const string PrismediaApiKey = "X-Prismedia-Api-Key";
    }

    /// <summary>Authorization scheme prefixes.</summary>
    public static class Schemes {
        /// <summary>Bearer token scheme prefix (note trailing space).</summary>
        public const string Bearer = "Bearer ";

        /// <summary>MediaBrowser authorization scheme prefix (note trailing space).</summary>
        public const string MediaBrowser = "MediaBrowser ";
    }

    /// <summary>Query string keys carrying an API key.</summary>
    public static class QueryKeys {
        /// <summary>PascalCase API key query parameter.</summary>
        public const string ApiKey = "ApiKey";

        /// <summary>snake_case API key query parameter.</summary>
        public const string ApiKeySnake = "api_key";
    }

    /// <summary>Field names parsed from the Emby authorization header value.</summary>
    public static class AuthFields {
        /// <summary>Client application name.</summary>
        public const string Client = "Client";

        /// <summary>Device name.</summary>
        public const string Device = "Device";

        /// <summary>Stable device identifier.</summary>
        public const string DeviceId = "DeviceId";

        /// <summary>Client version.</summary>
        public const string Version = "Version";

        /// <summary>Access token.</summary>
        public const string Token = "Token";
    }

    /// <summary>Jellyfin <c>BaseItemDto.Type</c> discriminators.</summary>
    public static class ItemTypes {
        /// <summary>Standalone playable video (home-video library item).</summary>
        public const string Video = "Video";

        /// <summary>Single film.</summary>
        public const string Movie = "Movie";

        /// <summary>Series episode.</summary>
        public const string Episode = "Episode";

        /// <summary>Show series.</summary>
        public const string Series = "Series";

        /// <summary>Series season.</summary>
        public const string Season = "Season";

        /// <summary>Collection / box set.</summary>
        public const string BoxSet = "BoxSet";

        /// <summary>Performer / cast or crew member.</summary>
        public const string Person = "Person";

        /// <summary>Playable audio track (music library item).</summary>
        public const string Audio = "Audio";

        /// <summary>Music album grouping.</summary>
        public const string MusicAlbum = "MusicAlbum";

        /// <summary>Music artist grouping.</summary>
        public const string MusicArtist = "MusicArtist";

        /// <summary>Generic folder.</summary>
        public const string Folder = "Folder";

        /// <summary>Top-level library view folder.</summary>
        public const string CollectionFolder = "CollectionFolder";
    }

    /// <summary>Jellyfin <c>BaseItemDto.MediaType</c> and media-stream type values.</summary>
    public static class MediaTypes {
        /// <summary>Playable video media / video stream.</summary>
        public const string Video = "Video";

        /// <summary>Playable audio media / audio stream.</summary>
        public const string Audio = "Audio";

        /// <summary>Subtitle stream.</summary>
        public const string Subtitle = "Subtitle";

        /// <summary>Non-playable container (folders, library views). Real Jellyfin always emits this
        /// rather than null; strict clients decode <c>MediaType</c> into an enum and drop items missing it.</summary>
        public const string Unknown = "Unknown";
    }

    /// <summary>Jellyfin <c>ImageType</c> values used in image tags, info, and asset lookups.</summary>
    public static class ImageTypes {
        /// <summary>Primary cover/poster image.</summary>
        public const string Primary = "Primary";

        /// <summary>Backdrop / fanart image.</summary>
        public const string Backdrop = "Backdrop";

        /// <summary>Logo image.</summary>
        public const string Logo = "Logo";

        /// <summary>Thumb (landscape) image.</summary>
        public const string Thumb = "Thumb";

        /// <summary>Banner image.</summary>
        public const string Banner = "Banner";

        /// <summary>Art (clear art) image.</summary>
        public const string Art = "Art";

        /// <summary>Disc image.</summary>
        public const string Disc = "Disc";

        /// <summary>Box image.</summary>
        public const string Box = "Box";
    }

    /// <summary>Jellyfin library collection type codes.</summary>
    public static class CollectionTypes {
        /// <summary>Movies library.</summary>
        public const string Movies = "movies";

        /// <summary>TV shows library.</summary>
        public const string Shows = "tvshows";

        /// <summary>Home videos library.</summary>
        public const string HomeVideos = "homevideos";

        /// <summary>Collections / box sets library.</summary>
        public const string BoxSets = "boxsets";

        /// <summary>Music library.</summary>
        public const string Music = "music";
    }
}
