namespace Prismedia.Domain.Entities;

/// <summary>Authoritative executor state; absence from an active queue is never a successful state.</summary>
public enum RemoteJobState {
    /// <summary>Durably accepted and waiting to execute.</summary>
    [Code("queued")] Queued,
    /// <summary>Executing the accepted selection.</summary>
    [Code("running")] Running,
    /// <summary>Execution is durably waiting for a recoverable upstream condition.</summary>
    [Code("waiting")] Waiting,
    /// <summary>Some selected items failed or remain incomplete.</summary>
    [Code("partial")] Partial,
    /// <summary>Execution finished and a sealed output manifest is available.</summary>
    [Code("succeeded")] Succeeded,
    /// <summary>Execution failed.</summary>
    [Code("failed")] Failed,
    /// <summary>Execution stopped after cancellation.</summary>
    [Code("cancelled")] Cancelled
}
