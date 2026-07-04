namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of events recorded in the durable acquisition activity log. Unlike the acquisition record
/// itself — which is hard-deleted when an acquisition is cancelled or removed — a history entry survives
/// its acquisition (the FK nulls out), so the log is a permanent grabbed/imported/failed/removed audit
/// trail for an item. Mirrors the Sonarr event kinds. The log is append-only: entries are never updated
/// or deleted through the application, only added.
/// </summary>
public enum AcquisitionHistoryEvent {
    /// <summary>A chosen release was sent to a download client and the acquisition began tracking it.</summary>
    [Code("grabbed")]
    Grabbed,

    /// <summary>The completed payload was imported into a library root.</summary>
    [Code("imported")]
    Imported,

    /// <summary>The completed payload could not be imported automatically (a hold or a failure during import).</summary>
    [Code("import-failed")]
    ImportFailed,

    /// <summary>The download failed or was removed from the client before completing.</summary>
    [Code("download-failed")]
    DownloadFailed,

    /// <summary>A release identity was placed on the acquisition blocklist so it is never re-grabbed.</summary>
    [Code("blocklisted")]
    Blocklisted,

    /// <summary>An owned copy was replaced by a higher-quality release through the upgrade loop.</summary>
    [Code("upgraded")]
    Upgraded,

    /// <summary>The acquisition was cancelled or deleted by the user (recorded before the row disappears).</summary>
    [Code("removed")]
    Removed
}
