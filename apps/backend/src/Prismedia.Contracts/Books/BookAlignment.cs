using Prismedia.Domain.Entities;
using Prismedia.Domain.Media.Books;

namespace Prismedia.Contracts.Books;

/// <summary>
/// Server-owned alignment between a Book's readable chapters and its audio chapter windows, with
/// the current user's exact positions and the resume, switch, and combined destinations derived
/// from them. Clients open the returned targets verbatim and never align positions themselves.
/// </summary>
/// <param name="Modalities">Consumption modalities the Book has content for.</param>
/// <param name="ReadablePositionTotal">Total that normalized readable positions (EPUB fractions) are expressed in.</param>
/// <param name="Rows">Alignment rows in display order; audio-only rows sit where their gap happens.</param>
/// <param name="Coverage">How much of the readable and audio content is paired.</param>
/// <param name="Resume">The current user's resume destinations, when the Book has any content to resume.</param>
public sealed record BookAlignmentResponse(
    IReadOnlyList<ConsumptionModality> Modalities,
    int ReadablePositionTotal,
    IReadOnlyList<BookAlignmentRow> Rows,
    BookAlignmentCoverage Coverage,
    BookResumeProjection? Resume);

/// <summary>One alignment row: a readable chapter, an audio chapter window, or a paired chapter.</summary>
/// <param name="RowId">Stable row identifier within the Book.</param>
/// <param name="Order">Zero-based display order.</param>
/// <param name="MatchState">Which sides the row carries.</param>
/// <param name="Provenance">Whether a user or the matcher paired the row, for paired rows.</param>
/// <param name="Readable">Readable chapter side, when present.</param>
/// <param name="Audio">Audio chapter side, when present.</param>
public sealed record BookAlignmentRow(
    string RowId,
    int Order,
    AlignmentMatchState MatchState,
    BookChapterMappingOrigin? Provenance,
    ReadableChapterWindow? Readable,
    AudioChapterWindow? Audio);

/// <summary>
/// Where the current user can resume a Book. Exact targets are the recorded positions; switch
/// targets align the other modality's position and are returned next to, never instead of, the
/// destination's exact position.
/// </summary>
/// <param name="LastModality">Modality of the newest checkpoint.</param>
/// <param name="CompletedAt">When the Book was finished, if it was.</param>
/// <param name="Continue">Exact target of the newest resumable checkpoint; null when finished or never started.</param>
/// <param name="ExactReading">Recorded reading position, when it is still worth resuming.</param>
/// <param name="ExactListening">Recorded listening position, when it is still worth resuming.</param>
/// <param name="SwitchToReading">Reading destination aligned from the listening position, or its gap.</param>
/// <param name="SwitchToListening">Listening destination aligned from the reading position, or its gap.</param>
/// <param name="Combined">Both sides anchored on the newest resumable position, or a fresh start.</param>
public sealed record BookResumeProjection(
    ConsumptionModality? LastModality,
    DateTimeOffset? CompletedAt,
    AlignedTarget? Continue,
    ReadingTarget? ExactReading,
    ListeningTarget? ExactListening,
    AlignedTarget SwitchToReading,
    AlignedTarget SwitchToListening,
    AlignedTarget Combined);
