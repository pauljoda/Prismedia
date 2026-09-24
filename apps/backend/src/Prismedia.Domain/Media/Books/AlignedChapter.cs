using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media.Books;

/// <summary>
/// One row of a work's reading/listening alignment: a readable chapter, an audio chapter window,
/// or both when they are paired.
/// </summary>
/// <param name="RowId">Stable row identifier within the work.</param>
/// <param name="Order">Zero-based display order.</param>
/// <param name="MatchState">Which sides the row carries.</param>
/// <param name="Provenance">Origin of the pairing, for paired rows.</param>
/// <param name="Readable">Readable chapter side, when present.</param>
/// <param name="Audio">Audio chapter side, when present.</param>
public sealed record AlignedChapter(
    string RowId,
    int Order,
    AlignmentMatchState MatchState,
    BookChapterMappingOrigin? Provenance,
    ReadableChapterWindow? Readable,
    AudioChapterWindow? Audio) {
    #region Variables

    /// <summary>Whether both sides are present, so positions can move between them.</summary>
    public bool IsPaired => Readable is not null && Audio is not null;

    /// <summary>Whether a relative position survives moving between the two sides.</summary>
    public bool IsWindowedPair => IsPaired && Readable!.IsWindowed() && Audio!.IsWindowed();

    #endregion
}
