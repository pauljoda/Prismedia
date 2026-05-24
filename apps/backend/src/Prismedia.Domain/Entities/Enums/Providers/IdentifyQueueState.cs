namespace Prismedia.Domain.Entities;

/// <summary>
/// Durable review state for an entity waiting in the identify queue.
/// </summary>
public enum IdentifyQueueState {
    /// <summary>The item is waiting for provider selection, search, or user candidate selection.</summary>
    [Code("search")]
    Search,

    /// <summary>The item has a fully hydrated proposal waiting for user review.</summary>
    [Code("proposal")]
    Proposal,

    /// <summary>The proposal was accepted and applied to the entity.</summary>
    [Code("done")]
    Done,

    /// <summary>The user removed the item from the active identify queue.</summary>
    [Code("deleted")]
    Deleted,

    /// <summary>The latest provider attempt failed and can be retried.</summary>
    [Code("error")]
    Error
}
