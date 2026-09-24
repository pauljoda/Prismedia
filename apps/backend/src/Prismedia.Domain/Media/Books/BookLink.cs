using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media.Books;

/// <summary>
/// Whether a Book's reading and listening are Linked, and why not when they are Separate. A Book is
/// Linked only when its audio has exact chapter boundaries and at least one readable chapter is paired
/// from exact evidence; nothing is ever guessed, so every other Book keeps two separate progresses.
/// </summary>
/// <param name="State">Linked or Separate.</param>
/// <param name="Reason">Why the Book is Separate; null when Linked.</param>
public sealed record BookLink(BookLinkState State, AlignmentGapReason? Reason) {
    #region Static Variables

    /// <summary>Reading and listening move together inside exactly paired chapters.</summary>
    public static readonly BookLink Linked = new(BookLinkState.Linked, null);

    #endregion

    #region Variables

    /// <summary>Whether switching and one shared progress follow the paired chapters.</summary>
    public bool IsLinked => State == BookLinkState.Linked;

    #endregion

    #region Actions - Decision

    /// <summary>A Separate link explained by <paramref name="reason"/>.</summary>
    public static BookLink Separate(AlignmentGapReason reason) => new(BookLinkState.Separate, reason);

    /// <summary>
    /// Decides the link from exact facts only: audio must exist and have a structure with exact chapter
    /// boundaries, the readable rendition must have chapters, and at least one pair must come from exact
    /// evidence (a person's pick, a reviewed in-order fill, or an exact title match with numbers kept).
    /// The first missing fact names the reason.
    /// </summary>
    /// <param name="structure">Structure of the audio rendition, or null without playable audio.</param>
    /// <param name="hasReadableChapters">Whether the readable rendition exposes chapter windows.</param>
    /// <param name="hasExactPair">Whether at least one readable chapter is paired from exact evidence.</param>
    public static BookLink Decide(
        AudiobookStructureDefinition? structure,
        bool hasReadableChapters,
        bool hasExactPair) =>
        structure is null ? Separate(AlignmentGapReason.AudioUnavailable)
        : structure.SeparateReason is { } structuralReason ? Separate(structuralReason)
        : !hasReadableChapters ? Separate(AlignmentGapReason.ReadableChaptersUnavailable)
        : !hasExactPair ? Separate(AlignmentGapReason.NoExactPairs)
        : Linked;

    #endregion
}
