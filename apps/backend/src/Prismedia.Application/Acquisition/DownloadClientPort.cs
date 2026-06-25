using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Connection details a download client needs to act on a transfer.</summary>
public sealed record DownloadClientConnection(
    Guid Id,
    DownloadClientKind Kind,
    string BaseUrl,
    string? Username,
    string? Password,
    string Category);

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
    string? ContentPath);

/// <summary>Result of probing a download client connection.</summary>
public sealed record DownloadClientConnectionTest(bool Connected, string? Message);

/// <summary>Drives a download client. qBittorrent is the v1 implementation; the port stays client-agnostic.</summary>
public interface IDownloadClient {
    /// <summary>The download client family this implementation serves.</summary>
    DownloadClientKind Kind { get; }

    /// <summary>Ensures the category exists, adds the release, and returns the client item id used to track it (the info hash when known).</summary>
    Task<string> AddAsync(DownloadClientConnection connection, DownloadAddRequest request, CancellationToken cancellationToken);

    /// <summary>Reads the current status of a tracked item, or null when the client no longer has it.</summary>
    Task<DownloadItemStatus?> GetItemAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken);

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
public sealed record DownloadClientSaveCommand(
    Guid? Id,
    DownloadClientKind Kind,
    string DisplayName,
    string BaseUrl,
    string? Username,
    string? Password,
    string Category,
    bool Enabled);

/// <summary>Persistence port for configured download clients.</summary>
public interface IDownloadClientConfigStore {
    Task<IReadOnlyList<DownloadClientSummary>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<DownloadClientDetail>> ListDetailsAsync(CancellationToken cancellationToken);
    Task<DownloadClientDetail?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Returns the default (first enabled) download client, or null when none is configured.</summary>
    Task<DownloadClientDetail?> GetDefaultAsync(CancellationToken cancellationToken);

    Task<DownloadClientSummary> SaveAsync(DownloadClientSaveCommand command, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
