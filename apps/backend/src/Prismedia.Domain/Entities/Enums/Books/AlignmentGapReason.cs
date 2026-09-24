namespace Prismedia.Domain.Entities;

/// <summary>Why a position could not be aligned to the other consumption modality.</summary>
public enum AlignmentGapReason {
    /// <summary>No resumable position exists to align from.</summary>
    [Code("no_position")]
    NoPosition,

    /// <summary>The readable rendition has no chapter windows (a PDF or an unparsed EPUB).</summary>
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
    PositionOutsideChapters
}
