namespace Prismedia.Domain.Entities;

/// <summary>
/// Why a position could not be aligned to the other consumption modality, or why a Book keeps its
/// reading and listening separate.
/// </summary>
public enum AlignmentGapReason {
    /// <summary>No resumable position exists to align from.</summary>
    [Code("no_position")]
    NoPosition,

    /// <summary>The readable rendition has no chapter windows (a PDF or an unparsed EPUB), or there is none.</summary>
    [Code("readable_chapters_unavailable")]
    ReadableChaptersUnavailable,

    /// <summary>The reading position sits in a readable chapter with no paired audio.</summary>
    [Code("readable_chapter_unpaired")]
    ReadableChapterUnpaired,

    /// <summary>The listening position sits in an audio chapter with no paired readable chapter.</summary>
    [Code("audio_chapter_unpaired")]
    AudioChapterUnpaired,

    /// <summary>The position falls outside every known chapter window.</summary>
    [Code("position_outside_chapters")]
    PositionOutsideChapters,

    /// <summary>The Book has no playable audio.</summary>
    [Code("audio_unavailable")]
    AudioUnavailable,

    /// <summary>The audiobook is a single file without chapter markers, so it has no exact chapter boundaries.</summary>
    [Code("audio_unstructured")]
    AudioUnstructured,

    /// <summary>The audiobook files are parts, discs, or length splits rather than chapters.</summary>
    [Code("audio_in_parts")]
    AudioInParts,

    /// <summary>No readable chapter is paired with an audio chapter from exact evidence.</summary>
    [Code("no_exact_pairs")]
    NoExactPairs
}
