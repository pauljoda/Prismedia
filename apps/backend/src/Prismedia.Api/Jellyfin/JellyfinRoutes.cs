namespace Prismedia.Api.Jellyfin;

/// <summary>
/// Jellyfin compatibility routing constants shared by the request pipeline. The path
/// prefixes identify which inbound requests belong to the Jellyfin surface (used by both
/// authentication and the dev proxy), and the public-route paths mark the handful of
/// Jellyfin endpoints that must be reachable before a session token exists.
/// </summary>
internal static class JellyfinRoutes {
    /// <summary>Path prefixes that identify a request as targeting the Jellyfin surface.</summary>
    public static readonly string[] Prefixes =
    [
        "/System",
        "/Users",
        "/UserViews",
        "/Items",
        "/Shows",
        "/Artists",
        "/Videos",
        "/Audio",
        "/Sessions",
        "/UserPlayedItems",
        "/UserItems",
        "/MediaSegments",
        "/Library",
        "/Branding",
        "/QuickConnect",
        "/DisplayPreferences"
    ];

    /// <summary>Library prefix, matched case-sensitively to avoid colliding with the SPA.</summary>
    public const string LibraryPrefix = "/Library";

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

    /// <summary>Public users listing endpoint.</summary>
    public const string UsersPublic = "/Users/Public";

    /// <summary>Authenticate-by-name endpoint.</summary>
    public const string UsersAuthenticateByName = "/Users/AuthenticateByName";

    /// <summary>Users path prefix used for per-user authenticate matching.</summary>
    public const string UsersPrefix = "/Users/";

    /// <summary>Per-user authenticate path suffix.</summary>
    public const string AuthenticateSuffix = "/Authenticate";
}
