using Prismedia.Application.Files;

namespace Prismedia.Application.Tests;

public sealed class EntitySourcePathTests {
    [Fact]
    public void ArchiveMemberRoundTripsThroughTheCanonicalSourcePathBoundary() {
        var source = EntitySourcePath.ArchiveMember("/media/books/Volume.cbz", "chapter/001.jpg");

        Assert.Equal("/media/books/Volume.cbz::chapter/001.jpg", source);
        Assert.True(EntitySourcePath.TrySplitArchiveMember(source, out var archive, out var member));
        Assert.Equal("/media/books/Volume.cbz", archive);
        Assert.Equal("chapter/001.jpg", member);
        Assert.Equal(archive, EntitySourcePath.PhysicalOwner(source));
    }

    [Theory]
    [InlineData("/media/books/Volume.cbz")]
    [InlineData("::chapter/001.jpg")]
    [InlineData("/media/books/Volume.cbz::")]
    public void PhysicalSourcePathsAndMalformedMemberPathsAreNotSplit(string source) {
        Assert.False(EntitySourcePath.TrySplitArchiveMember(source, out _, out _));
        Assert.Equal(source, EntitySourcePath.PhysicalOwner(source));
    }

    [Fact]
    public void DescendantComparisonUsesTheHostFilesystemCaseSemantics() {
        var parent = Path.Combine(Path.GetTempPath(), "PrismediaCaseSentinel");
        var differentlyCasedChild = Path.Combine(
            Path.GetTempPath(),
            "prismediacasesentinel",
            "Movie.mkv");

        Assert.Equal(
            OperatingSystem.IsWindows(),
            FileSystemPathComparison.IsSameOrDescendant(parent, differentlyCasedChild));
    }

    [Fact]
    public void DescendantComparisonRequiresADirectorySegmentBoundary() {
        var parent = Path.Combine(Path.GetTempPath(), "Prismedia");
        var siblingPrefix = Path.Combine(Path.GetTempPath(), "Prismedia-Other", "Movie.mkv");

        Assert.False(FileSystemPathComparison.IsSameOrDescendant(parent, siblingPrefix));
    }

    [Fact]
    public void PhysicalPrefixMovePreservesArchiveMemberSuffix() {
        var sourceArchive = Path.Combine(Path.GetTempPath(), "Incoming", "Volume.cbz");
        var targetArchive = Path.Combine(Path.GetTempPath(), "Books", "Volume.cbz");
        var persisted = EntitySourcePath.ArchiveMember(sourceArchive, "chapter/001.jpg");

        var mapped = EntitySourcePath.TryMapPhysicalPrefix(
            persisted,
            Path.GetDirectoryName(sourceArchive)!,
            Path.GetDirectoryName(targetArchive)!,
            out var mappedPath);

        Assert.True(mapped);
        Assert.Equal(
            EntitySourcePath.ArchiveMember(targetArchive, "chapter/001.jpg"),
            mappedPath);
    }
}
