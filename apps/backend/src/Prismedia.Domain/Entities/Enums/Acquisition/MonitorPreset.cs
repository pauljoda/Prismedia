namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of monitoring presets chosen when a container is requested (a series, an author, an
/// artist), adapting Sonarr's <c>MonitorTypes</c> to Prismedia's phantom-acquisition model. A preset
/// decides two things:
/// <list type="number">
/// <item>Which works that already exist on the provider get requested/monitored at commit time — see
/// <c>MonitorPresetSelection.Resolve</c>, the single source of truth for that mapping.</item>
/// <item>Whether the container sync auto-monitors works discovered <em>later</em>. Only
/// <see cref="All"/> and <see cref="Future"/> materialize newly-discovered works as monitored wanted
/// phantoms; every other preset keeps monitoring the works committed up front but ignores new arrivals.</item>
/// </list>
/// The preset is persisted on the container monitor so the second rule survives across syncs.
/// <para>
/// DIVERGENCE FROM SONARR (deliberate): Sonarr's <c>Existing</c>, <c>Recent</c>, <c>MonitorSpecials</c>,
/// and <c>UnmonitorSpecials</c> variants are intentionally omitted. Prismedia acquires works it does not
/// yet own, so "monitor only what already exists on disk" (<c>Existing</c>) has no acquisition meaning;
/// specials handling and the 90-day <c>Recent</c> window are TV-metadata concerns that the phantom model
/// does not expose at commit time. These may be added if a concrete need appears.
/// </para>
/// </summary>
public enum MonitorPreset {
    /// <summary>
    /// Request and monitor every existing work now, and auto-monitor every work discovered later. The
    /// broadest preset: the container is fully mirrored and stays that way.
    /// </summary>
    [Code("all")]
    All,

    /// <summary>
    /// Request nothing now — establish the container watch only — and auto-monitor every work discovered
    /// later. Sonarr's "monitor future episodes": you start caring about what has not aired yet.
    /// </summary>
    [Code("future")]
    Future,

    /// <summary>
    /// Request and monitor every existing work not already in the library, and do NOT auto-monitor works
    /// discovered later. This mirrors Prismedia's default request behavior (fill the gaps that exist right
    /// now) without committing to future arrivals.
    /// </summary>
    [Code("missing")]
    Missing,

    /// <summary>
    /// Request and monitor only the first season/work (the lowest season number, or the earliest work),
    /// and do NOT auto-monitor works discovered later. Sonarr's "first season".
    /// </summary>
    [Code("first-season")]
    FirstSeason,

    /// <summary>
    /// Request and monitor only the latest season/work (the highest season number, or the most recent
    /// work), and do NOT auto-monitor works discovered later. Sonarr's "latest season".
    /// </summary>
    [Code("latest-season")]
    LatestSeason,

    /// <summary>
    /// Request and monitor only the pilot — the first episode of the first season (S01E01) — and do NOT
    /// auto-monitor works discovered later. Sonarr's "pilot".
    /// </summary>
    [Code("pilot")]
    Pilot,

    /// <summary>
    /// Request and monitor nothing, and do NOT auto-monitor works discovered later. The container monitor
    /// exists but is effectively idle until the user monitors works by hand. Sonarr's "none".
    /// </summary>
    [Code("none")]
    None
}
