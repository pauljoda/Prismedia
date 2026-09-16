namespace Prismedia.Domain.Entities;

/// <summary>Execution of a manager command, independently of downloaded files or local availability.</summary>
public enum ManagedCommandStatus {
    /// <summary>The manager accepted a command which has not started.</summary>
    [Code("pending")] Pending,
    /// <summary>The manager is executing the command.</summary>
    [Code("running")] Running,
    /// <summary>The command finished successfully; it may have found no releases.</summary>
    [Code("completed")] Completed,
    /// <summary>The command finished unsuccessfully.</summary>
    [Code("failed")] Failed,
    /// <summary>The manager cancelled or aborted execution.</summary>
    [Code("cancelled")] Cancelled,
    /// <summary>History is missing or the manager cannot establish an execution outcome.</summary>
    [Code("unknown")] Unknown
}

/// <summary>Definite outcomes of one manager mutation. Transport failures provide no such guarantee.</summary>
public enum ManagedMutationOutcome {
    /// <summary>The explicitly requested configuration was observed after applying or was already satisfied.</summary>
    [Code("applied")] Applied,
    /// <summary>A command was accepted with an exact remote identifier and matching scope.</summary>
    [Code("accepted")] Accepted,
    /// <summary>The adapter establishes that this invocation did not apply its requested mutation.</summary>
    [Code("rejected")] Rejected
}
