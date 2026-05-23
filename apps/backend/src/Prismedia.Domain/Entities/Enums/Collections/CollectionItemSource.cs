namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of reasons an item appears inside a collection.
/// </summary>
public enum CollectionItemSource {
    /// <summary>The user explicitly added the item.</summary>
    [Code("manual")]
    Manual,

    /// <summary>A collection rule selected the item.</summary>
    [Code("dynamic")]
    Dynamic
}
