namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Shared wire vocabulary for acquisition adapters; secrets credential keys and HTTP header names referenced rather than retyped.</summary>
public static class AcquisitionHttp {
    /// <summary>Credential key under which an indexer's API key is stored.</summary>
    public const string IndexerApiKeyCredential = "apiKey";

    /// <summary>Credential key under which a download client's password is stored.</summary>
    public const string DownloadClientPasswordCredential = "password";
}

/// <summary>Prowlarr REST API wire vocabulary (Prowlarr v1). Referenced by the Prowlarr indexer client; never retyped.</summary>
public static class ProwlarrProtocol {
    public const string ApiKeyHeader = "X-Api-Key";

    public const string SearchEndpoint = "api/v1/search";
    public const string SystemStatusEndpoint = "api/v1/system/status";

    // ── ReleaseResource fields ──────────────────────────────────
    // prism-vocab: external — Prowlarr JSON field names, decoded only at this parse boundary.
    public const string Title = "title";
    public const string Size = "size";
    public const string Seeders = "seeders";
    public const string Leechers = "leechers";
    public const string Protocol = "protocol";
    public const string DownloadUrl = "downloadUrl";
    public const string MagnetUrl = "magnetUrl";
    public const string InfoHash = "infoHash";
    public const string InfoUrl = "infoUrl";
    public const string PublishDate = "publishDate";

    // ── Search query parameters ─────────────────────────────────
    public const string QueryParam = "query";
    public const string TypeParam = "type";
    public const string TypeSearch = "search";
    public const string CategoriesParam = "categories";
    public const string LimitParam = "limit";
    public const int DefaultLimit = 100;
}
