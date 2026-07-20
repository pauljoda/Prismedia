using System.IO.Compression;
using Prismedia.Application.Acquisition;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

public sealed class LocalAcquisitionUploadStorageTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"prismedia-upload-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task ZipContainingMediaIsExpandedIntoTheCompletedPayload() {
        var zip = Zip(("folder/movie.mkv", "video"), ("folder/movie.srt", "subtitle"));
        var storage = new LocalAcquisitionUploadStorage(new AcquisitionUploadStorageOptions(_root));

        var completed = await storage.StageAsync(
            Guid.NewGuid(),
            [new AcquisitionUploadItem("release.zip", zip)],
            CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(completed.ContentPath, "folder", "movie.mkv")));
        Assert.False(File.Exists(Path.Combine(completed.ContentPath, "release.zip")));
    }

    [Fact]
    public async Task ZipEntryCannotEscapeTheUploadBoundary() {
        var zip = Zip(("../escape.mkv", "video"));
        var storage = new LocalAcquisitionUploadStorage(new AcquisitionUploadStorageOptions(_root));

        await Assert.ThrowsAsync<InvalidDataException>(() => storage.StageAsync(
            Guid.NewGuid(),
            [new AcquisitionUploadItem("release.zip", zip)],
            CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(_root, "escape.mkv")));
    }

    [Fact]
    public async Task ImageOnlyComicZipStaysPackagedForTheBookImporter() {
        var zip = Zip(("001.jpg", "image"), ("002.jpg", "image"));
        var storage = new LocalAcquisitionUploadStorage(new AcquisitionUploadStorageOptions(_root));

        var completed = await storage.StageAsync(
            Guid.NewGuid(),
            [new AcquisitionUploadItem("comic.zip", zip)],
            CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(completed.ContentPath, "comic.zip")));
    }

    private static MemoryStream Zip(params (string Path, string Content)[] entries) {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) {
            foreach (var item in entries) {
                using var writer = new StreamWriter(archive.CreateEntry(item.Path).Open());
                writer.Write(item.Content);
            }
        }
        stream.Position = 0;
        return stream;
    }

    public void Dispose() {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
