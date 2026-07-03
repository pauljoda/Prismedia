using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Connection details a download client needs to act on a transfer.</summary>
/// <param name="ApiKey">API key for clients that authenticate with one (SABnzbd); null for cookie/session clients.</param>
public sealed record DownloadClientConnection(
    Guid Id,
    DownloadClientKind Kind,
    string BaseUrl,
    string? Username,
    string? Password,
    string Category,
    string? ApiKey = null);

/// <summary>A release to add to a download client.</summary>
/// <param name="Url">The download/magnet URL (for Prowlarr, a self-authenticating proxy URL).</param>
/// <param name="InfoHash">The release info hash, used to track the resulting torrent. Null when unknown.</param>
public sealed record DownloadAddRequest(string Url, string? InfoHash, string Category);

/// <summary>Current state of an item in a download client.</summary>
/// <param name="Progress">Transfer progress in the range 0..1.</param>
/// <param name="ContentPath">On-disk path of the downloaded content, available once known.</param>
public sealed record DownloadItemStatus(
    string ClientItemId,
    string? Name,
    double Progress,
    string? State,
    bool IsComplete,
    string? SavePath,
    string? ContentPath,
    /// <summary>
    /// True when the client reports the transfer is stuck and won't progress on its own (no peers/metadata,
    /// errored, or missing files). Normalized by the client adapter so callers stay free of client-specific
    /// state vocabulary. Distinct from <see cref="IsComplete"/>; a completed transfer is never stalled.
    /// </summary>
    bool IsStalled = false,
    /// <summary>
    /// True when the client reports the transfer definitively failed (e.g. a SABnzbd history entry in
    /// Failed status — download incomplete, unpack failed, or encrypted). Unlike <see cref="IsStalled"/>,
    /// a failed transfer cannot recover on its own, so the monitor hands it to failed-download recovery
    /// immediately instead of waiting out the stall grace window.
    /// </summary>
    bool IsFailed = false,
    /// <summary>The client's failure explanation when <see cref="IsFailed"/> is set, for the recovery record.</summary>
    string? FailureMessage = null);

/// <summary>Result of probing a download client connection.</summary>
public sealed record DownloadClientConnectionTest(bool Connected, string? Message);

/// <summary>One file within a download client item.</summary>
public sealed record DownloadItemFile(string Name, long SizeBytes, double Progress);

/// <summary>Live transfer telemetry for a download client item.</summary>
/// <param name="Ratio">Share ratio (uploaded/downloaded) for torrents; null for usenet or when unknown.</param>
/// <param name="SeedingTimeSeconds">How long the torrent has seeded; null for usenet or when unknown.</param>
public sealed record DownloadItemProperties(
    long TotalSizeBytes,
    double DownloadSpeedBytesPerSecond,
    double UploadSpeedBytesPerSecond,
    long EtaSeconds,
    int Seeds,
    int Peers,
    string? SavePath,
    double? Ratio = null,
    long? SeedingTimeSeconds = null);

/// <summary>Drives a download client. qBittorrent is the v1 implementation; the port stays client-agnostic.</summary>
public interface IDownloadClient {
    /// <summary>The download client family this implementation serves.</summary>
    DownloadClientKind Kind { get; }

    /// <summary>Ensures the category exists, adds the release, and returns the client item id used to track it (the info hash when known).</summary>
    Task<string> AddAsync(DownloadClientConnection connection, DownloadAddRequest request, CancellationToken cancellationToken);

    /// <summary>Adds a user-supplied .torrent file and returns the discovered client item id.</summary>
    Task<string> AddTorrentFileAsync(DownloadClientConnection connection, string fileName, byte[] torrent, CancellationToken cancellationToken);

    /// <summary>Reads the current status of a tracked item, or null when the client no longer has it.</summary>
    Task<DownloadItemStatus?> GetItemAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the status of every item in the connection's category. This is the authoritative presence
    /// check: a per-item lookup can momentarily miss a torrent (e.g. while it is fetching metadata), but a
    /// torrent absent from the full listing is genuinely gone from the client.
    /// </summary>
    Task<IReadOnlyList<DownloadItemStatus>> ListItemsAsync(DownloadClientConnection connection, CancellationToken cancellationToken);

    /// <summary>Lists the files within a tracked item.</summary>
    Task<IReadOnlyList<DownloadItemFile>> GetFilesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken);

    /// <summary>Reads live transfer telemetry for a tracked item, or null when the client no longer has it.</summary>
    Task<DownloadItemProperties?> GetPropertiesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken);

    /// <summary>Reads per-piece download state (0 = missing, 1 = downloading, 2 = downloaded).</summary>
    Task<byte[]> GetPieceStatesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken);

    /// <summary>Removes a tracked item, optionally deleting its downloaded data.</summary>
    Task RemoveAsync(DownloadClientConnection connection, string clientItemId, bool deleteData, CancellationToken cancellationToken);

    /// <summary>Probes the client for reachability and authentication.</summary>
    Task<DownloadClientConnectionTest> TestAsync(DownloadClientConnection connection, CancellationToken cancellationToken);
}

/// <summary>Resolves the configured <see cref="IDownloadClient"/> for a client family.</summary>
public interface IDownloadClientFactory {
    IDownloadClient Get(DownloadClientKind kind);
}

/// <summary>Command for creating or updating a download client configuration.</summary>
/// <param name="ApiKey">API key for clients that authenticate with one (SABnzbd); blank keeps the stored key.</param>
public sealed record DownloadClientSaveCommand(
    Guid? Id,
    DownloadClientKind Kind,
    string DisplayName,
    string BaseUrl,
    string? Username,
    string? Password,
    string Category,
    bool Enabled,
    string? ApiKey = null,
    int Priority = 25,
    double? SeedRatio = null,
    int? SeedTimeMinutes = null);

/// <summary>Persistence port for configured download clients.</summary>
public interface IDownloadClientConfigStore {
    Task<IReadOnlyList<DownloadClientSummary>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<DownloadClientDetail>> ListDetailsAsync(CancellationToken cancellationToken);
    Task<DownloadClientDetail?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Returns the default (first enabled) download client, or null when none is configured.</summary>
    Task<DownloadClientDetail?> GetDefaultAsync(CancellationToken cancellationToken);

    /// <summary>Returns the first enabled download client that speaks <paramref name="protocol"/>, or null when none does.</summary>
    Task<DownloadClientDetail?> GetDefaultAsync(DownloadProtocol protocol, CancellationToken cancellationToken);

    /// <summary>Every enabled download client that speaks <paramref name="protocol"/>, in selection order (priority, then age).</summary>
    Task<IReadOnlyList<DownloadClientDetail>> ListEnabledAsync(DownloadProtocol protocol, CancellationToken cancellationToken);

    /// <summary>The transfer protocols the enabled download clients collectively support.</summary>
    Task<IReadOnlyList<DownloadProtocol>> GetEnabledProtocolsAsync(CancellationToken cancellationToken);

    Task<DownloadClientSummary> SaveAsync(DownloadClientSaveCommand command, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
