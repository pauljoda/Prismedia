using Prismedia.Application.Acquisition;

namespace Prismedia.Application.Tests.Acquisition;

/// <summary>
/// Covers the PROPER/REPACK/RERIP + anime-version revision detector and the trailing release-group
/// extractor — the title-truth groundwork for revision-aware ranking/upgrades and custom formats.
/// </summary>
public sealed class ReleaseRevisionDetectionTests {
    [Theory]
    [InlineData("Show S01E05 1080p WEB-DL", 1)]
    [InlineData("Show S01E05 1080p WEB-DL PROPER", 2)]
    [InlineData("Show S01E05 1080p WEB-DL REPACK", 2)]
    [InlineData("Show S01E05 1080p WEB-DL RERIP", 2)]
    [InlineData("Show S01E05 1080p WEB-DL REPACK2", 3)]
    [InlineData("Show S01E05 1080p WEB-DL PROPER2", 3)]
    [InlineData("[SubGroup] Anime - 05 [1080p] v3", 3)]
    [InlineData("[SubGroup] Anime - 05.v2.1080p", 2)]
    [InlineData("Show S01E05 PROPER 1080p WEB", 2)]
    public void DetectsRevisionFromTokens(string title, int expected) =>
        Assert.Equal(expected, ReleaseRevisionDetection.Detect(title));

    [Fact]
    public void RevisionDetectionIsCaseInsensitive() {
        Assert.Equal(2, ReleaseRevisionDetection.Detect("show s01e05 proper 1080p"));
        Assert.Equal(2, ReleaseRevisionDetection.Detect("SHOW S01E05 REPACK 1080P"));
    }

    [Fact]
    public void WordsContainingProperDoNotFalseTrigger() {
        // "Property" contains "proper" but is not a revision token — the word boundary keeps it at 1.
        Assert.Equal(1, ReleaseRevisionDetection.Detect("Property Brothers S01E05 1080p WEB-DL"));
        // A version-looking substring inside a word must not fire either.
        Assert.Equal(1, ReleaseRevisionDetection.Detect("Revamp 1080p BluRay"));
    }

    [Fact]
    public void HighestRecognizedTokenWins() {
        // Both PROPER (→2) and v3 (→3) present: the max is taken.
        Assert.Equal(3, ReleaseRevisionDetection.Detect("[SubGroup] Anime - 05 PROPER v3 1080p"));
    }

    [Theory]
    [InlineData("Show.S01E05.1080p.WEB.H264-NTb", "NTb")]
    [InlineData("Movie 2024 2160p BluRay x265-GROUP[rartv]", "GROUP")]
    [InlineData("Movie.2024.1080p.BluRay.x264-Xtra-Ordinary", "Xtra-Ordinary")]
    public void DetectsTrailingReleaseGroup(string title, string expected) =>
        Assert.Equal(expected, ReleaseGroupDetection.Detect(title));

    [Fact]
    public void StripsSiteSuffixesAndExtensionBeforeMatching() {
        Assert.Equal("GROUP", ReleaseGroupDetection.Detect("Movie.2024.1080p.WEB-DL-GROUP[eztv].mkv"));
        Assert.Equal("NTb", ReleaseGroupDetection.Detect("Show.S01E05.1080p.WEB.H264-NTb[rartv][eztv]"));
    }

    [Fact]
    public void ReturnsNullWhenNoConfidentGroup() {
        Assert.Null(ReleaseGroupDetection.Detect("Movie 2024 1080p BluRay x264"));
        Assert.Null(ReleaseGroupDetection.Detect("Show S01E05 1080p WEB-DL"));
        Assert.Null(ReleaseGroupDetection.Detect(""));
        Assert.Null(ReleaseGroupDetection.Detect(null));
    }

    [Fact]
    public void DoesNotMistakeTrailingQualityOrLanguageTokensForAGroup() {
        // A trailing "-1080p" / "-ITA" is a quality/language token, not a group.
        Assert.Null(ReleaseGroupDetection.Detect("Movie 2024 BluRay-1080p"));
        Assert.Null(ReleaseGroupDetection.Detect("Movie 2024 1080p BluRay-ITA"));
    }
}
