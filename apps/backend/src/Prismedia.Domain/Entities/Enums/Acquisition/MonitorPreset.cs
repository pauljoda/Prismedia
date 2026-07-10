namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of generic monitoring policies chosen when a container is requested (a series, an author,
/// an artist, or any future Entity grouping). A policy
/// decides two things:
/// <list type="number">
/// <item>Which works that already exist on the provider get requested/monitored at commit time — see
/// <c>MonitorPresetSelection.Resolve</c>, the single source of truth for that mapping.</item>
/// <item>Whether the container sync auto-monitors works discovered <em>later</em>. Only
/// <see cref="All"/> and <see cref="Future"/> send newly-discovered works through the same monitored
/// acquisition path as a direct child toggle; every other preset keeps monitoring the works committed up
/// front but ignores new arrivals.</item>
/// </list>
/// The preset is persisted on the container monitor so the second rule survives across syncs.
/// <para>
/// Medium-specific shortcuts do not belong here. The review's explicit child selection represents which
/// current works the user wants, while this policy only supplies generic current/future defaults and the
/// durable future-discovery choice.
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
    /// Request and monitor nothing, and do NOT auto-monitor works discovered later. The container monitor
    /// exists but is effectively idle until the user monitors works by hand. Sonarr's "none".
    /// </summary>
    [Code("none")]
    None
}
