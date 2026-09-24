namespace Prismedia.Domain.Entities;

/// <summary>How one row of a work's reading/listening alignment pairs its two sides.</summary>
public enum AlignmentMatchState {
    /// <summary>A readable chapter paired with one audio chapter window.</summary>
    [Code("paired")]
    Paired,

    /// <summary>A readable chapter with no paired audio.</summary>
    [Code("readable_only")]
    ReadableOnly,

    /// <summary>An audio chapter window with no paired readable chapter.</summary>
    [Code("audio_only")]
    AudioOnly
}
