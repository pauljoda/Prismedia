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
    private static readonly LibraryScanSelection AllEnabled = new(true, true, true, true, true);

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
    public void RoutesProseAndComicFormatsToIndependentScans() {
        var routed = MediaScanKindRouter.Route(AllEnabled, ["/media/books/novel.epub", "/media/comics/issue.cbz"]);

        Assert.Equal(
            [JobType.ScanBook, JobType.ScanComic],
            routed.Keys.OrderBy(type => type.ToCode()).ToArray());
        Assert.Equal(["/media/books/novel.epub"], routed[JobType.ScanBook]);
        Assert.Equal(["/media/comics/issue.cbz"], routed[JobType.ScanComic]);
    }

    [Fact]
    public void RoutesLooseComicPagesToGalleryAndComicScans() {
        var routed = MediaScanKindRouter.Route(AllEnabled, ["/media/comics/issue-1/001.jpg"]);

        Assert.Equal(
            [JobType.ScanComic, JobType.ScanGallery],
            routed.Keys.OrderBy(type => type.ToCode()).ToArray());
    }

    [Fact]
    public void RoutesOnlyRecognizedComicXmlSidecarsToComicScan() {
        var routed = MediaScanKindRouter.Route(AllEnabled, [
            "/media/comics/issue-1/ComicInfo.xml",
            "/media/comics/issue-1/notes.xml"
        ]);

        Assert.Equal([JobType.ScanComic], routed.Keys.ToArray());
        Assert.Equal(["/media/comics/issue-1/ComicInfo.xml"], routed[JobType.ScanComic]);
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
        var videosOnly = new LibraryScanSelection(
            Videos: true,
            Images: false,
            Audio: false,
            Books: false,
            Comics: false);
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

        Assert.Equal(5, routed.Count);
    }

    [Fact]
    public void SelectionForCoversExactlyTheRoutedKinds() {
        var routed = MediaScanKindRouter.Route(AllEnabled, ["/media/music/track.flac"]);
        var selection = MediaScanKindRouter.SelectionFor(routed);

        Assert.True(selection.Audio);
        Assert.False(selection.Videos);
        Assert.False(selection.Images);
        Assert.False(selection.Books);
        Assert.False(selection.Comics);
    }
}
