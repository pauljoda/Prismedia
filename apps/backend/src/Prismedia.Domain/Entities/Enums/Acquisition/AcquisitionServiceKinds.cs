namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of indexer families Prismedia can search for releases: the Prowlarr
/// aggregator, or any individual indexer speaking the Torznab (torrent) / Newznab
/// (usenet) XML API directly — which also covers Jackett's per-indexer endpoints.
/// </summary>
public enum IndexerKind {
    /// <summary>Prowlarr indexer aggregator (JSON search API across all its indexers).</summary>
    [Code("prowlarr")]
    Prowlarr,

    /// <summary>Jackett indexer proxy. Configure its per-indexer Torznab endpoints as <see cref="Torznab"/> indexers instead.</summary>
    [Code("jackett")]
    Jackett,

    /// <summary>A torrent indexer speaking the Torznab XML API directly (native tracker endpoints, Jackett, per-indexer Prowlarr URLs).</summary>
    [Code("torznab")]
    Torznab,

    /// <summary>A usenet indexer speaking the Newznab XML API directly (DrunkenSlug, NZBgeek, …).</summary>
    [Code("newznab")]
    Newznab
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
