namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of reader layouts supported by the book reading surface.
/// </summary>
public enum ReaderMode {
    /// <summary>One page or spread at a time.</summary>
    [Code("paged")]
    Paged,

    /// <summary>Continuous vertical reading for long-strip comics and similar formats.</summary>
    [Code("webtoon")]
    Webtoon,

    /// <summary>Continuous scrolled flow used by the EPUB and PDF reading surfaces.</summary>
    [Code("scrolled")]
    Scrolled
}
