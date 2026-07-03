using Prismedia.Application.Acquisition;

namespace Prismedia.Application.Tests.Acquisition;

/// <summary>
/// Pins the dangerous-payload gate: executable and known-dangerous extensions are flagged wherever
/// they sit in the payload (the live-verified scenario is a release whose "episode" is a .scr),
/// while ordinary media payloads pass untouched.
/// </summary>
public sealed class DangerousFileDetectionTests {
    [Theory]
    [InlineData("Rick.and.Morty.S09E07.1080p.WEB.h264.scr")]
    [InlineData("setup.exe")]
    [InlineData("Subs/run-me.bat")]
    [InlineData("extras/helper.ps1")]
    [InlineData("Movie.2024.zipx")]
    public void FlagsDangerousExtensionsAnywhereInThePayload(string dangerous) {
        var files = new[] { "Movie.2024.1080p.mkv", dangerous, "Movie.2024.nfo" };
        Assert.Equal(dangerous, DangerousFileDetection.FindDangerousFile(files));
    }

    [Fact]
    public void CleanMediaPayloadsPass() {
        var files = new[] { "Show.S01E01.mkv", "Show.S01E01.srt", "cover.jpg", "book.epub", "notes.txt" };
        Assert.Null(DangerousFileDetection.FindDangerousFile(files));
    }

    [Fact]
    public void MatchingIsCaseInsensitive() {
        Assert.NotNull(DangerousFileDetection.FindDangerousFile(["Payload.SCR"]));
    }
}
