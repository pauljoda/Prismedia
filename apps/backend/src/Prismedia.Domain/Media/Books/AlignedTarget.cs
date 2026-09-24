using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media.Books;

/// <summary>
/// A resume or switch destination in a work's reading/listening alignment. When the anchor position
/// cannot be aligned, <paramref name="Gap"/> explains why and no derived side is returned; there is
/// never a silent fallback to another chapter.
/// </summary>
/// <param name="RowId">Alignment row holding the anchor position, when it could be located.</param>
/// <param name="Reading">Readable destination, when one applies.</param>
/// <param name="Listening">Audio destination, when one applies.</param>
/// <param name="Approximate">Whether a derived side is an estimate rather than a recorded position.</param>
/// <param name="Basis">How the derived side was computed; <see cref="AlignmentBasis.Exact"/> when nothing was derived.</param>
/// <param name="Gap">Why alignment was impossible, when it was.</param>
/// <param name="GapChapterTitle">Chapter at the heart of the gap, for the client's explanation.</param>
public sealed record AlignedTarget(
    string? RowId,
    ReadingTarget? Reading,
    ListeningTarget? Listening,
    bool Approximate,
    AlignmentBasis Basis,
    AlignmentGapReason? Gap,
    string? GapChapterTitle);
