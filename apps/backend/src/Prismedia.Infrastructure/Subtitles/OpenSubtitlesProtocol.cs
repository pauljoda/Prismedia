namespace Prismedia.Infrastructure.Subtitles;

/// <summary>OpenSubtitles REST vocabulary and safe endpoint constants.</summary>
internal static class OpenSubtitlesProtocol {
    public const string ApiBaseUrl = "https://api.opensubtitles.com/api/v1/";
    public const string ApiKeyHeader = "Api-Key";
    public const string UserAgentHeader = "User-Agent";
    public const string AuthorizationScheme = "Bearer";
    public const string UserAgent = "Prismedia/1.0";
    public const string LoginPath = "login";
    public const string SubtitlesPath = "subtitles";
    public const string DownloadPath = "download";
    public const string InfosUserPath = "infos/user";
    public const string OutputFormat = "srt";
    public const string ApiKeyEnvironment = "PRISMEDIA_OPENSUBTITLES_API_KEY";
    public const string UsernameEnvironment = "PRISMEDIA_OPENSUBTITLES_USERNAME";
    public const string PasswordEnvironment = "PRISMEDIA_OPENSUBTITLES_PASSWORD";

    public static bool IsAllowedApiBase(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        (string.Equals(uri.Host, "api.opensubtitles.com", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Host, "vip-api.opensubtitles.com", StringComparison.OrdinalIgnoreCase));
}
