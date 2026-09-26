using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media.Books;

namespace Prismedia.Domain.Tests;

/// <summary>
/// Guards server-side reading/listening alignment. A Linked Book carries relative positions across
/// paired chapters in both directions (the listening runway never crosses a chapter start, unwindowed
/// chapters align at their start, unpaired chapters report a gap). Every other Book is Separate: it
/// never switches, never moves the reading cursor from listening, and measures each format on its own.
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
    public void LinkedBooksCarryTheRelativePositionOrReportTheGap(
        bool fromListening,
        double position,
        AlignmentBasis expectedBasis,
        AlignmentGapReason? expectedGap,
        string? expectedGapChapter,
        double? expectedOffset,
        int? expectedIndex) {
        var alignment = LinkedAlignment();
        var source = fromListening ? Listening(MarkedTrackId, position) : Reading((int)position);

        var target = alignment.Switch(source, ReaderMode.Paged);
        var combined = alignment.Combined(source, ReaderMode.Paged);

        Assert.True(alignment.Link.IsLinked);
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
        var alignment = LinkedAlignment();

        Assert.Equal(
            ["Chapter One", "Chapter Two", "Interlude", "Appendix", "Epilogue"],
            alignment.Rows.Select(row => row.Readable?.Title ?? row.Audio!.Title));
        Assert.Equal(3, alignment.Coverage.PairedCount);
        Assert.Equal(2, alignment.Coverage.ManualCount);
        Assert.Equal(1, alignment.Coverage.AutomaticCount);
        Assert.Equal(1, alignment.Coverage.AudioOnlyCount);

        var fresh = alignment.Combined(anchor: null, ReaderMode.Paged);
        Assert.Equal(AlignmentBasis.FreshStart, fresh.Basis);
        Assert.Equal(ChapterOneMarkerId, fresh.Listening?.MarkerId);
        Assert.Equal(0, fresh.Reading?.Index);

        var reading = alignment.Switch(Listening(UnprobedTrackId, 30), ReaderMode.Paged);
        Assert.Equal(AlignmentBasis.ChapterStart, reading.Basis);
        Assert.Equal(7_000, reading.Reading?.Index);
        Assert.Equal("Text/appendix.xhtml", reading.Reading?.ChapterLocation);
    }

    [Fact]
    public void SeparateBooksNeverSwitchNorMoveTheReadingCursorFromListening() {
        // A single chapterless file: even a hand-picked pair cannot link it, because the file has no
        // chapter boundary a readable chapter could line up with.
        var audio = new AudiobookRendition([new AudioTrackSpan(MarkedTrackId, "Whole Book", 36_000, SourcePath: "/b/Whole Book.mp3")]);
        var alignment = new WorkAlignment(
            BookId,
            hasReadableRendition: true,
            [Chapter("Text/one.xhtml", "Chapter One", 0, 0.5), Chapter("Text/two.xhtml", "Chapter Two", 0.5, 1)],
            audio,
            [new ChapterPairing("Text/one.xhtml", MarkedTrackId, null, BookChapterMappingOrigin.Manual)]);
        var listening = Listening(MarkedTrackId, 9_000);
        var reading = Reading(2_500);

        Assert.Equal(AudiobookStructure.Unstructured, alignment.Audio.Structure?.Structure);
        Assert.Equal(BookLink.Separate(AlignmentGapReason.AudioUnstructured), alignment.Link);
        Assert.True(alignment.KeepsProgressSeparate);
        Assert.Null(alignment.PlaceCursor(listening));

        var toReading = alignment.Switch(listening, ReaderMode.Paged);
        Assert.Equal(AlignmentGapReason.AudioUnstructured, toReading.Gap);
        Assert.Null(toReading.Reading);
        Assert.Equal("read:Text/one.xhtml", toReading.RowId);
        var toListening = alignment.Switch(reading, ReaderMode.Paged);
        Assert.Equal(AlignmentGapReason.AudioUnstructured, toListening.Gap);
        Assert.Null(toListening.Listening);
        Assert.Equal(AlignmentGapReason.AudioUnstructured, alignment.Combined(anchor: null, ReaderMode.Paged).Gap);

        // Each format is measured only from its own exact position.
        Assert.Equal(0.25, alignment.ReadingFraction(reading));
        Assert.Equal(0.25, alignment.ListeningFraction(listening));
    }

    [Fact]
    public void ExactChapterBoundariesLinkOnlyWithAnExactPair() {
        var chaptered = new AudioTrackSpan(
            MarkedTrackId,
            "Book",
            1_200,
            [
                new SourceChapterMarker(ChapterOneMarkerId, "Chapter One", 0, 600),
                new SourceChapterMarker(ChapterTwoMarkerId, "Chapter 2", 600, 1_200, Untitled: true)
            ]);
        IReadOnlyList<ReadableChapterWindow> readable = [Chapter("Text/one.xhtml", "Chapter One", 0, 1)];

        var unpaired = new WorkAlignment(BookId, true, readable, new AudiobookRendition([chaptered]), []);
        var paired = new WorkAlignment(
            BookId,
            true,
            readable,
            new AudiobookRendition([chaptered]),
            [new ChapterPairing("Text/one.xhtml", MarkedTrackId, ChapterOneMarkerId, BookChapterMappingOrigin.Auto)]);
        var audioOnly = new WorkAlignment(BookId, false, [], new AudiobookRendition([chaptered]), []);

        Assert.Equal(BookLink.Separate(AlignmentGapReason.NoExactPairs), unpaired.Link);
        Assert.Equal(BookLink.Linked, paired.Link);
        Assert.Equal("Chapter One", chaptered.IdentifyingTitle(chaptered.ChapterWindows()[0]));
        Assert.Null(chaptered.IdentifyingTitle(chaptered.ChapterWindows()[1]));
        // An audio-only Book keeps its whole-work seconds cursor.
        Assert.Equal(900, audioOnly.PlaceCursor(Listening(MarkedTrackId, 900))?.Index);
        Assert.False(audioOnly.KeepsProgressSeparate);
    }

    [Theory]
    [InlineData(AudiobookStructure.Unstructured, new[] { "/b/Book.mp3" }, new double[] { 36_000 })]
    [InlineData(AudiobookStructure.Parts, new[] { "/b/Book - Part 01.mp3", "/b/Book - Part 02.mp3", "/b/Book - Part 03.mp3" }, new double[] { 3_600, 900, 60 })]
    [InlineData(AudiobookStructure.Parts, new[] { "/b/CD1/01.mp3", "/b/CD2/01.mp3" }, new double[] { 600, 700 })]
    [InlineData(AudiobookStructure.Parts, new[] { "/b/a.mp3", "/b/b.mp3", "/b/c.mp3", "/b/d.mp3" }, new double[] { 3_600, 3_550, 3_700, 1_200 })]
    [InlineData(AudiobookStructure.FilePerChapter, new[] { "/b/01 Arrival.mp3", "/b/02 Departure.mp3", "/b/03 Coda.mp3" }, new double[] { 1_300, 2_900, 700 })]
    public void AudiobookStructureIsRecognisedFromTheFiles(AudiobookStructure expected, string[] paths, double[] durations) {
        var rendition = new AudiobookRendition(paths.Select((path, index) => new AudioTrackSpan(
            Guid.NewGuid(),
            Path.GetFileNameWithoutExtension(path),
            durations[index],
            SourcePath: path)));

        Assert.Equal(expected, rendition.Structure?.Structure);
        Assert.Equal(expected is AudiobookStructure.Parts or AudiobookStructure.Unstructured, !rendition.Structure!.HasExactChapterBoundaries);
    }

    [Fact]
    public void AnyTrackWithEmbeddedChaptersEverywhereIsChaptered() {
        var marker = new SourceChapterMarker(Guid.NewGuid(), "Opening", 0, 60);
        var rendition = new AudiobookRendition([
            new AudioTrackSpan(Guid.NewGuid(), "Part 1", 60, [marker], "/b/Book - Part 1.m4b"),
            new AudioTrackSpan(Guid.NewGuid(), "Part 2", 60, [marker with { MarkerId = Guid.NewGuid() }], "/b/Book - Part 2.m4b")
        ]);

        Assert.Equal(AudiobookStructure.Chaptered, rendition.Structure?.Structure);
    }

    [Fact]
    public void PlaybackOrderUsesDistinctTrackNumbersElseNaturalFileOrder() {
        var untagged = new AudiobookRendition([Track("/b/10.mp3", null), Track("/b/2.mp3", null), Track("/b/1.mp3", null)]);
        var tagged = new AudiobookRendition([Track("/b/a.mp3", 3), Track("/b/b.mp3", 1), Track("/b/c.mp3", 2)]);
        var duplicateTags = new AudiobookRendition([Track("/b/b.mp3", 1), Track("/b/a.mp3", 1), Track("/b/c.mp3", 2)]);

        Assert.Equal(["/b/1.mp3", "/b/2.mp3", "/b/10.mp3"], untagged.Tracks.Select(track => track.SourcePath));
        Assert.Equal(["/b/b.mp3", "/b/c.mp3", "/b/a.mp3"], tagged.Tracks.Select(track => track.SourcePath));
        Assert.Equal(["/b/a.mp3", "/b/b.mp3", "/b/c.mp3"], duplicateTags.Tracks.Select(track => track.SourcePath));
    }

    [Fact]
    public void ListeningFractionCountsEveryEarlierTrackInPlaybackOrder() {
        var first = new AudioTrackSpan(MarkedTrackId, "01", 600, SourcePath: "/b/01.mp3");
        var second = new AudioTrackSpan(UnprobedTrackId, "02", 400, SourcePath: "/b/02.mp3");
        var rendition = new AudiobookRendition([second, first]);

        Assert.Equal(700, rendition.ElapsedSeconds(Listening(UnprobedTrackId, 100)));
        Assert.Equal(0.7, rendition.ListenedFraction(Listening(UnprobedTrackId, 100)));
        Assert.Null(rendition.ListenedFraction(Listening(Guid.NewGuid(), 100)));
    }

    private static AudioTrackSpan Track(string path, int? trackNumber) =>
        new(Guid.NewGuid(), Path.GetFileNameWithoutExtension(path), 60, SourcePath: path, TrackNumberTag: trackNumber);

    private static WorkAlignment LinkedAlignment() {
        var marked = new AudioTrackSpan(
            MarkedTrackId,
            "Main Recording",
            1_800,
            [
                new SourceChapterMarker(ChapterOneMarkerId, "Chapter One", 0, 600),
                new SourceChapterMarker(ChapterTwoMarkerId, "Chapter Two", 600, null),
                new SourceChapterMarker(InterludeMarkerId, "Interlude", 1_200, null)
            ],
            "/b/Main Recording.m4b");
        var unprobed = new AudioTrackSpan(UnprobedTrackId, "Bonus Material", null, SourcePath: "/b/Bonus Material.mp3");
        return new WorkAlignment(
            BookId,
            hasReadableRendition: true,
            [
                Chapter("Text/one.xhtml", "Chapter One", 0, 0.4),
                Chapter("Text/two.xhtml", "Chapter Two", 0.4, 0.7),
                Chapter("Text/appendix.xhtml", "Appendix", 0.7, 0.8),
                Chapter("Text/epilogue.xhtml", "Epilogue", 0.8, 1)
            ],
            new AudiobookRendition([marked, unprobed]),
            [
                new ChapterPairing("Text/one.xhtml", MarkedTrackId, ChapterOneMarkerId, BookChapterMappingOrigin.Auto),
                new ChapterPairing("Text/two.xhtml", MarkedTrackId, ChapterTwoMarkerId, BookChapterMappingOrigin.Manual),
                new ChapterPairing("Text/appendix.xhtml", UnprobedTrackId, null, BookChapterMappingOrigin.Ordered)
            ]);
    }

    private static ProgressCheckpoint Listening(Guid trackId, double offset) =>
        ConsumptionModalityDefinition.Listening.OffsetCheckpoint(trackId, null, offset, null, RecordedAt);

    private static ProgressCheckpoint Reading(int index) =>
        ConsumptionModalityDefinition.Reading.Checkpoint(
            BookId,
            ProgressUnit.Cfi,
            index,
            ConsumptionModalityDefinition.ReadablePositionTotal,
            RecordedAt);

    private static ReadableChapterWindow Chapter(string key, string title, double start, double end) =>
        new(key, title, 0, key, null, start, end, null);
}
