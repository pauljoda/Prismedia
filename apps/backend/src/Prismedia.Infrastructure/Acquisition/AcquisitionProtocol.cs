namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Shared wire vocabulary for acquisition adapters; secrets credential keys and HTTP header names referenced rather than retyped.</summary>
public static class AcquisitionHttp {
    /// <summary>Credential key under which an indexer's API key is stored.</summary>
    public const string IndexerApiKeyCredential = "apiKey";

    /// <summary>Credential key under which a download client's password is stored.</summary>
    public const string DownloadClientPasswordCredential = "password";

    /// <summary>Credential key under which a download client's API key is stored (SABnzbd).</summary>
    public const string DownloadClientApiKeyCredential = "apiKey";
}

/// <summary>Transmission RPC wire vocabulary (RPC spec 15+/Transmission 3+). Referenced by the Transmission download client; never retyped.</summary>
public static class TransmissionProtocol {
    /// <summary>Default RPC endpoint path appended to a base URL that doesn't already point at the RPC.</summary>
    public const string RpcPath = "transmission/rpc";
    public const string SessionIdHeader = "X-Transmission-Session-Id";

    // ── methods ─────────────────────────────────────────────────
    public const string MethodSessionGet = "session-get";
    public const string MethodTorrentAdd = "torrent-add";
    public const string MethodTorrentGet = "torrent-get";
    public const string MethodTorrentRemove = "torrent-remove";

    // ── request/response fields ─────────────────────────────────
    // prism-vocab: external — Transmission RPC field names, decoded only at this parse boundary.
    public const string Method = "method";
    public const string Arguments = "arguments";
    public const string Result = "result";
    public const string ResultSuccess = "success";
    public const string Filename = "filename";
    public const string Metainfo = "metainfo";
    public const string Labels = "labels";
    public const string Ids = "ids";
    public const string Fields = "fields";
    public const string DeleteLocalData = "delete-local-data";
    public const string TorrentAdded = "torrent-added";
    public const string TorrentDuplicate = "torrent-duplicate";
    public const string Torrents = "torrents";
    public const string Version = "version";
    public const string HashString = "hashString";
    public const string Name = "name";
    public const string PercentDone = "percentDone";
    public const string Status = "status";
    public const string IsStalled = "isStalled";
    public const string IsFinished = "isFinished";
    public const string ErrorCode = "error";
    public const string ErrorString = "errorString";
    public const string DownloadDir = "downloadDir";
    public const string TotalSize = "totalSize";
    public const string RateDownload = "rateDownload";
    public const string RateUpload = "rateUpload";
    public const string Eta = "eta";
    public const string PeersSendingToUs = "peersSendingToUs";
    public const string PeersGettingFromUs = "peersGettingFromUs";
    public const string UploadRatio = "uploadRatio";
    public const string SecondsSeeding = "secondsSeeding";
    public const string Files = "files";
    public const string FileLength = "length";
    public const string FileBytesCompleted = "bytesCompleted";
    public const string Pieces = "pieces";
    public const string PieceCount = "pieceCount";

    /// <summary>The torrent-get fields the status/properties projections need.</summary>
    public static readonly string[] StatusFields = [
        HashString, Name, PercentDone, Status, IsStalled, IsFinished, ErrorCode, ErrorString,
        DownloadDir, TotalSize, RateDownload, RateUpload, Eta, PeersSendingToUs, PeersGettingFromUs, Labels,
        UploadRatio, SecondsSeeding
    ];
}

/// <summary>SABnzbd JSON API wire vocabulary. Referenced by the SABnzbd download client; never retyped.</summary>
public static class SabnzbdProtocol {
    public const string ApiEndpoint = "api";

    // ── request parameters ──────────────────────────────────────
    public const string ModeParam = "mode";
    public const string OutputParam = "output";
    public const string OutputJson = "json";
    public const string ApiKeyParam = "apikey";
    public const string UsernameParam = "ma_username";
    public const string PasswordParam = "ma_password";
    public const string NameParam = "name";
    public const string ValueParam = "value";
    public const string CategoryParam = "cat";
    public const string NzoIdsParam = "nzo_ids";
    public const string LimitParam = "limit";
    public const string DeleteFilesParam = "del_files";
    public const string NzbFileField = "name";

    // ── modes ───────────────────────────────────────────────────
    public const string ModeVersion = "version";
    public const string ModeQueue = "queue";
    public const string ModeHistory = "history";
    public const string ModeAddUrl = "addurl";
    public const string ModeAddFile = "addfile";
    public const string ModeGetFiles = "get_files";
    public const string ModeGetCategories = "get_cats";
    public const string OperationDelete = "delete";

    // ── response fields ─────────────────────────────────────────
    // prism-vocab: external — SABnzbd JSON field names, decoded only at this parse boundary.
    public const string Version = "version";
    public const string Status = "status";
    public const string Error = "error";
    public const string NzoIds = "nzo_ids";
    public const string Queue = "queue";
    public const string History = "history";
    public const string Slots = "slots";
    public const string Categories = "categories";
    public const string Files = "files";
    public const string NzoId = "nzo_id";
    public const string Filename = "filename";
    public const string HistoryName = "name";
    public const string QueueCategory = "cat";
    public const string HistoryCategory = "category";
    public const string Mb = "mb";
    public const string MbLeft = "mbleft";
    public const string Bytes = "bytes";
    public const string TimeLeft = "timeleft";
    public const string KbPerSec = "kbpersec";
    public const string SlotStatus = "status";
    public const string Storage = "storage";
    public const string FailMessage = "fail_message";

    // ── slot status values ──────────────────────────────────────
    // prism-vocab: external — SABnzbd status strings, matched only at this boundary.
    public const string StatusCompleted = "Completed";
    public const string StatusFailed = "Failed";
    public const string StatusPaused = "Paused";

    /// <summary>True when a history slot's status means the download finished and its payload is on disk.</summary>
    public static bool IsCompletedStatus(string? status) =>
        string.Equals(status, StatusCompleted, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a history slot's status means the download definitively failed.</summary>
    public static bool IsFailedStatus(string? status) =>
        string.Equals(status, StatusFailed, StringComparison.OrdinalIgnoreCase);
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
    public const string ShareRatio = "share_ratio";
    public const string SeedingTime = "seeding_time";

    // qBittorrent state values that mean a download is stuck and won't progress without intervention.
    // prism-vocab: external — qBittorrent state strings, matched only at this boundary.
    public const string StateStalledDownload = "stalledDL";
    public const string StateMetadataDownload = "metaDL";
    public const string StateError = "error";
    public const string StateMissingFiles = "missingFiles";

    /// <summary>True when a qBittorrent state means the download is stuck (no peers/metadata, errored, or missing files) and will not progress on its own.</summary>
    public static bool IsStalledState(string? state) =>
        state is StateStalledDownload or StateMetadataDownload or StateError or StateMissingFiles;

    /// <summary>qBittorrent requires a Referer header on WebUI API calls to pass its CSRF check.</summary>
    public const string RefererHeader = "Referer";

    /// <summary>Marker shared by qBittorrent session-cookie names across versions (<c>SID</c>, <c>QBT_SID_&lt;port&gt;</c>).</summary>
    public const string SessionCookieMarker = "SID";
}
