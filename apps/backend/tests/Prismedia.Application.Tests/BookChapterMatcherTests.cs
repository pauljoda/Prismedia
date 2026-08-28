using Prismedia.Application.Books;

namespace Prismedia.Application.Tests;

/// <summary>
/// Mirrors the reference client's chapter-matching cases so the server matcher can never drift
/// from the behavior users saw when matching ran in the browser.
/// </summary>
public sealed class BookChapterMatcherTests {
    private static readonly Guid Track1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Track2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Track3 = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Theory]
    [InlineData("Chapter 01 — The Boy Who Lived", "the boy who lived")]
    [InlineData("01. The Boy Who Lived", "the boy who lived")]
    [InlineData("Prologue", "prologue")]
    public void NormalizesChapterLabelsWithoutErasingMeaningfulTitles(string input, string expected) =>
        Assert.Equal(expected, BookChapterMatcher.MatchKey(input));

    [Fact]
    public void MatchesAudioPartsToReadableChaptersByNormalizedTitle() {
        var pairs = BookChapterMatcher.ComputeAutoPairs(
            [
                new MatchableReadableChapter("chapter-1", "Chapter 1: Bran", 0),
                new MatchableReadableChapter("chapter-2", "Chapter 2: Catelyn", 1)
            ],
            [
                new MatchableAudioTrack(Track2, "02 - Catelyn", 1),
                new MatchableAudioTrack(Track1, "01 - Bran", 0)
            ],
            []);

        Assert.Equal(
            [("chapter-1", Track1), ("chapter-2", Track2)],
            pairs);
    }

    [Fact]
    public void DoesNotUseChapterNumbersWhenTextTitlesDiffer() {
        var pairs = BookChapterMatcher.ComputeAutoPairs(
            [
                new MatchableReadableChapter("chapter-1", "Chapter 1: An Unexpected Party", 0),
                new MatchableReadableChapter("chapter-2", "Chapter 2: Roast Mutton", 1)
            ],
            [
                new MatchableAudioTrack(Track2, "A Storm of Swords — Chapter 02", 0),
                new MatchableAudioTrack(Track1, "A Storm of Swords — Chapter 01", 1)
            ],
            []);

        Assert.Empty(pairs);
    }

    [Fact]
    public void DoesNotUseDelimitedTrailingNumbersFromAudioFilenames() {
        var pairs = BookChapterMatcher.ComputeAutoPairs(
            [
                new MatchableReadableChapter("chapter-1", "Chapter 1", 0),
                new MatchableReadableChapter("chapter-2", "Chapter 2", 1)
            ],
            [
                new MatchableAudioTrack(Track2, "George R. R. Martin - SFI03 Storm of Swords - 2", 0),
                new MatchableAudioTrack(Track1, "George R. R. Martin - SFI03 Storm of Swords - 1", 1)
            ],
            []);

        Assert.Empty(pairs);
    }

    [Fact]
    public void DoesNotMistakeABookNumberAtTheEndOfATitleForAChapter() {
        var pairs = BookChapterMatcher.ComputeAutoPairs(
            [new MatchableReadableChapter("chapter-3", "Chapter 3", 0)],
            [new MatchableAudioTrack(Track1, "A Storm of Swords: A Song of Ice and Fire, Book 3", 0)],
            []);

        Assert.Empty(pairs);
    }

    [Fact]
    public void DoesNotInferChapterNumbersFromAudioSortOrder() {
        var pairs = BookChapterMatcher.ComputeAutoPairs(
            [
                new MatchableReadableChapter("chapter-1", "Chapter 1", 0),
                new MatchableReadableChapter("chapter-2", "Chapter 2", 1)
            ],
            [
                new MatchableAudioTrack(Track1, "Bran", 0),
                new MatchableAudioTrack(Track2, "Catelyn", 1)
            ],
            []);

        Assert.Empty(pairs);
    }

    [Fact]
    public void ManualMappingsConsumeTheirChaptersAndTracksBeforeTitleMatching() {
        var pairs = BookChapterMatcher.ComputeAutoPairs(
            [
                new MatchableReadableChapter("prologue", "Prologue", 0),
                new MatchableReadableChapter("chapter-1", "Chapter 1", 1)
            ],
            [
                new MatchableAudioTrack(Track1, "Chapter 1", 0),
                new MatchableAudioTrack(Track2, "Prologue", 1)
            ],
            [("prologue", Track1), ("chapter-1", Track2)]);

        // Both sides are fully claimed by the manual map, so no automatic pair remains.
        Assert.Empty(pairs);
    }

    [Fact]
    public void LeavesUnmatchedAudioUnattachedInsteadOfGuessing() {
        var pairs = BookChapterMatcher.ComputeAutoPairs(
            [
                new MatchableReadableChapter("prologue", "Prologue", 0),
                new MatchableReadableChapter("chapter-1", "Bran", 1)
            ],
            [
                new MatchableAudioTrack(Track1, "Publisher credits", 0),
                new MatchableAudioTrack(Track2, "Historical appendix", 1),
                new MatchableAudioTrack(Track3, "Author interview", 2)
            ],
            []);

        Assert.Empty(pairs);
    }

    [Fact]
    public void ManualPairsOutsideTheCurrentInputsAreIgnoredForConsumption() {
        var pairs = BookChapterMatcher.ComputeAutoPairs(
            [new MatchableReadableChapter("chapter-1", "Bran", 0)],
            [new MatchableAudioTrack(Track1, "Bran", 0)],
            [("vanished-chapter", Track2)]);

        Assert.Equal([("chapter-1", Track1)], pairs);
    }

    [Fact]
    public void MapsMultipleEmbeddedChaptersFromOnePhysicalTrackByTitle() {
        var openingMarker = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var chapterMarker = Guid.Parse("10000000-0000-0000-0000-000000000002");

        var pairs = BookChapterMatcher.ComputeAutoChapterPairs(
            [
                new MatchableReadableChapter("opening", "Opening Credits", 0),
                new MatchableReadableChapter("chapter-1", "Chapter One", 1)
            ],
            [
                new MatchableAudioChapter(Track1, openingMarker, "Opening Credits", 0, 0, 0, 12.5),
                new MatchableAudioChapter(Track1, chapterMarker, "Chapter One", 0, 1, 12.5, 180)
            ],
            []);

        Assert.Equal([
            new MatchedBookAudioChapter("opening", Track1, openingMarker, 0, 12.5),
            new MatchedBookAudioChapter("chapter-1", Track1, chapterMarker, 12.5, 180)
        ], pairs);
    }

    [Fact]
    public void UsesOrdinalFallbackOnlyForACompleteEmbeddedChapterSet() {
        var firstMarker = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var secondMarker = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var readable = new[] {
            new MatchableReadableChapter("chapter-1", "The First", 0),
            new MatchableReadableChapter("chapter-2", "The Second", 1)
        };

        var embedded = BookChapterMatcher.ComputeAutoChapterPairs(
            readable,
            [
                new MatchableAudioChapter(Track1, firstMarker, "Part A", 0, 0, 0, 60),
                new MatchableAudioChapter(Track1, secondMarker, "Part B", 0, 1, 60, 120)
            ],
            []);
        var unmarkedFiles = BookChapterMatcher.ComputeAutoChapterPairs(
            readable,
            [
                new MatchableAudioChapter(Track1, null, "Part A", 0, 0, 0, 60),
                new MatchableAudioChapter(Track2, null, "Part B", 1, 0, 0, 60)
            ],
            []);

        Assert.Equal(["chapter-1", "chapter-2"], embedded.Select(pair => pair.ChapterKey));
        Assert.Empty(unmarkedFiles);
    }
}
