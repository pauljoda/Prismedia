namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of reasons a release identity is placed on the acquisition blocklist. A blocklisted
/// release is refused by the decision engine on future searches (surfaced as
/// <see cref="ReleaseRejectionReason.Blocklisted"/>) so failed-download auto-recovery never re-grabs
/// the same bad release. The reason is retained for the blocklist management surface.
/// </summary>
public enum BlocklistReason {
    /// <summary>The download failed or was removed from the client before completing.</summary>
    [Code("failed")]
    Failed,

    /// <summary>The download stalled (no progress / no seeders) past the allowed window and was abandoned.</summary>
    [Code("stalled")]
    Stalled,

    /// <summary>The completed payload contained no importable book file (e.g. only an unsupported format).</summary>
    [Code("no-importable-files")]
    NoImportableFiles,

    /// <summary>The user manually blocklisted the release.</summary>
    [Code("manual")]
    Manual
}
