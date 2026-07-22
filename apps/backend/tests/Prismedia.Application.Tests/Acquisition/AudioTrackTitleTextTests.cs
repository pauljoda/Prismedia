using Prismedia.Application.Acquisition;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class AudioTrackTitleTextTests {
    [Theory]
    [InlineData(
        "Bow Down (Inspired by \"Dragon Ball\")",
        "01. Bow Down (Inspired by Dragon Ball )")]
    [InlineData(
        "Demons (Inspired by \"My Hero Academia\")",
        "01. Demons (Inspired by My Hero Academia )")]
    [InlineData(
        "Say My Name (Inspired by \"Dragon Ball\")",
        "01. Say My Name (Inspired by Dragon Ball )")]
    [InlineData(
        "Set It On Fire (Inspired by \"Demon Slayer: Mugen Train\")",
        "01. Set It On Fire (Inspired by Demon Slayer_ Mugen Train )")]
    [InlineData(
        "Who's Standing Now?",
        "01. Who's Standing Now ")]
    [InlineData(
        "Re/Align.",
        "01 - Re Align_")]
    [InlineData(
        "Find Your Way (Inspired by “Avatar: The Last Airbender”)",
        "01. Find Your Way (Inspired by Avatar_ The Last Airbender )")]
    [InlineData(
        "Find Your Way (Inspired by “Avatar: The Last Airbender”)",
        "01 Find Your Way (Inspired by “Avatar - The Last Airbender”) [798 kbps]")]
    public void NormalizeMatchesOnlyFilesystemSafeTitleDecoration(string wantedTitle, string scannedTitle) {
        Assert.Equal(
            AudioTrackTitleText.Normalize(wantedTitle),
            AudioTrackTitleText.Normalize(scannedTitle));
    }

    [Fact]
    public void NormalizePreservesPunctuationThatTheImportSanitizerAllows() {
        Assert.NotEqual(
            AudioTrackTitleText.Normalize("Light It Up!"),
            AudioTrackTitleText.Normalize("Light It Up"));
    }

    [Theory]
    [InlineData("Happy", "04 Pharrell Williams - Happy", true)]
    [InlineData("Happy - From Despicable Me 2", "04 Pharrell Williams - Happy - From Despicable Me 2", true)]
    [InlineData("Happy", "Pharrell Williams - Happy", false)]
    [InlineData("Happy", "04 Pharrell Williams - Not Happy", false)]
    public void MetadataMatchAllowsOnlyNumberedArtistPrefixedFilenames(
        string metadataTitle,
        string scannedTitle,
        bool expected) {
        Assert.Equal(expected, AudioTrackTitleText.MatchesMetadataTitle(metadataTitle, scannedTitle));
    }
}
