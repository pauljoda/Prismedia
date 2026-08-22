namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of prose-book categories supported by the core book model.
/// </summary>
public enum BookType {
    /// <summary>Default book-shaped item when no narrower category is known.</summary>
    [Code("book")]
    Book,

    /// <summary>Long-form prose content.</summary>
    [Code("novel")]
    Novel
}
