namespace Prismedia.Domain.Entities;

/// <summary>Durable external fulfillment progress; manager commands and locally readable bytes are separate facts.</summary>
public enum ManagedRequestPhase {
    /// <summary>No remote creation call has been dispatched.</summary>
    [Code("pending-creation")] PendingCreation,
    /// <summary>A creation may have happened; only exact identity observation can resolve it.</summary>
    [Code("creation-uncertain")] CreationUncertain,
    /// <summary>A remote holding exists; its requested files have not yet been bound locally.</summary>
    [Code("awaiting-files")] AwaitingFiles,
    /// <summary>The exact requested local identities own verified mapped files.</summary>
    [Code("completed")] Completed,
    /// <summary>The creation was definitely rejected; ownership remains until explicitly cancelled.</summary>
    [Code("rejected")] Rejected,
    /// <summary>Intent was cancelled before any possible remote creation effect.</summary>
    [Code("cancelled")] Cancelled,
    /// <summary>The associated holding completed an explicit ownership handoff.</summary>
    [Code("ownership-released")] OwnershipReleased,
}
