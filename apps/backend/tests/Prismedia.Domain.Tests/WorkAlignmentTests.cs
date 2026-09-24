using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media.Books;

namespace Prismedia.Domain.Tests;

/// <summary>
/// Guards server-side reading/listening alignment: relative positions carry across paired chapters
/// in both directions, the listening runway never crosses a chapter start, unwindowed chapters align
/// at their start, and unpaired chapters report a gap instead of jumping to another chapter.
/// </summary>
public sealed class WorkAlignmentTests {
    private static readonly Guid BookId = Guid.Parse("10000000-0000-0000-0000-000000000000");
    private static readonly Guid MarkedTrackId = Guid.Parse("20000000-0000-0000-0000-000000000000");
    private static readonly Guid UnprobedTrackId = Guid.Parse("30000000-0000-0000-0000-000000000000");
    private static readonly Guid ChapterOneMarkerId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid ChapterTwoMarkerId = Guid.Parse("40000000-0000-0000-0000-000000000002");
    private static readonly Guid InterludeMarkerId = Guid.Parse("40000000-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset RecordedAt = DateTimeOffset.Parse("2026-09-24T12:00:00Z");

    [Theory]
    // Reading → listening: φ = 0.5 inside [0, 0.4) lands at 300 s minus the 5 s runway.
    [InlineData(false, 2_000d, AlignmentBasis.Interpolated, null, null, 295d, null)]
    // The runway never crosses the start of the paired window.
    [InlineData(false, 4_001d, AlignmentBasis.Interpolated, null, null, 600d, null)]
    // Listening → reading: halfway through [600, 1200) is halfway through [0.4, 0.7).
    [InlineData(true, 900d, AlignmentBasis.Interpolated, null, null, null, 5_500)]
    // A paired chapter whose audio has no known end aligns at the chapter start.
    [InlineData(false, 7_500d, AlignmentBasis.ChapterStart, null, null, 0d, null)]
    // Unpaired audio and unpaired text report a gap, never the first paired chapter.
    [InlineData(true, 1_500d, AlignmentBasis.Exact, AlignmentGapReason.AudioChapterUnpaired, "Interlude", null, null)]
    [InlineData(false, 9_000d, AlignmentBasis.Exact, AlignmentGapReason.ReadableChapterUnpaired, "Epilogue", null, null)]
    // At or past the track's end the position belongs to the track's last window.
    [InlineData(true, 2_400d, AlignmentBasis.Exact, AlignmentGapReason.AudioChapterUnpaired, "Interlude", null, null)]
    public void SwitchCarriesTheRelativePositionOrReportsTheGap(
        bool fromListening,
        double position,
        AlignmentBasis expectedBasis,
        AlignmentGapReason? expectedGap,
        string? expectedGapChapter,
        double? expectedOffset,
        int? expectedIndex) {
        var alignment = Alignment();
        var source = fromListening
            ? ConsumptionModalityDefinition.Listening.OffsetCheckpoint(MarkedTrackId, null, position, 1_800, RecordedAt)
            : ConsumptionModalityDefinition.Reading.Checkpoint(
                BookId,
                ProgressUnit.Cfi,
                (int)position,
                ConsumptionModalityDefinition.ReadablePositionTotal,
                RecordedAt);

        var target = alignment.Switch(source, ReaderMode.Paged);
        var combined = alignment.Combined(source, ReaderMode.Paged);

        Assert.Equal(expectedBasis, target.Basis);
        Assert.Equal(expectedGap, target.Gap);
        Assert.Equal(expectedGapChapter, target.GapChapterTitle);
        Assert.Equal(expectedOffset, target.Listening?.OffsetSeconds);
        Assert.Equal(expectedIndex, target.Reading?.Index);
        Assert.Equal(expectedBasis != AlignmentBasis.Exact, target.Approximate);
        if (expectedGap is not null) {
            Assert.Null(fromListening ? combined.Reading : combined.Listening);
            Assert.NotNull(fromListening ? combined.Listening : combined.Reading);
        }
    }

    [Fact]
    public void AudioOnlyWindowsSitWhereTheGapHappensAndFreshStartUsesTheFirstPair() {
        var alignment = Alignment();

        Assert.Equal(
            ["Chapter One", "Chapter Two", "Interlude", "Appendix", "Epilogue"],
            alignment.Rows.Select(row => row.Readable?.Title ?? row.Audio!.Title));
        Assert.Equal(3, alignment.Coverage.PairedCount);
        Assert.Equal(1, alignment.Coverage.AudioOnlyCount);

        var fresh = alignment.Combined(anchor: null, ReaderMode.Paged);
        Assert.Equal(AlignmentBasis.FreshStart, fresh.Basis);
        Assert.Equal(ChapterOneMarkerId, fresh.Listening?.MarkerId);
        Assert.Equal(0, fresh.Reading?.Index);

        var unprobed = ConsumptionModalityDefinition.Listening.OffsetCheckpoint(UnprobedTrackId, null, 30, null, RecordedAt);
        var reading = alignment.Switch(unprobed, ReaderMode.Paged);
        Assert.Equal(AlignmentBasis.ChapterStart, reading.Basis);
        Assert.Equal(7_000, reading.Reading?.Index);
        Assert.Equal("Text/appendix.xhtml", reading.Reading?.ChapterLocation);
    }

    private static WorkAlignment Alignment() {
        var marked = new AudioTrackSpan(MarkedTrackId, "Part 1", 1_800);
        var unprobed = new AudioTrackSpan(UnprobedTrackId, "Part 2", null);
        var windows = marked.ChapterWindows([
                new SourceChapterMarker(ChapterOneMarkerId, "Chapter One", 0, 600),
                new SourceChapterMarker(ChapterTwoMarkerId, "Chapter Two", 600, null),
                new SourceChapterMarker(InterludeMarkerId, "Interlude", 1_200, null)
            ])
            .Concat(unprobed.ChapterWindows([]))
            .ToArray();
        return new WorkAlignment(
            BookId,
            hasReadableRendition: true,
            [
                Chapter("Text/one.xhtml", "Chapter One", 0, 0.4),
                Chapter("Text/two.xhtml", "Chapter Two", 0.4, 0.7),
                Chapter("Text/appendix.xhtml", "Appendix", 0.7, 0.8),
                Chapter("Text/epilogue.xhtml", "Epilogue", 0.8, 1)
            ],
            [marked, unprobed],
            windows,
            [
                new ChapterPairing("Text/one.xhtml", MarkedTrackId, ChapterOneMarkerId, BookChapterMappingOrigin.Auto),
                new ChapterPairing("Text/two.xhtml", MarkedTrackId, ChapterTwoMarkerId, BookChapterMappingOrigin.Manual),
                new ChapterPairing("Text/appendix.xhtml", UnprobedTrackId, null, BookChapterMappingOrigin.Manual)
            ]);
    }

    private static ReadableChapterWindow Chapter(string key, string title, double start, double end) =>
        new(key, title, 0, key, null, start, end, null);
}
