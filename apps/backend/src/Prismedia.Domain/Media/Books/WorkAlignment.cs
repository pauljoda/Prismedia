using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media.Books;

/// <summary>
/// The alignment between a work's readable chapters and its audio chapter windows, built from the
/// persisted chapter pairings. It orders the rows so gaps show where they happen, measures coverage,
/// locates exact reading and listening checkpoints, and derives resume, switch, and combined
/// destinations by carrying the relative position inside a paired chapter across to the other side.
/// It never falls back to another chapter: a position that cannot be aligned reports a gap.
/// </summary>
public sealed partial class WorkAlignment {
    #region Variables

    private readonly IReadOnlyList<AudioTrackSpan> _tracks;

    /// <summary>Alignment rows in display order.</summary>
    public IReadOnlyList<AlignedChapter> Rows { get; }

    /// <summary>How much of the readable and audio content is paired.</summary>
    public BookAlignmentCoverage Coverage { get; }

    /// <summary>Work Entity addressed by whole-work positions.</summary>
    public Guid WorkId { get; }

    /// <summary>Whether the work has a readable rendition, even one without chapter windows (a PDF).</summary>
    public bool HasReadableRendition { get; }

    /// <summary>Whether the work has playable audio tracks.</summary>
    public bool HasAudio => _tracks.Count > 0;

    #endregion

    #region Constructors

    /// <summary>Builds the alignment for one work.</summary>
    /// <param name="workId">Work Entity addressed by whole-work positions.</param>
    /// <param name="hasReadableRendition">Whether the work has a readable rendition.</param>
    /// <param name="readableChapters">Readable chapters in display order.</param>
    /// <param name="tracks">Playable audio tracks in playback order.</param>
    /// <param name="audioWindows">Audio chapter windows in playback order.</param>
    /// <param name="pairings">
    /// Persisted chapter pairings. Manual pairs win; pairs naming a missing chapter or window, or an
    /// already paired side, are ignored.
    /// </param>
    public WorkAlignment(
        Guid workId,
        bool hasReadableRendition,
        IReadOnlyList<ReadableChapterWindow> readableChapters,
        IReadOnlyList<AudioTrackSpan> tracks,
        IReadOnlyList<AudioChapterWindow> audioWindows,
        IReadOnlyList<ChapterPairing> pairings) {
        WorkId = workId;
        HasReadableRendition = hasReadableRendition || readableChapters.Count > 0;
        _tracks = tracks;
        Rows = BuildRows(readableChapters, audioWindows, pairings);
        Coverage = MeasureCoverage(readableChapters, audioWindows.Count);
    }

    #endregion

    #region Actions - Rows

    private static IReadOnlyList<AlignedChapter> BuildRows(
        IReadOnlyList<ReadableChapterWindow> readableChapters,
        IReadOnlyList<AudioChapterWindow> audioWindows,
        IReadOnlyList<ChapterPairing> pairings) {
        var readableKeys = readableChapters.Select(chapter => chapter.ChapterKey).ToHashSet(StringComparer.Ordinal);
        var audioIndexById = audioWindows
            .Select((window, index) => (Window: window, Index: index))
            .GroupBy(entry => (entry.Window.TrackEntityId, entry.Window.MarkerId))
            .ToDictionary(group => group.Key, group => group.First().Index);

        // Origins are declared manual-first, so user-chosen pairs claim their chapters before the
        // matcher's automatic pairs.
        var pairByKey = new Dictionary<string, (int AudioIndex, BookChapterMappingOrigin Origin)>(StringComparer.Ordinal);
        var pairedAudio = new SortedSet<int>();
        foreach (var pairing in pairings.OrderBy(pairing => pairing.Origin)) {
            if (!readableKeys.Contains(pairing.ChapterKey) ||
                pairByKey.ContainsKey(pairing.ChapterKey) ||
                !audioIndexById.TryGetValue((pairing.TrackEntityId, pairing.MarkerId), out var audioIndex) ||
                !pairedAudio.Add(audioIndex)) {
                continue;
            }
            pairByKey[pairing.ChapterKey] = (audioIndex, pairing.Origin);
        }

        // Each unpaired audio window follows the row holding the nearest earlier paired window, so a
        // gap in the audio shows where it happens. Windows before the first pair lead; without any
        // pair they trail the readable chapters.
        var leading = new List<int>();
        var trailing = new List<int>();
        var following = new Dictionary<int, List<int>>();
        for (var index = 0; index < audioWindows.Count; index++) {
            if (pairedAudio.Contains(index)) {
                continue;
            }
            var anchor = pairedAudio.GetViewBetween(int.MinValue, index - 1);
            if (anchor.Count > 0) {
                var anchorIndex = anchor.Max;
                if (!following.TryGetValue(anchorIndex, out var list)) {
                    list = [];
                    following[anchorIndex] = list;
                }
                list.Add(index);
            } else if (pairedAudio.Count > 0) {
                leading.Add(index);
            } else {
                trailing.Add(index);
            }
        }

        var rows = new List<AlignedChapter>(readableChapters.Count + audioWindows.Count);
        foreach (var audioIndex in leading) {
            rows.Add(AudioOnlyRow(audioWindows[audioIndex], rows.Count));
        }
        foreach (var chapter in readableChapters) {
            if (pairByKey.TryGetValue(chapter.ChapterKey, out var pair)) {
                rows.Add(new AlignedChapter(
                    ReadableRowId(chapter),
                    rows.Count,
                    AlignmentMatchState.Paired,
                    pair.Origin,
                    chapter,
                    audioWindows[pair.AudioIndex]));
                foreach (var audioIndex in following.GetValueOrDefault(pair.AudioIndex) ?? []) {
                    rows.Add(AudioOnlyRow(audioWindows[audioIndex], rows.Count));
                }
            } else {
                rows.Add(new AlignedChapter(
                    ReadableRowId(chapter),
                    rows.Count,
                    AlignmentMatchState.ReadableOnly,
                    null,
                    chapter,
                    null));
            }
        }
        foreach (var audioIndex in trailing) {
            rows.Add(AudioOnlyRow(audioWindows[audioIndex], rows.Count));
        }
        return rows;
    }

    private static AlignedChapter AudioOnlyRow(AudioChapterWindow window, int order) =>
        new($"audio:{window.TrackEntityId:N}:{window.MarkerId?.ToString("N") ?? "track"}",
            order,
            AlignmentMatchState.AudioOnly,
            null,
            null,
            window);

    private static string ReadableRowId(ReadableChapterWindow chapter) => $"read:{chapter.ChapterKey}";

    #endregion

    #region Actions - Coverage

    private BookAlignmentCoverage MeasureCoverage(
        IReadOnlyList<ReadableChapterWindow> readableChapters,
        int audioWindowCount) {
        var paired = Rows.Where(row => row.IsPaired).ToArray();
        var manual = paired.Count(row => row.Provenance == BookChapterMappingOrigin.Manual);
        return new BookAlignmentCoverage(
            readableChapters.Count,
            audioWindowCount,
            paired.Length,
            manual,
            paired.Length - manual,
            Rows.Count(row => row.MatchState == AlignmentMatchState.ReadableOnly),
            Rows.Count(row => row.MatchState == AlignmentMatchState.AudioOnly),
            PairedReadableFraction(readableChapters, paired),
            paired
                .Where(row => row.Audio!.IsWindowed())
                .Sum(row => row.Audio!.EndSeconds!.Value - row.Audio.StartSeconds),
            _tracks.Sum(track => track.KnownDuration() ?? 0));
    }

    /// <summary>
    /// Paired share of the readable rendition: paired pages over all pages for paged books, the
    /// union of paired fraction ranges for EPUBs (nested entries overlap), or the paired chapter
    /// share when no bounds are known.
    /// </summary>
    private static double PairedReadableFraction(
        IReadOnlyList<ReadableChapterWindow> readableChapters,
        IReadOnlyList<AlignedChapter> paired) {
        var totalPages = readableChapters.Where(chapter => chapter.HasPageBounds()).Sum(chapter => chapter.PageCount!.Value);
        if (totalPages > 0) {
            var pairedPages = paired
                .Where(row => row.Readable!.HasPageBounds())
                .Sum(row => row.Readable!.PageCount!.Value);
            return Math.Clamp(pairedPages / (double)totalPages, 0, 1);
        }

        if (readableChapters.Any(chapter => chapter.HasFractionBounds())) {
            var covered = 0d;
            var reach = double.NegativeInfinity;
            foreach (var (start, end) in paired
                         .Where(row => row.Readable!.HasFractionBounds())
                         .Select(row => (row.Readable!.StartFraction!.Value, row.Readable.EndFraction!.Value))
                         .OrderBy(range => range.Item1)) {
                var clippedStart = Math.Max(start, reach);
                if (end > clippedStart) {
                    covered += end - clippedStart;
                }
                reach = Math.Max(reach, end);
            }
            return Math.Clamp(covered, 0, 1);
        }

        return readableChapters.Count > 0 ? paired.Count / (double)readableChapters.Count : 0;
    }

    #endregion
}
