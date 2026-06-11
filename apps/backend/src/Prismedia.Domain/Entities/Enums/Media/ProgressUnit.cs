namespace Prismedia.Domain.Entities;

/// <summary>
/// What a non-time progress cursor counts, stored on <c>EntityProgressRow.Unit</c> and carried
/// by the progress capability. Readers save <see cref="Page"/> (comics, PDFs) or <see cref="Cfi"/>
/// (EPUB locators); <see cref="Item"/> is the generic default for list-shaped media.
/// </summary>
public enum ProgressUnit {
    /// <summary>Generic list position (default).</summary>
    [Code("item")]
    Item,

    /// <summary>Zero-based page index with a meaningful page count.</summary>
    [Code("page")]
    Page,

    /// <summary>Chapter position within a chaptered work.</summary>
    [Code("chapter")]
    Chapter,

    /// <summary>Track position within an album or playlist.</summary>
    [Code("track")]
    Track,

    /// <summary>EPUB canonical fragment identifier; the index is approximate and the locator lives in Location.</summary>
    [Code("cfi")]
    Cfi
}
