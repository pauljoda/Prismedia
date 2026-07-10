using Prismedia.Infrastructure.Media.Persistence;

namespace Prismedia.Infrastructure.Tests;

public sealed class LibraryScanPathRulesTests {
    [Fact]
    public void UnixPathsPreserveCaseDistinctLibraryEntries() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        Assert.False(LibraryScanPathRules.IsDirectChildPath(
            "/media/Bluey/episode.mp4",
            "/media/bluey"));
        Assert.False(LibraryScanPathRules.IsPathUnderRoot(
            "/media/Bluey/episode.mp4",
            "/media/bluey"));
        Assert.False(LibraryScanPathRules.IsPathCoveredByExclusion(
            "/media/Bluey/episode.mp4",
            "/media/bluey"));
    }

    [Theory]
    [InlineData("/media/root/movie.mp4", "/media/root", true)]
    [InlineData("/media/root/season/movie.mp4", "/media/root", false)]
    [InlineData("/media/root/movie.mp4", "/media/other", false)]
    [InlineData(@"C:\Media\Root\movie.mp4", @"C:\Media\Root", true)]
    public void DirectChildPathMatchesOnlyImmediateChildren(string path, string parentPath, bool expected) {
        Assert.Equal(expected, LibraryScanPathRules.IsDirectChildPath(path, parentPath));
    }

    [Theory]
    [InlineData("/media/root", "/media/root", true)]
    [InlineData("/media/root/season/movie.mp4", "/media/root", true)]
    [InlineData("/media/rootish/movie.mp4", "/media/root", false)]
    [InlineData(@"C:\Media\Root\Season\movie.mp4", @"C:\Media\Root", true)]
    public void PathUnderRootRequiresBoundaryMatch(string path, string rootPath, bool expected) {
        Assert.Equal(expected, LibraryScanPathRules.IsPathUnderRoot(path, rootPath));
    }

    [Theory]
    [InlineData("/media/root/skip/movie.mp4", "/media/root/skip", true)]
    [InlineData("/media/root/skip::archive/page.jpg", "/media/root/skip", true)]
    [InlineData("/media/root/skipped/movie.mp4", "/media/root/skip", false)]
    [InlineData(@"C:\Media\Root\Skip\movie.mp4", @"C:\Media\Root\Skip", true)]
    public void ExclusionCoverageMatchesDirectoriesAndArchiveChildren(string path, string excludedPath, bool expected) {
        Assert.Equal(expected, LibraryScanPathRules.IsPathCoveredByExclusion(path, excludedPath));
    }
}
