namespace Prismedia.Domain.Entities;

/// <summary>
/// Ranked provenance/edition of a release or owned file, independent of its format. Higher is strictly
/// better. Detected from release-title tokens at grab time and persisted onto the book at import (it is NOT
/// inferable from the file bytes), so a later upgrade search can decide whether a candidate is a genuine
/// improvement. Because the source is parsed from an externally-supplied title, a source-tier gain alone is
/// never trusted to authorize a destructive file replacement (see the upgrade engine rules).
/// </summary>
public enum BookSourceTier {
    /// <summary>Unknown or undetected provenance — the floor.</summary>
    [Code("unknown")]
    Unknown = 0,

    /// <summary>Web/scraped/converted edition (e.g. "web", "webrip", "converted", "calibre").</summary>
    [Code("web")]
    Web = 10,

    /// <summary>Clean retail/official edition (e.g. "retail", "official").</summary>
    [Code("retail")]
    Retail = 20
}
