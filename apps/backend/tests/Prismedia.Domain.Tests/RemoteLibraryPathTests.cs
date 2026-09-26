using Prismedia.Domain.Integrations;

namespace Prismedia.Domain.Tests;

public sealed class RemoteLibraryPathTests {
    [Theory]
    [InlineData("/films", "/films/Title (2024)", true)]
    [InlineData("/films/", "/films/Title/film.mkv", true)]
    [InlineData("/films", "/films", false)]
    [InlineData("/films", "/films-extra/Title", false)]
    [InlineData("/Films", "/films/Title", false)]
    [InlineData("C:\\Films", "c:\\films\\Title", true)]
    [InlineData("\\\\server\\share", "\\\\SERVER\\share\\Title", true)]
    [InlineData("/films", "/films/../outside", false)]
    [InlineData("/films", "films/Title", false)]
    [InlineData("/films", null, false)]
    public void HoldingMustLieStrictlyInsideItsMappedRoot(string root, string? path, bool expected) =>
        Assert.Equal(expected, RemoteLibraryPath.Parse(root).IsAncestorOf(path));

    [Theory]
    [InlineData("")]
    [InlineData("relative/films")]
    [InlineData("/films/../outside")]
    [InlineData("/films/./title")]
    [InlineData("/films/title\\file.mkv")]
    [InlineData("//server")]
    public void UnusableRemotePathsAreRefused(string path) =>
        Assert.Throws<ArgumentException>(() => RemoteLibraryPath.Parse(path));

    [Theory]
    [InlineData("/media", "/Media/films", true)]
    [InlineData("/media/films", "/media", true)]
    [InlineData("/media", "/media2", false)]
    [InlineData("C:\\Media", "/Media", false)]
    public void MappedRootsOverlapConservatively(string first, string second, bool expected) =>
        Assert.Equal(expected, RemoteLibraryPath.Parse(first).Overlaps(RemoteLibraryPath.Parse(second)));
}
