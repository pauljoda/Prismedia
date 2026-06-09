namespace Prismedia.Domain.Entities;

/// <summary>
/// Lifecycle state of a live Identify proposal apply operation, polled by the review screen while a
/// reviewed proposal (and its child tree) is being written.
/// </summary>
public enum IdentifyApplyState {
    /// <summary>The apply is in progress.</summary>
    [Code("running")]
    Running,

    /// <summary>The apply completed successfully.</summary>
    [Code("succeeded")]
    Succeeded,

    /// <summary>The apply failed; an error message accompanies the snapshot.</summary>
    [Code("failed")]
    Failed
}
