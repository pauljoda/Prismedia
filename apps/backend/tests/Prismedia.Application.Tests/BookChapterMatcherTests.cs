using Prismedia.Application.Books;

namespace Prismedia.Application.Tests;

/// <summary>
/// Guards the exact-evidence matcher: titles pair only when equal after case, accent, punctuation and
/// whitespace normalisation with every number kept, only when unique on both sides and in the same
/// order on both sides, and never by position, file name, or an untitled chapter's placeholder.
/// </summary>
public sealed class BookChapterMatcherTests {
    private static readonly Guid Track1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Track2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Track3 = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Theory]
    [InlineData("Chapter 3", "chapter 3")]
    [InlineData("CHAPTER  3.", "chapter 3")]
    [InlineData("Chapter 3: The Storm", "chapter 3 the storm")]
    [InlineData("12 Rules for Life", "12 rules for life")]
    [InlineData("Mix of Vivid Civil Mimic", "mix of vivid civil mimic")]
    [InlineData("L’Étranger — Première partie", "l etranger premiere partie")]
    public void NormalizesCaseAccentsAndPunctuationButKeepsNumbersAndWords(string input, string expected) =>
        Assert.Equal(expected, BookChapterMatcher.MatchKey(input));

    [Fact]
    public void PairsOnlyEqualTitlesWithTheSameNumbers() {
        var pairs = Match(
            [Readable("chapter-3", "Chapter 3", 0), Readable("storm", "Chapter 4: The Storm", 1)],
            [
                Audio(Track1, "chapter 3", 0),
                Audio(Track2, "Track 17 – The Storm", 1),
                Audio(Track3, "Chapter 5: The Storm", 2)
            ]);

        Assert.Equal([("chapter-3", Track1)], pairs);
    }

    [Fact]
    public void NeverPairsByPositionEvenForACompleteEmbeddedChapterSet() {
        var firstMarker = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var secondMarker = Guid.Parse("10000000-0000-0000-0000-000000000002");

        var pairs = BookChapterMatcher.ComputeAutoChapterPairs(
            [Readable("chapter-1", "The First", 0), Readable("chapter-2", "The Second", 1)],
            [
                new MatchableAudioChapter(Track1, firstMarker, "Part A", "Part A", 0, 0, 0, 60),
                new MatchableAudioChapter(Track1, secondMarker, "Part B", "Part B", 0, 1, 60, 120)
            ],
            []);

        Assert.Empty(pairs);
    }

    [Fact]
    public void FileNameAndUntitledPlaceholderTitlesNeverProveIdentity() {
        var marker = Guid.Parse("10000000-0000-0000-0000-000000000001");

        var pairs = BookChapterMatcher.ComputeAutoChapterPairs(
            [Readable("chapter-1", "Chapter 1", 0), Readable("prologue", "Prologue", 1)],
            [
                // An untitled embedded chapter keeps its "Chapter 1" placeholder only for display.
                new MatchableAudioChapter(Track1, marker, "Chapter 1", null, 0, 0, 0, 60),
                // A whole file titled by its file name has no identifying title.
                new MatchableAudioChapter(Track2, null, "Prologue", null, 1, 0, 0, 60)
            ],
            []);

        Assert.Empty(pairs);
    }

    [Fact]
    public void TitlesThatRepeatOnEitherSideAreNeverAutoPaired() {
        var pairs = Match(
            [
                Readable("prologue", "Prologue", 0),
                Readable("part-1", "Part One", 1),
                Readable("part-1-again", "Part One", 2)
            ],
            [
                Audio(Track1, "Prologue", 0),
                Audio(Track2, "Prologue", 1),
                Audio(Track3, "Part One", 2)
            ]);

        Assert.Empty(pairs);
    }

    [Fact]
    public void CrossingPairsAreDroppedAndOrderedPairsKept() {
        var pairs = Match(
            [Readable("a", "Arrival", 0), Readable("b", "Departure", 1), Readable("c", "Coda", 2)],
            [Audio(Track1, "Departure", 0), Audio(Track2, "Arrival", 1), Audio(Track3, "Coda", 2)]);

        Assert.Equal([("c", Track3)], pairs);
    }

    [Fact]
    public void ConfirmedPairsConsumeTheirChaptersAndOutrankCrossingAutomaticPairs() {
        var pairs = BookChapterMatcher.ComputeAutoChapterPairs(
            [Readable("a", "Arrival", 0), Readable("b", "Departure", 1), Readable("c", "Coda", 2)],
            [
                Audio(Track1, "Coda", 0),
                Audio(Track2, "Arrival", 1),
                Audio(Track3, "Departure", 2)
            ],
            // A person placed "Departure" first; "Arrival" now crosses that confirmed pair.
            [("b", Track1, null)]);

        Assert.Empty(pairs);
    }

    [Fact]
    public void ConfirmedPairsOutsideTheCurrentInputsAreIgnoredForConsumption() {
        var pairs = BookChapterMatcher.ComputeAutoChapterPairs(
            [Readable("chapter-1", "Bran", 0)],
            [Audio(Track1, "Bran", 0)],
            [("vanished-chapter", Track2, null)]);

        Assert.Equal([new MatchedBookAudioChapter("chapter-1", Track1, null, 0, 60)], pairs);
    }

    [Fact]
    public void MapsMultipleEmbeddedChaptersFromOnePhysicalTrackByTitle() {
        var openingMarker = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var chapterMarker = Guid.Parse("10000000-0000-0000-0000-000000000002");

        var pairs = BookChapterMatcher.ComputeAutoChapterPairs(
            [Readable("opening", "Opening Credits", 0), Readable("chapter-1", "Chapter One", 1)],
            [
                new MatchableAudioChapter(Track1, openingMarker, "Opening Credits", "Opening Credits", 0, 0, 0, 12.5),
                new MatchableAudioChapter(Track1, chapterMarker, "Chapter One", "Chapter One", 0, 1, 12.5, 180)
            ],
            []);

        Assert.Equal([
            new MatchedBookAudioChapter("opening", Track1, openingMarker, 0, 12.5),
            new MatchedBookAudioChapter("chapter-1", Track1, chapterMarker, 12.5, 180)
        ], pairs);
    }

    [Fact]
    public void SignaturesCarryTheMatcherVersion() {
        var current = BookChapterMatcher.StampSignature("0123456789abcdef");

        Assert.True(BookChapterMatcher.IsCurrentSignature(current));
        Assert.False(BookChapterMatcher.IsCurrentSignature("0123456789abcdef0123456789abcdef"));
        Assert.False(BookChapterMatcher.IsCurrentSignature(null));
    }

    private static MatchableReadableChapter Readable(string key, string title, int order) => new(key, title, order);

    /// <summary>A whole audio file whose title tag is <paramref name="titleTag"/>.</summary>
    private static MatchableAudioChapter Audio(Guid trackId, string titleTag, int trackOrder) =>
        new(trackId, null, $"{trackOrder + 1:00}", titleTag, trackOrder, 0, 0, 60);

    private static IReadOnlyList<(string ChapterKey, Guid AudioTrackId)> Match(
        IReadOnlyList<MatchableReadableChapter> readable,
        IReadOnlyList<MatchableAudioChapter> audio) =>
        BookChapterMatcher.ComputeAutoChapterPairs(readable, audio, [])
            .Select(pair => (pair.ChapterKey, pair.AudioTrackId))
            .ToArray();
}
