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
/// qBittorrent is the primary v1 target; additional clients share the same port.
/// </summary>
public enum DownloadClientKind {
    /// <summary>qBittorrent, driven through its Web API.</summary>
    [Code("qbittorrent")]
    QBittorrent,

    /// <summary>Transmission RPC client. Reserved for a later adapter.</summary>
    [Code("transmission")]
    Transmission
}

/// <summary>
/// Closed set of release transfer protocols. Prismedia v1 acquires over torrent only;
/// usenet releases are decoded so they can be recognized and rejected rather than mishandled.
/// </summary>
public enum DownloadProtocol {
    /// <summary>BitTorrent release (magnet link or .torrent file).</summary>
    [Code("torrent")]
    Torrent,

    /// <summary>Usenet (NZB) release. Not acquired in v1; rejected by the decision engine.</summary>
    [Code("usenet")]
    Usenet
}
