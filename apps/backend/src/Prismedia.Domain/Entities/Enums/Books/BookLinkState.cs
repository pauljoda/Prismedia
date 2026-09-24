namespace Prismedia.Domain.Entities;

/// <summary>
/// Whether a Book's reading and listening move together. A Linked Book switches between formats inside
/// exactly paired chapters and keeps one progress; every other Book is Separate and keeps two.
/// </summary>
public enum BookLinkState {
    /// <summary>
    /// The audio has exact chapter boundaries and at least one readable chapter is paired from exact
    /// evidence, so switching and the shared progress follow the paired chapters.
    /// </summary>
    [Code("linked")]
    Linked,

    /// <summary>Reading and listening keep their own positions and progress; nothing converts one into the other.</summary>
    [Code("separate")]
    Separate
}
