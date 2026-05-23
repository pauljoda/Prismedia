namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of queue job lifecycle statuses.
/// </summary>
public enum JobRunStatus {
    /// <summary>Job is waiting to be claimed by a worker.</summary>
    [Code("queued")]
    Queued,

    /// <summary>Job has been claimed and is currently running.</summary>
    [Code("running")]
    Running,

    /// <summary>Job finished successfully.</summary>
    [Code("completed")]
    Completed,

    /// <summary>Job exhausted retry rules or failed permanently.</summary>
    [Code("failed")]
    Failed,

    /// <summary>Job was cancelled before completion.</summary>
    [Code("cancelled")]
    Cancelled
}
