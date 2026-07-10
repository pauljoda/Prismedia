using Prismedia.Application.Jobs.Handlers.Scan;

namespace Prismedia.Api.Tests;

/// <summary>
/// Unit tests for <see cref="AudioLibraryClassifier"/>, the leaf-first resolver that maps a music
/// directory tree into the two supported layouts (Album/Songs and Artist/Album/Songs) plus their
/// multi-disc section extensions.
/// </summary>
public sealed class AudioLibraryClassifierTests {
    private const string Root = "/media/music";

    [Fact]
    public void AlbumWithSongs_NoArtist_SingleUnsectionedAlbum() {
        var layout = AudioLibraryClassifier.Classify(Root, [$"{Root}/Evolve"]);

        Assert.Empty(layout.Artists);
        var album = Assert.Single(layout.Albums);
        Assert.Equal($"{Root}/Evolve", album.Path);
        Assert.Equal("Evolve", album.Title);
        Assert.Null(album.ArtistPath);
        var section = Assert.Single(album.Sections);
        Assert.Null(section.Label);
        Assert.Equal($"{Root}/Evolve", section.DirectoryPath);
    }

    [Fact]
    public void ArtistAlbumSongs_CreatesArtistGroupingWithAlbum() {
        var layout = AudioLibraryClassifier.Classify(Root, [$"{Root}/Imagine Dragons/Evolve"]);

        var artist = Assert.Single(layout.Artists);
        Assert.Equal($"{Root}/Imagine Dragons", artist.Path);
        Assert.Equal("Imagine Dragons", artist.Title);
        var album = Assert.Single(layout.Albums);
        Assert.Equal($"{Root}/Imagine Dragons/Evolve", album.Path);
        Assert.Equal(artist.Path, album.ArtistPath);
        Assert.Null(Assert.Single(album.Sections).Label);
    }

    [Fact]
    public void AlbumWithDiscFolders_NoArtist_BecomesSectionedAlbum() {
        var layout = AudioLibraryClassifier.Classify(Root, [$"{Root}/Greatest Hits/Disc 1", $"{Root}/Greatest Hits/Disc 2"]);

        Assert.Empty(layout.Artists);
        var album = Assert.Single(layout.Albums);
        Assert.Equal($"{Root}/Greatest Hits", album.Path);
        Assert.Null(album.ArtistPath);
        Assert.Collection(album.Sections,
            section => { Assert.Equal("Disc 1", section.Label); Assert.Equal(0, section.Order); },
            section => { Assert.Equal("Disc 2", section.Label); Assert.Equal(1, section.Order); });
    }

    [Fact]
    public void ArtistAlbumDiscSongs_FullChainWithSections() {
        var layout = AudioLibraryClassifier.Classify(Root,
            [$"{Root}/Pink Floyd/The Wall/Disc 1", $"{Root}/Pink Floyd/The Wall/Disc 2"]);

        var artist = Assert.Single(layout.Artists);
        Assert.Equal($"{Root}/Pink Floyd", artist.Path);
        var album = Assert.Single(layout.Albums);
        Assert.Equal($"{Root}/Pink Floyd/The Wall", album.Path);
        Assert.Equal(artist.Path, album.ArtistPath);
        Assert.Collection(album.Sections,
            section => Assert.Equal("Disc 1", section.Label),
            section => Assert.Equal("Disc 2", section.Label));
    }

    [Fact]
    public void AlbumWithDirectTracksAndSubfolder_SubfolderIsSection() {
        var layout = AudioLibraryClassifier.Classify(Root, [$"{Root}/Album", $"{Root}/Album/Bonus"]);

        Assert.Empty(layout.Artists);
        var album = Assert.Single(layout.Albums);
        Assert.Equal($"{Root}/Album", album.Path);
        Assert.Collection(album.Sections,
            section => { Assert.Null(section.Label); Assert.Equal($"{Root}/Album", section.DirectoryPath); },
            section => { Assert.Equal("Bonus", section.Label); Assert.Equal($"{Root}/Album/Bonus", section.DirectoryPath); });
    }

    [Fact]
    public void TwoFoldersDeep_NonDiscNames_AreArtistOfAlbums() {
        var layout = AudioLibraryClassifier.Classify(Root,
            [$"{Root}/The Beatles/Abbey Road", $"{Root}/The Beatles/Revolver"]);

        var artist = Assert.Single(layout.Artists);
        Assert.Equal("The Beatles", artist.Title);
        Assert.Equal(2, layout.Albums.Count);
        Assert.All(layout.Albums, album => Assert.Equal(artist.Path, album.ArtistPath));
        Assert.Contains(layout.Albums, album => album.Title == "Abbey Road");
        Assert.Contains(layout.Albums, album => album.Title == "Revolver");
    }

    [Fact]
    public void DeeperThanThreeLevels_IsFlattenedIntoSectionsNotEntities() {
        var layout = AudioLibraryClassifier.Classify(Root, [$"{Root}/A/B/Disc 1/Sub"]);

        // A is the artist, B the album, and everything below B collapses to a single section —
        // never a nested album entity.
        var artist = Assert.Single(layout.Artists);
        Assert.Equal($"{Root}/A", artist.Path);
        var album = Assert.Single(layout.Albums);
        Assert.Equal($"{Root}/A/B", album.Path);
        var section = Assert.Single(album.Sections);
        Assert.Equal("Sub", section.Label);
    }

    [Fact]
    public void RootLevelDirectories_AreExcluded() {
        // The root itself is never an artist or album; its direct files are loose tracks handled
        // by the scan handler, not the classifier.
        var layout = AudioLibraryClassifier.Classify(Root, [Root]);

        Assert.Empty(layout.Artists);
        Assert.Empty(layout.Albums);
    }

    [Fact]
    public void UnixCaseDistinctAlbumFoldersRemainDistinct() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        var layout = AudioLibraryClassifier.Classify(
            Root,
            [$"{Root}/Live", $"{Root}/live"]);

        Assert.Empty(layout.Artists);
        Assert.Equal(2, layout.Albums.Count);
        Assert.Contains(layout.Albums, album => album.Path == $"{Root}/Live");
        Assert.Contains(layout.Albums, album => album.Path == $"{Root}/live");
    }

    [Theory]
    [InlineData("Disc 1", true)]
    [InlineData("Disc One", true)]
    [InlineData("CD2", true)]
    [InlineData("CD 2", true)]
    [InlineData("Side A", true)]
    [InlineData("Vol. 3", true)]
    [InlineData("Part II", true)]
    [InlineData("disque 1", true)]
    [InlineData("Evolve", false)]
    [InlineData("Greatest Hits", false)]
    [InlineData("CD", false)]
    [InlineData("Disc", false)]
    public void IsSectionFolderName_MatchesDiscPatterns(string name, bool expected) =>
        Assert.Equal(expected, AudioLibraryClassifier.IsSectionFolderName(name));
}
