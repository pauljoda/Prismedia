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
/// <param name="Link">
/// Whether reading and listening are Linked or Separate, why, and each format's own progress. Clients
/// show one progress and switching actions only for a Linked Book; a Separate Book shows two meters.
/// </param>
public sealed record BookAlignmentResponse(
    IReadOnlyList<ConsumptionModality> Modalities,
    int ReadablePositionTotal,
    IReadOnlyList<BookAlignmentRow> Rows,
    BookAlignmentCoverage Coverage,
    BookResumeProjection? Resume,
    BookLinkProjection Link);

/// <summary>
/// How a Book's reading and listening relate, and the current user's progress in each format measured
/// only from that format's own exact position.
/// </summary>
/// <param name="State">Linked when the audio has exact chapter boundaries and at least one chapter pair comes from exact evidence; Separate otherwise.</param>
/// <param name="Reason">Why the Book is Separate; null when Linked.</param>
/// <param name="AudioStructure">How the audio divides into chapters, or null without playable audio.</param>
/// <param name="ReadingPercent">Share (0..1) of the readable rendition before the reading position, when there is one.</param>
/// <param name="ListeningPercent">Share (0..1) of the known audio listened before the listening position, when there is one.</param>
public sealed record BookLinkProjection(
    BookLinkState State,
    AlignmentGapReason? Reason,
    AudiobookStructure? AudioStructure,
    double? ReadingPercent,
    double? ListeningPercent);

/// <summary>One alignment row: a readable chapter, an audio chapter window, or a paired chapter.</summary>
/// <param name="RowId">Stable row identifier within the Book.</param>
/// <param name="Order">Zero-based display order.</param>
/// <param name="MatchState">Which sides the row carries.</param>
/// <param name="Provenance">Whether a person (by hand or a reviewed in-order fill) or the matcher paired the row, for paired rows.</param>
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
