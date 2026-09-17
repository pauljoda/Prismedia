namespace Prismedia.Domain.Entities;

/// <summary>Health of a retained association with an externally managed holding.</summary>
public enum ManagedTrackingStatus {
    /// <summary>Explicit local associations are awaiting worker verification.</summary>
    [Code("pending")] Pending,
    /// <summary>Owned wanted targets exist independently of their first locally readable source files.</summary>
    [Code("waiting-for-files")] WaitingForFiles,
    /// <summary>The most recent observation reconciled the established scope.</summary>
    [Code("tracking")] Tracking,
    /// <summary>Identity, coverage, or source ownership needs an explicit decision.</summary>
    [Code("needs-review")] NeedsReview,
    /// <summary>The connected service could not be observed; previous evidence is retained.</summary>
    [Code("stale")] Stale,
    /// <summary>Host actions are frozen while a fresh remote drain observation is pending.</summary>
    [Code("release-pending")] ReleasePending,
    /// <summary>Acquisition ownership and active source associations were explicitly released; files and history remain.</summary>
    [Code("released")] Released
}
