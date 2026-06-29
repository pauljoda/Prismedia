namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of lifecycle states for a <c>Monitor</c> — a standing intent that Prismedia keeps acting on.
/// A monitor is not one-and-done: while <see cref="Active"/> it is periodically re-searched until the wanted
/// item is acquired (<see cref="Fulfilled"/>) or the user pauses it (<see cref="Paused"/>).
/// </summary>
public enum MonitorStatus {
    /// <summary>Due for periodic re-search until the wanted item is acquired.</summary>
    [Code("active")]
    Active,

    /// <summary>Paused by the user, or auto-paused because the linked acquisition was hard-deleted.</summary>
    [Code("paused")]
    Paused,

    /// <summary>The wanted item was acquired (the linked acquisition reached <see cref="AcquisitionStatus.Imported"/>); searching stops.</summary>
    [Code("fulfilled")]
    Fulfilled
}
