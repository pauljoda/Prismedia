namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of page-based media categories supported by the core book model.
/// </summary>
public enum BookType {
    /// <summary>Default book-shaped item when no narrower category is known.</summary>
    [Code("book")]
    Book,

    /// <summary>Sequential art or comic archive content.</summary>
    [Code("comic")]
    Comic,

    /// <summary>Manga content, usually read with manga-specific ordering or layout affordances.</summary>
    [Code("manga")]
    Manga,

    /// <summary>Long-form prose content.</summary>
    [Code("novel")]
    Novel
}
