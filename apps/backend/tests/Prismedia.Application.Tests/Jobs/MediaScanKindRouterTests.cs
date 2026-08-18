using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Jobs;

/// <summary>
/// Locks the change-intent routing matrix: a touched file reaches only the scan kinds whose
/// media families can own it, directories fan out to every enabled kind, and disabled or
/// unrecognized paths route nowhere.
/// </summary>
public sealed class MediaScanKindRouterTests {
    private static readonly LibraryScanSelection AllEnabled = new(true, true, true, true);

    [Fact]
    public void RoutesVideoFileToVideoAndGalleryScansOnly() {
        // Gallery scans deliberately accept video containers (animated web clips), so a video
        // extension reaches both — but never audio or book scans.
        var routed = MediaScanKindRouter.Route(AllEnabled, ["/media/shows/S01E05.mkv"]);

        Assert.Equal(
            [JobType.ScanGallery, JobType.ScanLibrary],
            routed.Keys.OrderBy(type => type.ToCode()).ToArray());
    }

    [Fact]
    public void RoutesSubtitleSidecarToVideoScan() {
        var routed = MediaScanKindRouter.Route(AllEnabled, ["/media/shows/S01E05.en.srt"]);

        Assert.Equal([JobType.ScanLibrary], routed.Keys.ToArray());
    }

    [Fact]
    public void RoutesBookFormatsToBookScanOnly() {
        var routed = MediaScanKindRouter.Route(AllEnabled, ["/media/books/novel.epub", "/media/comics/issue.cbz"]);

        Assert.Equal([JobType.ScanBook], routed.Keys.ToArray());
        Assert.Equal(2, routed[JobType.ScanBook].Count);
    }

    [Fact]
    public void RoutesAudiobookContainersToAudioAndBookScans() {
        var routed = MediaScanKindRouter.Route(AllEnabled, ["/media/audiobooks/story.m4b"]);

        Assert.Equal(
            [JobType.ScanAudio, JobType.ScanBook],
            routed.Keys.OrderBy(type => type.ToCode()).ToArray());
    }

    [Fact]
    public void DropsKindsDisabledOnTheRoot() {
        var videosOnly = new LibraryScanSelection(Videos: true, Images: false, Audio: false, Books: false);
        var routed = MediaScanKindRouter.Route(videosOnly, ["/media/shows/S01E05.mkv", "/media/books/novel.epub"]);

        Assert.Equal([JobType.ScanLibrary], routed.Keys.ToArray());
        Assert.Equal(["/media/shows/S01E05.mkv"], routed[JobType.ScanLibrary]);
    }

    [Fact]
    public void DropsUnrecognizedFileExtensions() {
        var routed = MediaScanKindRouter.Route(AllEnabled, ["/media/shows/notes.txt"]);

        Assert.Empty(routed);
    }

    [Fact]
    public void RoutesExtensionlessPathsToEveryEnabledKind() {
        // A deleted directory cannot be distinguished from a deleted extensionless file; its
        // former contents are unknown, so every enabled kind re-checks its subtree.
        var routed = MediaScanKindRouter.Route(AllEnabled, ["/media/shows/Season 02"]);

        Assert.Equal(4, routed.Count);
    }

    [Fact]
    public void SelectionForCoversExactlyTheRoutedKinds() {
        var routed = MediaScanKindRouter.Route(AllEnabled, ["/media/music/track.flac"]);
        var selection = MediaScanKindRouter.SelectionFor(routed);

        Assert.True(selection.Audio);
        Assert.False(selection.Videos);
        Assert.False(selection.Images);
        Assert.False(selection.Books);
    }
}
