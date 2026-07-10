namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of lifecycle states for a <c>Monitor</c> — a standing intent that Prismedia keeps acting on.
/// A monitor is not one-and-done: while <see cref="Active"/> it is periodically re-searched until the wanted
/// item is acquired (<see cref="Fulfilled"/>), the user pauses it (<see cref="Paused"/>), managed file
/// deletion temporarily freezes it (<see cref="DeletingFiles"/>), or recursive unmonitor cleanup claims
/// it (<see cref="Stopping"/>).
/// </summary>
public enum MonitorStatus {
    /// <summary>Due for periodic re-search until the wanted item is acquired.</summary>
    [Code("active")]
    Active,

    /// <summary>Paused by the user, or auto-paused because the linked acquisition was hard-deleted.</summary>
    [Code("paused")]
    Paused,

    /// <summary>
    /// Managed Delete files has durably frozen this monitor while remote and on-disk state is reconciled.
    /// It retains reacquisition intent across partial failure but cannot search, pause, resume, or unmonitor
    /// until the same Delete files operation finishes and restores it to <see cref="Active"/>.
    /// </summary>
    [Code("deleting-files")]
    DeletingFiles,

    /// <summary>
    /// Recursive unmonitor cleanup has claimed this monitor. It is neither searchable nor resumable;
    /// retrying the stop operation completes any state left after an unexpected lifecycle race.
    /// </summary>
    [Code("stopping")]
    Stopping,

    /// <summary>The wanted item was acquired (the linked acquisition reached <see cref="AcquisitionStatus.Imported"/>); searching stops.</summary>
    [Code("fulfilled")]
    Fulfilled
}
