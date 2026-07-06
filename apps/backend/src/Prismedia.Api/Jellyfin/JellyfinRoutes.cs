namespace Prismedia.Api.Jellyfin;

/// <summary>
/// Jellyfin compatibility routing constants shared by the request pipeline. The path
/// prefixes identify which inbound requests belong to the Jellyfin surface (used by both
/// authentication and the dev proxy), and the public-route paths mark the handful of
/// Jellyfin endpoints that must be reachable before a session token exists.
/// </summary>
internal static class JellyfinRoutes {
    /// <summary>Videos prefix; also a lowercase SPA route, so matched shape-aware.</summary>
    public const string VideosPrefix = "/Videos";

    /// <summary>Audio prefix; also a lowercase SPA route, so matched shape-aware.</summary>
    public const string AudioPrefix = "/Audio";

    /// <summary>Artists prefix; also a lowercase SPA route, so matched shape-aware.</summary>
    public const string ArtistsPrefix = "/Artists";

    /// <summary>Library prefix; also a lowercase SPA route, so matched shape-aware.</summary>
    public const string LibraryPrefix = "/Library";

    /// <summary>Path prefixes that identify a request as targeting the Jellyfin surface.</summary>
    public static readonly string[] Prefixes =
    [
        "/System",
        "/Users",
        "/UserViews",
        "/Items",
        "/Shows",
        ArtistsPrefix,
        VideosPrefix,
        AudioPrefix,
        "/Sessions",
        "/UserPlayedItems",
        "/UserItems",
        "/Playlists",
        "/MediaSegments",
        LibraryPrefix,
        "/Branding",
        "/QuickConnect",
        "/DisplayPreferences",
        "/Startup"
    ];

    /// <summary>
    /// Jellyfin prefixes that also exist as lowercase SPA routes (<c>/videos</c>, <c>/audio</c>,
    /// <c>/artists</c>, <c>/library</c>). These are matched shape-aware: the bare page and the
    /// entity-detail route (<c>/videos/{id}</c>) belong to the SPA, while PascalCase or
    /// sub-resource paths (<c>/Videos/{id}/stream</c>, <c>/videos/ActiveEncodings</c>) are Jellyfin.
    /// </summary>
    private static readonly string[] SpaCollidablePrefixes =
    [
        VideosPrefix,
        AudioPrefix,
        ArtistsPrefix,
        LibraryPrefix
    ];

    /// <summary>
    /// Classifies a request path as targeting the Jellyfin surface. Shared by the dev proxy
    /// and authentication so both agree on which lowercase requests belong to the backend
    /// (Jellyfin clients such as Infuse send lowercase routes) versus the Svelte SPA.
    /// </summary>
    public static bool IsJellyfinRequest(string path) =>
        Prefixes.Any(prefix => MatchesPrefix(path, prefix));

    private static bool MatchesPrefix(string path, string prefix) {
        // Non-colliding prefixes (e.g. /Items, /Users): match case-insensitively so lowercase
        // clients still reach the backend.
        if (Array.IndexOf(SpaCollidablePrefixes, prefix) < 0) {
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        // Colliding prefixes double as SPA routes. PascalCase is always Jellyfin; the lowercase
        // form is Jellyfin only when it is a sub-resource rather than the bare page or detail route.
        if (StartsWithSegment(path, prefix, StringComparison.Ordinal)) {
            return true;
        }

        if (!StartsWithSegment(path, prefix, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return !IsSpaEntityPath(path, prefix);
    }

    /// <summary>True when <paramref name="path"/> begins with <paramref name="prefix"/> on a path-segment boundary.</summary>
    private static bool StartsWithSegment(string path, string prefix, StringComparison comparison) =>
        path.StartsWith(prefix, comparison) &&
        (path.Length == prefix.Length || path[prefix.Length] == '/');

    /// <summary>True for the SPA list page (<c>/videos</c>) and entity-detail route (<c>/videos/{guid}</c>).</summary>
    private static bool IsSpaEntityPath(string path, string prefix) {
        var remainder = path[prefix.Length..].Trim('/');
        if (remainder.Length == 0) {
            return true;
        }

        var segments = remainder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 1 && Guid.TryParse(segments[0], out _);
    }

    /// <summary>Public system info endpoint.</summary>
    public const string SystemInfoPublic = "/System/Info/Public";

    /// <summary>Server ping endpoint.</summary>
    public const string SystemPing = "/System/Ping";

    /// <summary>Branding configuration endpoint.</summary>
    public const string BrandingConfiguration = "/Branding/Configuration";

    /// <summary>Branding CSS endpoint.</summary>
    public const string BrandingCss = "/Branding/Css";

    /// <summary>Branding CSS endpoint with file extension.</summary>
    public const string BrandingCssFile = "/Branding/Css.css";

    /// <summary>Branding splashscreen endpoint.</summary>
    public const string BrandingSplashscreen = "/Branding/Splashscreen";

    /// <summary>QuickConnect availability probe, called before authentication.</summary>
    public const string QuickConnectEnabled = "/QuickConnect/Enabled";

    /// <summary>QuickConnect disabled-login initiation probe.</summary>
    public const string QuickConnectInitiate = "/QuickConnect/Initiate";

    /// <summary>QuickConnect polling endpoint.</summary>
    public const string QuickConnectConnect = "/QuickConnect/Connect";

    /// <summary>Public users listing endpoint.</summary>
    public const string UsersPublic = "/Users/Public";

    /// <summary>Authenticate-by-name endpoint.</summary>
    public const string UsersAuthenticateByName = "/Users/AuthenticateByName";

    /// <summary>QuickConnect authenticate endpoint.</summary>
    public const string UsersAuthenticateWithQuickConnect = "/Users/AuthenticateWithQuickConnect";

    /// <summary>Users path prefix used for per-user authenticate matching.</summary>
    public const string UsersPrefix = "/Users/";

    /// <summary>Per-user authenticate path suffix.</summary>
    public const string AuthenticateSuffix = "/Authenticate";
}
