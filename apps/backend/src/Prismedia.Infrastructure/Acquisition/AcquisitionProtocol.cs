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

/// <summary>qBittorrent WebUI API wire vocabulary (API v2). Referenced by the qBittorrent download client; never retyped.</summary>
public static class QBittorrentProtocol {
    public const string LoginEndpoint = "api/v2/auth/login";
    public const string VersionEndpoint = "api/v2/app/version";
    public const string CreateCategoryEndpoint = "api/v2/torrents/createCategory";
    public const string AddEndpoint = "api/v2/torrents/add";
    public const string InfoEndpoint = "api/v2/torrents/info";
    public const string DeleteEndpoint = "api/v2/torrents/delete";
    public const string FilesEndpoint = "api/v2/torrents/files";
    public const string PropertiesEndpoint = "api/v2/torrents/properties";
    public const string PieceStatesEndpoint = "api/v2/torrents/pieceStates";

    public const string UsernameField = "username";
    public const string PasswordField = "password";
    public const string CategoryField = "category";
    public const string SavePathField = "savePath";
    public const string UrlsField = "urls";
    public const string TorrentsField = "torrents";
    public const string HashesField = "hashes";
    public const string DeleteFilesField = "deleteFiles";

    // ── torrent info / files / properties JSON fields ───────────
    // prism-vocab: external — qBittorrent JSON field names, decoded only at this parse boundary.
    public const string Hash = "hash";
    public const string Name = "name";
    public const string Size = "size";
    public const string Progress = "progress";
    public const string State = "state";
    public const string SavePathJson = "save_path";
    public const string ContentPathJson = "content_path";
    public const string TotalSize = "total_size";
    public const string DlSpeed = "dl_speed";
    public const string UpSpeed = "up_speed";
    public const string Eta = "eta";
    public const string Seeds = "seeds";
    public const string Peers = "peers";

    /// <summary>qBittorrent requires a Referer header on WebUI API calls to pass its CSRF check.</summary>
    public const string RefererHeader = "Referer";

    /// <summary>Marker shared by qBittorrent session-cookie names across versions (<c>SID</c>, <c>QBT_SID_&lt;port&gt;</c>).</summary>
    public const string SessionCookieMarker = "SID";
}
