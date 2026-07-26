namespace Prismedia.Domain.Entities;

/// <summary>
/// Durable review state for an entity waiting in the identify queue.
/// </summary>
public enum IdentifyQueueState {
    /// <summary>A provider search returned candidates that are waiting for the user's choice.</summary>
    [Code("search")]
    Search,

    /// <summary>A search was requested; a background identify-search job will run it.</summary>
    [Code("queued")]
    Queued,

    /// <summary>An identify-search job is actively searching providers for this item.</summary>
    [Code("searching")]
    Searching,

    /// <summary>The item has a fully hydrated proposal waiting for user review.</summary>
    [Code("proposal")]
    Proposal,

    /// <summary>The reviewed proposal is queued or running in its interactive graph.</summary>
    [Code("applying")]
    Applying,

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
