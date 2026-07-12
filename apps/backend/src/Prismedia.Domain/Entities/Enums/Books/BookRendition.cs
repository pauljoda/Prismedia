namespace Prismedia.Domain.Entities;

/// <summary>
/// Independently ownable renditions of one canonical book work. A Book Entity may carry either or both;
/// acquisition, monitoring, and completion are scoped to one rendition so neither replaces the other.
/// </summary>
public enum BookRendition {
    /// <summary>Page/text payload such as EPUB, PDF, CBZ, or ZIP.</summary>
    [Code("ebook")]
    Ebook,

    /// <summary>Spoken-word audio payload such as M4B, M4A, or MP3.</summary>
    [Code("audiobook")]
    Audiobook
}
