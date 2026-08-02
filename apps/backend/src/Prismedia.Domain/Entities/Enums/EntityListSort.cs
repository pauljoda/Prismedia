namespace Prismedia.Domain.Entities;

/// <summary>Canonical server-side Entity list ordering strategies.</summary>
public enum EntityListSort {
    /// <summary>Alphabetical sort-name ordering.</summary>
    [Code("title")]
    Title,

    /// <summary>Entity creation time.</summary>
    [Code("date-added")]
    DateAdded,

    /// <summary>Current user's rating.</summary>
    [Code("rating")]
    Rating,

    /// <summary>Stable seeded shuffle.</summary>
    [Code("random")]
    Random,

    /// <summary>Current user's most recent position, activity, or progress signal.</summary>
    [Code("last-active")]
    LastActive,

    /// <summary>Inbound relationship reference count.</summary>
    [Code("references")]
    References
}
