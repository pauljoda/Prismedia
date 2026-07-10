using Prismedia.Infrastructure.Media.Processing;

namespace Prismedia.Infrastructure.Tests;

public sealed class FileDiscoveryServiceTests : IDisposable {
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("prismedia-discovery-");

    [Fact]
    public async Task DiscoverFilesSkipsExcludedFilesAndDirectoryDescendants() {
        Directory.CreateDirectory(Path.Combine(_root.FullName, "Excluded"));
        Directory.CreateDirectory(Path.Combine(_root.FullName, "Included"));
        await File.WriteAllTextAsync(Path.Combine(_root.FullName, "Excluded", "skip.mkv"), "skip");
        await File.WriteAllTextAsync(Path.Combine(_root.FullName, "Included", "keep.mkv"), "keep");
        await File.WriteAllTextAsync(Path.Combine(_root.FullName, "single-skip.mkv"), "skip");
        var service = new FileDiscoveryService();

        var files = await service.DiscoverFilesAsync(
            _root.FullName,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mkv" },
            recursive: true,
            excludedPaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                Path.Combine(_root.FullName, "Excluded"),
                Path.Combine(_root.FullName, "single-skip.mkv")
            },
            CancellationToken.None);

        Assert.Equal([Path.Combine(_root.FullName, "Included", "keep.mkv")], files);
    }

    [Fact]
    public async Task DiscoverFilesByDirectoryOmitsExcludedGroups() {
        Directory.CreateDirectory(Path.Combine(_root.FullName, "Excluded"));
        Directory.CreateDirectory(Path.Combine(_root.FullName, "Included"));
        await File.WriteAllTextAsync(Path.Combine(_root.FullName, "Excluded", "skip.flac"), "skip");
        await File.WriteAllTextAsync(Path.Combine(_root.FullName, "Included", "keep.flac"), "keep");
        var service = new FileDiscoveryService();

        var groups = await service.DiscoverFilesByDirectoryAsync(
            _root.FullName,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".flac" },
            recursive: true,
            excludedPaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                Path.Combine(_root.FullName, "Excluded")
            },
            CancellationToken.None);

        Assert.Equal([Path.Combine(_root.FullName, "Included")], groups.Keys.ToArray());
        Assert.Equal([Path.Combine(_root.FullName, "Included", "keep.flac")], groups.Single().Value);
    }

    [Fact]
    public async Task DiscoverFilesByDirectoryTreatsAnimatedGalleryMediaAsImages() {
        var galleryPath = Path.Combine(_root.FullName, "Gallery", "A secondGallery");
        Directory.CreateDirectory(galleryPath);
        await File.WriteAllTextAsync(Path.Combine(galleryPath, "CMAF_1080.mp4"), "clip");
        await File.WriteAllTextAsync(Path.Combine(galleryPath, "still.png"), "still");
        var service = new FileDiscoveryService();

        var groups = await service.DiscoverFilesByDirectoryAsync(
            _root.FullName,
            SupportedExtensions.Image,
            recursive: true,
            excludedPaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);

        Assert.Equal([galleryPath], groups.Keys.ToArray());
        Assert.Equal(
            [
                Path.Combine(galleryPath, "CMAF_1080.mp4"),
                Path.Combine(galleryPath, "still.png")
            ],
            groups[galleryPath]);
    }

    [Fact]
    public async Task LinuxCaseDistinctDirectoriesRemainSeparateGroups() {
        if (!OperatingSystem.IsLinux()) {
            return;
        }

        var upper = Directory.CreateDirectory(Path.Combine(_root.FullName, "Album"));
        var lower = Directory.CreateDirectory(Path.Combine(_root.FullName, "album"));
        await File.WriteAllTextAsync(Path.Combine(upper.FullName, "one.flac"), "one");
        await File.WriteAllTextAsync(Path.Combine(lower.FullName, "two.flac"), "two");
        var service = new FileDiscoveryService();

        var groups = await service.DiscoverFilesByDirectoryAsync(
            _root.FullName,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".flac" },
            recursive: true,
            excludedPaths: null,
            CancellationToken.None);

        Assert.Equal(2, groups.Count);
        Assert.Contains(upper.FullName, groups.Keys);
        Assert.Contains(lower.FullName, groups.Keys);
    }

    public void Dispose() {
        if (_root.Exists) {
            _root.Delete(recursive: true);
        }
    }
}
