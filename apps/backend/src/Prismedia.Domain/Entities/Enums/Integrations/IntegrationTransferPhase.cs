namespace Prismedia.Domain.Entities;

/// <summary>Locally persisted transfer progress, kept separate from remote execution and library availability.</summary>
public enum IntegrationTransferPhase {
    /// <summary>The durable intent has not been sent.</summary>
    [Code("pending-submission")] PendingSubmission,
    /// <summary>Submission may have reached the executor; operation lookup must reconcile it.</summary>
    [Code("submission-uncertain")] SubmissionUncertain,
    /// <summary>A stable remote job is accepted and being monitored.</summary>
    [Code("awaiting-remote")] AwaitingRemote,
    /// <summary>Remote execution finished; a complete manifest must still be read.</summary>
    [Code("awaiting-artifacts")] AwaitingArtifacts,
    /// <summary>Exact output bytes are being retrieved and verified.</summary>
    [Code("transferring")] Transferring,
    /// <summary>Verified bytes are being materialized in the local library.</summary>
    [Code("importing")] Importing,
    /// <summary>Local imports are committed; only a remote receipt remains.</summary>
    [Code("awaiting-acknowledgement")] AwaitingAcknowledgement,
    /// <summary>Local imports and the remote acknowledgement both completed.</summary>
    [Code("completed")] Completed,
    /// <summary>Incomplete or expired remote outputs require a user decision.</summary>
    [Code("needs-review")] NeedsReview,
    /// <summary>The executor failed before fulfillment completed.</summary>
    [Code("failed")] Failed,
    /// <summary>The executor confirms cancellation.</summary>
    [Code("cancelled")] Cancelled
}
