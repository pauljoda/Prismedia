using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Infrastructure.Files;
using Prismedia.Infrastructure.Media.Comics;
using SkiaSharp;

namespace Prismedia.Infrastructure.Tests;

public sealed class ComicFolderNormalizerTests {
    [Fact]
    public async Task ExplicitSidecarCreatesVerifiedNaturallyOrderedArchiveAndKeepsOriginals() {
        var sourceRoot = Directory.CreateTempSubdirectory("prismedia-loose-comic-");
        var dataRoot = Directory.CreateTempSubdirectory("prismedia-loose-comic-data-");
        try {
            var chapter = Directory.CreateDirectory(Path.Combine(sourceRoot.FullName, "Series", "Chapter 1"));
            var pageTen = Path.Combine(chapter.FullName, "10.png");
            var pageTwo = Path.Combine(chapter.FullName, "2.png");
            var sidecar = Path.Combine(chapter.FullName, "ComicInfo.xml");
            WritePng(pageTen, SKColors.Red);
            WritePng(pageTwo, SKColors.Blue);
            await File.WriteAllTextAsync(sidecar, "<ComicInfo><Series>Series</Series></ComicInfo>");
            var normalizer = Create(dataRoot.FullName);

            var result = await normalizer.NormalizeAsync(
                Root(sourceRoot.FullName, scanImages: true),
                new HashSet<string>(),
                CancellationToken.None);

            var normalized = Assert.Single(result.Archives);
            Assert.Empty(result.FailedPaths);
            Assert.Equal(chapter.FullName + ".cbz", normalized.ClassificationPath);
            Assert.Equal(chapter.FullName, normalized.OriginFolderPath);
            Assert.StartsWith(Path.Combine(dataRoot.FullName, "generated-sources", "comics"), normalized.ArchivePath);
            using (var archive = ZipFile.OpenRead(normalized.ArchivePath)) {
                Assert.Equal(
                    ["2.png", "10.png", "ComicInfo.xml"],
                    archive.Entries.Select(entry => entry.FullName).ToArray());
            }
            Assert.True(File.Exists(pageTen));
            Assert.True(File.Exists(pageTwo));
            Assert.True(File.Exists(sidecar));

            var firstWrite = File.GetLastWriteTimeUtc(normalized.ArchivePath);
            var second = await normalizer.NormalizeAsync(
                Root(sourceRoot.FullName, scanImages: true),
                new HashSet<string>(),
                CancellationToken.None);
            Assert.Equal(normalized, Assert.Single(second.Archives));
            Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(normalized.ArchivePath));
        } finally {
            sourceRoot.Delete(recursive: true);
            dataRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ImageOnlyLeafIsImplicitBoundaryOnlyWhenGalleryScanningIsDisabled() {
        var sourceRoot = Directory.CreateTempSubdirectory("prismedia-loose-comic-");
        var dataRoot = Directory.CreateTempSubdirectory("prismedia-loose-comic-data-");
        try {
            var chapter = Directory.CreateDirectory(Path.Combine(sourceRoot.FullName, "Series", "Chapter 1"));
            WritePng(Path.Combine(chapter.FullName, "001.png"), SKColors.Green);
            var normalizer = Create(dataRoot.FullName);

            var galleryResult = await normalizer.NormalizeAsync(
                Root(sourceRoot.FullName, scanImages: true),
                new HashSet<string>(),
                CancellationToken.None);
            var comicResult = await normalizer.NormalizeAsync(
                Root(sourceRoot.FullName, scanImages: false),
                new HashSet<string>(),
                CancellationToken.None);

            Assert.Empty(galleryResult.Archives);
            Assert.Single(comicResult.Archives);
        } finally {
            sourceRoot.Delete(recursive: true);
            dataRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ExplicitBoundaryRejectsMixedContentWithoutCreatingArchive() {
        var sourceRoot = Directory.CreateTempSubdirectory("prismedia-loose-comic-");
        var dataRoot = Directory.CreateTempSubdirectory("prismedia-loose-comic-data-");
        try {
            var chapter = Directory.CreateDirectory(Path.Combine(sourceRoot.FullName, "Series", "Chapter 1"));
            var page = Path.Combine(chapter.FullName, "001.png");
            var sidecar = Path.Combine(chapter.FullName, "ComicInfo.xml");
            var unrelated = Path.Combine(chapter.FullName, "payload.bin");
            WritePng(page, SKColors.Purple);
            await File.WriteAllTextAsync(sidecar, "<ComicInfo />");
            await File.WriteAllBytesAsync(unrelated, [1, 2, 3]);

            var result = await Create(dataRoot.FullName).NormalizeAsync(
                Root(sourceRoot.FullName, scanImages: false),
                new HashSet<string>(),
                CancellationToken.None);

            Assert.Empty(result.Archives);
            Assert.Equal(
                [page, sidecar, unrelated],
                result.FailedPaths.OrderBy(path => path, StringComparer.Ordinal).ToArray());
            Assert.False(Directory.Exists(Path.Combine(dataRoot.FullName, "generated-sources")));
        } finally {
            sourceRoot.Delete(recursive: true);
            dataRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ExplicitBoundaryRejectsTruncatedImageThatCannotFullyDecode() {
        var sourceRoot = Directory.CreateTempSubdirectory("prismedia-loose-comic-");
        var dataRoot = Directory.CreateTempSubdirectory("prismedia-loose-comic-data-");
        try {
            var chapter = Directory.CreateDirectory(Path.Combine(sourceRoot.FullName, "Series", "Chapter 1"));
            var page = Path.Combine(chapter.FullName, "001.png");
            var sidecar = Path.Combine(chapter.FullName, "ComicInfo.xml");
            WritePng(page, SKColors.Purple);
            var completeBytes = await File.ReadAllBytesAsync(page);
            await File.WriteAllBytesAsync(page, completeBytes[..Math.Min(32, completeBytes.Length)]);
            await File.WriteAllTextAsync(sidecar, "<ComicInfo />");

            var result = await Create(dataRoot.FullName).NormalizeAsync(
                Root(sourceRoot.FullName, scanImages: false),
                new HashSet<string>(),
                CancellationToken.None);

            Assert.Empty(result.Archives);
            Assert.Contains(page, result.FailedPaths);
            Assert.False(Directory.Exists(Path.Combine(dataRoot.FullName, "generated-sources")));
        } finally {
            sourceRoot.Delete(recursive: true);
            dataRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PruneRemovesOnlyObsoleteManagedFilesAndNeverOriginalPages() {
        var sourceRoot = Directory.CreateTempSubdirectory("prismedia-loose-comic-");
        var dataRoot = Directory.CreateTempSubdirectory("prismedia-loose-comic-data-");
        try {
            var chapter = Directory.CreateDirectory(Path.Combine(sourceRoot.FullName, "Series", "Chapter 1"));
            var page = Path.Combine(chapter.FullName, "001.png");
            WritePng(page, SKColors.Orange);
            var root = Root(sourceRoot.FullName, scanImages: false);
            var normalizer = Create(dataRoot.FullName);
            var normalized = Assert.Single((await normalizer.NormalizeAsync(
                root,
                new HashSet<string>(),
                CancellationToken.None)).Archives);

            await normalizer.PruneAsync(
                root.Id,
                new HashSet<string>(),
                CancellationToken.None);

            Assert.False(File.Exists(normalized.ArchivePath));
            Assert.False(File.Exists(normalized.ArchivePath + ".source.sha256"));
            Assert.True(File.Exists(page));
        } finally {
            sourceRoot.Delete(recursive: true);
            dataRoot.Delete(recursive: true);
        }
    }

    private static ComicFolderNormalizer Create(string dataPath) =>
        new(new ManagedGeneratedSourceRoot(dataPath), NullLogger<ComicFolderNormalizer>.Instance);

    private static LibraryRootData Root(string path, bool scanImages) =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            path,
            "Comics",
            Enabled: true,
            Recursive: true,
            ScanVideos: false,
            ScanImages: scanImages,
            ScanAudio: false,
            ScanBooks: true,
            IsNsfw: false);

    private static void WritePng(string path, SKColor color) {
        using var bitmap = new SKBitmap(4, 4);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
