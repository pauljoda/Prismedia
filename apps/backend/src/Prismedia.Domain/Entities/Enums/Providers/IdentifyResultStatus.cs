namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of review states for provider identification results.
/// </summary>
public enum IdentifyResultStatus {
    /// <summary>Result is waiting for review or application.</summary>
    [Code("pending")]
    Pending,

    /// <summary>Result was applied to the entity.</summary>
    [Code("applied")]
    Applied,

    /// <summary>Result was rejected by the user or rules engine.</summary>
    [Code("rejected")]
    Rejected,

    /// <summary>Result could not be applied because provider data or persistence failed.</summary>
    [Code("failed")]
    Failed
}
