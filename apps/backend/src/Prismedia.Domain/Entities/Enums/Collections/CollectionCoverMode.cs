namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of collection cover generation strategies.
/// </summary>
public enum CollectionCoverMode {
    /// <summary>Cover generated from multiple collection items.</summary>
    [Code("mosaic")]
    Mosaic,

    /// <summary>Cover supplied by an uploaded or generated image path.</summary>
    [Code("custom")]
    Custom,

    /// <summary>Cover borrowed from a specific collection item.</summary>
    [Code("item")]
    Item
}
