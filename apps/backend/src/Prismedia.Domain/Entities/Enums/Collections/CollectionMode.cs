namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of collection population strategies.
/// </summary>
public enum CollectionMode {
    /// <summary>User-managed collection membership.</summary>
    [Code("manual")]
    Manual,

    /// <summary>Collection membership produced from stored rules.</summary>
    [Code("dynamic")]
    Dynamic,

    /// <summary>Collection combines manually pinned items with rule-produced items.</summary>
    [Code("hybrid")]
    Hybrid
}
