using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media.Books;

public sealed partial class WorkAlignment {
    #region Actions - Exact Targets

    /// <summary>The recorded reading position, annotated with the chapter that holds it.</summary>
    /// <exception cref="ArgumentException">The checkpoint is offset-addressed (listening).</exception>
    public ReadingTarget ExactReading(ProgressCheckpoint reading) {
        if (reading.Definition.AddressesByOffset) {
            throw new ArgumentException("An exact reading target needs a reading checkpoint.", nameof(reading));
        }

        var anchor = LocateReading(reading);
        var chapter = anchor.Row?.Readable;
        return new ReadingTarget(
            reading.PositionEntityId,
            reading.Unit,
            reading.Index,
            reading.Total,
            reading.Location,
            chapter?.ChapterKey,
            chapter?.Location,
            chapter is null ? null : anchor.Position,
            chapter?.ChapterEntityId is null ? null : reading.Index,
            reading.Mode);
    }

    /// <summary>The recorded listening position on its physical track.</summary>
    /// <exception cref="ArgumentException">The checkpoint is not offset-addressed.</exception>
    public ListeningTarget ExactListening(ProgressCheckpoint listening) {
        if (!listening.Definition.AddressesByOffset) {
            throw new ArgumentException("An exact listening target needs a listening checkpoint.", nameof(listening));
        }

        var anchor = LocateListening(listening);
        return new ListeningTarget(
            listening.PositionEntityId,
            listening.MarkerId ?? anchor.Row?.Audio?.MarkerId,
            listening.OffsetSeconds ?? listening.Index);
    }

    /// <summary>Resume target of <paramref name="checkpoint"/>: its own exact position.</summary>
    public AlignedTarget Continue(ProgressCheckpoint checkpoint) {
        var anchor = Locate(checkpoint);
        return checkpoint.Definition.AddressesByOffset
            ? new AlignedTarget(anchor.Row?.RowId, null, ExactListening(checkpoint), false, AlignmentBasis.Exact, null, null)
            : new AlignedTarget(anchor.Row?.RowId, ExactReading(checkpoint), null, false, AlignmentBasis.Exact, null, null);
    }

    #endregion

    #region Actions - Aligned Targets

    /// <summary>
    /// Destination on the other side of the alignment for <paramref name="source"/>: the same relative
    /// position inside the paired chapter (with the listening runway), the chapter start when either
    /// window is unwindowed, or a gap. Only the derived side is returned.
    /// </summary>
    /// <param name="source">Resumable checkpoint to switch from, or null when there is none.</param>
    /// <param name="readerMode">Reader layout to open aligned reading positions with.</param>
    public AlignedTarget Switch(ProgressCheckpoint? source, ReaderMode? readerMode) {
        if (source is null) {
            return Gap(null, AlignmentGapReason.NoPosition, null);
        }

        var anchor = Locate(source);
        if (anchor.Row is not { } row) {
            return Gap(null, anchor.Gap ?? AlignmentGapReason.PositionOutsideChapters, anchor.GapChapterTitle);
        }

        var fromAudio = source.Definition.AddressesByOffset;
        if (!row.IsPaired) {
            return fromAudio
                ? Gap(row.RowId, AlignmentGapReason.AudioChapterUnpaired, row.Audio?.Title)
                : Gap(row.RowId, AlignmentGapReason.ReadableChapterUnpaired, row.Readable?.Title);
        }

        var position = row.IsWindowedPair ? anchor.Position : 0;
        var basis = position > 0 ? AlignmentBasis.Interpolated : AlignmentBasis.ChapterStart;
        var approximate = position > 0 || !row.IsWindowedPair;
        return fromAudio
            ? new AlignedTarget(row.RowId, row.Readable!.TargetAt(WorkId, position, readerMode), null, approximate, basis, null, null)
            : new AlignedTarget(row.RowId, null, row.Audio!.TargetAt(position), approximate, basis, null, null);
    }

    /// <summary>
    /// Target for reading and listening together, anchored on <paramref name="anchor"/>'s exact
    /// position with the other side aligned to it. Without a resumable position both sides start at
    /// the first paired chapter (<see cref="AlignmentBasis.FreshStart"/>); that is the only time the
    /// first paired chapter is chosen.
    /// </summary>
    public AlignedTarget Combined(ProgressCheckpoint? anchor, ReaderMode? readerMode) {
        if (anchor is null) {
            return FreshStart(readerMode);
        }

        var aligned = Switch(anchor, readerMode);
        return anchor.Definition.AddressesByOffset
            ? aligned with { Listening = ExactListening(anchor) }
            : aligned with { Reading = ExactReading(anchor) };
    }

    /// <summary>Both sides at the start of the first paired chapter, or a gap when nothing is paired.</summary>
    public AlignedTarget FreshStart(ReaderMode? readerMode) {
        var first = Rows.FirstOrDefault(row => row.IsPaired);
        if (first is null) {
            var firstReadable = Rows.FirstOrDefault(row => row.Readable is not null);
            return firstReadable is null
                ? Gap(null, AlignmentGapReason.ReadableChaptersUnavailable, null)
                : Gap(null, AlignmentGapReason.ReadableChapterUnpaired, firstReadable.Readable!.Title);
        }

        return new AlignedTarget(
            first.RowId,
            first.Readable!.TargetAt(WorkId, 0, readerMode),
            new ListeningTarget(first.Audio!.TrackEntityId, first.Audio.MarkerId, first.Audio.StartSeconds),
            false,
            AlignmentBasis.FreshStart,
            null,
            null);
    }

    private static AlignedTarget Gap(string? rowId, AlignmentGapReason reason, string? chapterTitle) =>
        new(rowId, null, null, false, AlignmentBasis.Exact, reason, chapterTitle);

    #endregion

    #region Actions - Cursor Placement

    /// <summary>
    /// Where a listening checkpoint places the work's shared cursor: whole-work seconds for a work
    /// without a readable rendition, the aligned readable cursor inside a paired chapter with known
    /// readable bounds, or nothing (leave the cursor alone) for unpaired or unlocatable audio, so the
    /// cursor keeps one unit for thumbnails and in-progress filters.
    /// </summary>
    /// <exception cref="ArgumentException">The checkpoint is not offset-addressed.</exception>
    public WorkCursorPlacement? PlaceCursor(ProgressCheckpoint listening) {
        if (!listening.Definition.AddressesByOffset) {
            throw new ArgumentException("Only a listening checkpoint places the cursor from audio.", nameof(listening));
        }

        if (!HasReadableRendition) {
            return CumulativePlacement(listening);
        }

        var anchor = LocateListening(listening);
        if (anchor.Row is not { IsPaired: true } row || !row.Readable!.IsWindowed()) {
            return null;
        }

        var target = row.Readable.TargetAt(WorkId, row.IsWindowedPair ? anchor.Position : 0, mode: null);
        return new WorkCursorPlacement(target.PositionEntityId, target.Unit, target.Index, target.Total);
    }

    private WorkCursorPlacement? CumulativePlacement(ProgressCheckpoint listening) {
        var elapsed = 0d;
        var found = false;
        foreach (var track in _tracks) {
            if (track.TrackEntityId == listening.PositionEntityId) {
                found = true;
                break;
            }
            elapsed += track.KnownDuration() ?? 0;
        }
        if (!found) {
            return null;
        }

        var index = (int)Math.Min(int.MaxValue - 1, Math.Floor(elapsed + (listening.OffsetSeconds ?? listening.Index)));
        var total = (int)Math.Min(int.MaxValue - 1, Math.Ceiling(_tracks.Sum(track => track.KnownDuration() ?? 0)));
        return new WorkCursorPlacement(WorkId, listening.Unit, index, Math.Max(index, total));
    }

    #endregion

    #region Actions - Location

    /// <summary>Whether <paramref name="markerId"/> still identifies an audio chapter window on the track.</summary>
    public bool HasAudioWindow(Guid trackEntityId, Guid markerId) =>
        Rows.Any(row => row.Audio?.Identifies(trackEntityId, markerId) == true);

    private AnchoredPosition Locate(ProgressCheckpoint checkpoint) =>
        checkpoint.Definition.AddressesByOffset ? LocateListening(checkpoint) : LocateReading(checkpoint);

    /// <summary>
    /// A paged checkpoint belongs to the chapter it names. An EPUB checkpoint's whole-work fraction
    /// belongs to the containing chapter with the greatest start, then the greatest depth, then the
    /// later order, so a shared boundary opens the later chapter.
    /// </summary>
    private AnchoredPosition LocateReading(ProgressCheckpoint reading) {
        var readableRows = Rows.Where(row => row.Readable is not null).ToArray();
        if (readableRows.Length == 0) {
            return AnchoredPosition.Missing(AlignmentGapReason.ReadableChaptersUnavailable);
        }

        var chapterRow = readableRows.FirstOrDefault(row => row.Readable!.ChapterEntityId == reading.PositionEntityId);
        if (chapterRow is not null) {
            return new AnchoredPosition(chapterRow, chapterRow.Readable!.PositionOf(reading), null, null);
        }
        if (reading.Total <= 0) {
            return AnchoredPosition.Missing(AlignmentGapReason.PositionOutsideChapters);
        }

        var fraction = reading.Index / (double)reading.Total;
        var holder = readableRows
            .Where(row => row.Readable!.ContainsFraction(fraction))
            .OrderByDescending(row => row.Readable!.StartFraction)
            .ThenByDescending(row => row.Readable!.Depth)
            .ThenByDescending(row => row.Order)
            .FirstOrDefault();
        return holder is null
            ? AnchoredPosition.Missing(AlignmentGapReason.PositionOutsideChapters)
            : new AnchoredPosition(holder, holder.Readable!.PositionOf(reading), null, null);
    }

    /// <summary>
    /// A still-valid marker names its window. Otherwise the offset belongs to the window on its track
    /// with the greatest start where <c>start ≤ t &lt; end</c>; at or past the track's duration it
    /// belongs to the track's last window.
    /// </summary>
    private AnchoredPosition LocateListening(ProgressCheckpoint listening) {
        var trackRows = Rows.Where(row => row.Audio?.TrackEntityId == listening.PositionEntityId).ToArray();
        if (trackRows.Length == 0) {
            return AnchoredPosition.Missing(AlignmentGapReason.NoPosition);
        }

        var offset = listening.OffsetSeconds ?? listening.Index;
        var holder = listening.MarkerId is { } markerId
            ? trackRows.FirstOrDefault(row => row.Audio!.MarkerId == markerId)
            : null;
        var duration = _tracks.FirstOrDefault(track => track.TrackEntityId == listening.PositionEntityId)?.KnownDuration();
        if (holder is null && duration is { } knownDuration && offset >= knownDuration) {
            holder = trackRows.MaxBy(row => row.Audio!.StartSeconds);
        }
        holder ??= trackRows
            .Where(row => row.Audio!.Contains(offset))
            .MaxBy(row => row.Audio!.StartSeconds);
        return holder is null
            ? AnchoredPosition.Missing(AlignmentGapReason.PositionOutsideChapters)
            : new AnchoredPosition(holder, holder.Audio!.PositionOf(offset), null, null);
    }

    /// <summary>A checkpoint located in an alignment row, or the reason it could not be.</summary>
    private sealed record AnchoredPosition(
        AlignedChapter? Row,
        double Position,
        AlignmentGapReason? Gap,
        string? GapChapterTitle) {
        public static AnchoredPosition Missing(AlignmentGapReason gap) => new(null, 0, gap, null);
    }

    #endregion
}
