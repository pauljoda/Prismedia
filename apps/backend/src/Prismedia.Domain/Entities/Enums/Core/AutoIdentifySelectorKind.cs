namespace Prismedia.Domain.Entities;

/// <summary>
/// High-level media families users may enable for automatic identification. Concrete entity-kind
/// definitions opt into one family so every consumer derives eligibility from the kind itself.
/// </summary>
public enum AutoIdentifySelectorKind {
    /// <summary>Video files, movies, and series.</summary>
    [Code("video")]
    Video,

    /// <summary>Image galleries.</summary>
    [Code("gallery")]
    Gallery,

    /// <summary>Standalone images.</summary>
    [Code("image")]
    Image,

    /// <summary>Tracks, albums, and music artists.</summary>
    [Code("audio")]
    Audio,

    /// <summary>Books, comics, and other page-based media.</summary>
    [Code("book")]
    Book
}
