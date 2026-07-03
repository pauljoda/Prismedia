namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of indexer aggregator families Prismedia can search for releases.
/// Prowlarr is the primary v1 target; the adapter shape is Torznab-compatible so
/// Jackett can follow without changing the contract.
/// </summary>
public enum IndexerKind {
    /// <summary>Prowlarr indexer aggregator (Torznab-compatible search API).</summary>
    [Code("prowlarr")]
    Prowlarr,

    /// <summary>Jackett indexer proxy (Torznab/TorrentPotato results). Reserved for a later adapter.</summary>
    [Code("jackett")]
    Jackett
}

/// <summary>
/// Closed set of download client families Prismedia can hand a release to.
/// qBittorrent is the primary torrent client and SABnzbd the usenet client;
/// additional clients share the same port.
/// </summary>
public enum DownloadClientKind {
    /// <summary>qBittorrent, driven through its Web API.</summary>
    [Code("qbittorrent")]
    QBittorrent,

    /// <summary>Transmission RPC client. Reserved for a later adapter.</summary>
    [Code("transmission")]
    Transmission,

    /// <summary>SABnzbd usenet client, driven through its JSON API.</summary>
    [Code("sabnzbd")]
    Sabnzbd
}

/// <summary>
/// Closed set of release transfer protocols. A protocol is acquirable when an enabled
/// download client supports it; releases over unsupported protocols are decoded so they
/// can be recognized and rejected rather than mishandled.
/// </summary>
public enum DownloadProtocol {
    /// <summary>BitTorrent release (magnet link or .torrent file).</summary>
    [Code("torrent")]
    Torrent,

    /// <summary>Usenet (NZB) release, acquired through a usenet client such as SABnzbd.</summary>
    [Code("usenet")]
    Usenet
}

/// <summary>Protocol capabilities of the download client families.</summary>
public static class DownloadClientKindProtocol {
    /// <summary>The transfer protocol a download client family speaks.</summary>
    public static DownloadProtocol Protocol(this DownloadClientKind kind) => kind switch {
        DownloadClientKind.QBittorrent or DownloadClientKind.Transmission => DownloadProtocol.Torrent,
        DownloadClientKind.Sabnzbd => DownloadProtocol.Usenet,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped download client kind.")
    };
}
