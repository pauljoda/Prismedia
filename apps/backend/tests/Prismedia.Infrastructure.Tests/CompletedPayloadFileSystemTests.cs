using System.Security.Cryptography;
using System.Text;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

public sealed class CompletedPayloadFileSystemTests : IDisposable {
    private readonly string root = Path.Combine(Path.GetTempPath(), "payload-cleanup-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void RemovesCompletedFilesAndCompanionsWithoutChangingTheLibraryCopy() {
        var payload = FileAt("downloads/item/film.mkv");
        FileAt("downloads/item/extras/proof.jpg");
        var owned = FileAt("library/film.mkv");

        CompletedPayloadFileSystem.Delete(Path.GetDirectoryName(payload)!, "receipt", [Path.Combine(root, "library")], [owned], default);

        Assert.False(Directory.Exists(Path.GetDirectoryName(payload)));
        Assert.Equal("validated", File.ReadAllText(owned));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(root, "downloads")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingOrEmptyLibraryCopiesKeepTheRetainedPayload(bool missing) {
        var payload = FileAt("downloads/item/film.mkv");
        var owned = FileAt("library/film.mkv");
        if (missing) File.Delete(owned); else File.WriteAllText(owned, "");

        Assert.Throws<IOException>(() => CompletedPayloadFileSystem.Delete(Path.GetDirectoryName(payload)!, "receipt",
            [Path.Combine(root, "library")], [owned], default));

        Assert.True(File.Exists(payload));
    }

    [Fact]
    public void LibraryRootOverlapProtectsBothAncestorsAndDescendants() {
        var owned = FileAt("library/item/film.mkv");
        foreach (var payload in new[] { root, Path.Combine(root, "library"), Path.Combine(root, "library/item") }) {
            Assert.Throws<IOException>(() => CompletedPayloadFileSystem.Delete(payload, "receipt", [Path.Combine(root, "library")], [], default));
        }
        Assert.True(File.Exists(owned));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PayloadAndNestedDirectoryLinksNeverReachTheLibrary(bool payloadLink) {
        var owned = FileAt("library/film.mkv");
        var payload = Path.Combine(root, "downloads/item");
        Directory.CreateDirectory(Path.GetDirectoryName(payload)!);
        if (payloadLink) Directory.CreateSymbolicLink(payload, Path.Combine(root, "library"));
        else {
            Directory.CreateDirectory(payload);
            Directory.CreateSymbolicLink(Path.Combine(payload, "linked"), Path.Combine(root, "library"));
        }

        Assert.Throws<IOException>(() => CompletedPayloadFileSystem.Delete(payload, "receipt", [Path.Combine(root, "library")], [], default));
        Assert.True(File.Exists(owned));
    }

    [Fact]
    public void ASourceSymlinkIntoThePayloadDoesNotCountAsAnIndependentLibraryCopy() {
        var payload = FileAt("downloads/item/film.mkv");
        Directory.CreateDirectory(Path.Combine(root, "library"));
        var owned = Path.Combine(root, "library/film.mkv");
        File.CreateSymbolicLink(owned, payload);

        Assert.Throws<IOException>(() => CompletedPayloadFileSystem.Delete(Path.GetDirectoryName(payload)!, "receipt",
            [Path.Combine(root, "library")], [owned], default));
        Assert.True(File.Exists(payload));
    }

    [Fact]
    public void SingleFileCleanupAndRepeatedCleanupAreIdempotent() {
        var payload = FileAt("downloads/film.mkv");
        var owned = FileAt("library/film.mkv");
        for (var attempt = 0; attempt < 2; attempt++) {
            CompletedPayloadFileSystem.Delete(payload, "receipt", [Path.Combine(root, "library")], [owned], default);
        }
        Assert.False(File.Exists(payload));
        Assert.True(File.Exists(owned));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InterruptedCleanupResumesUnlessANewPayloadHasReusedTheOriginalPath(bool newPayload) {
        var original = Path.GetDirectoryName(FileAt("downloads/item/film.mkv"))!;
        var owned = FileAt("library/film.mkv");
        // Reproduce the durable on-disk state left by a process ending immediately after staging.
        var staged = original + ".prismedia-cleanup-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("receipt")))[..16];
        Directory.Move(original, staged);
        if (newPayload) FileAt("downloads/item/new.mkv");
        var cleanup = () => CompletedPayloadFileSystem.Delete(original, "receipt", [Path.Combine(root, "library")], [owned], default);

        if (newPayload) {
            Assert.Throws<IOException>(cleanup);
            Assert.True(File.Exists(Path.Combine(original, "new.mkv")));
            Assert.True(File.Exists(Path.Combine(staged, "film.mkv")));
        } else {
            cleanup();
            Assert.False(Directory.Exists(staged));
        }
        Assert.Equal("validated", File.ReadAllText(owned));
    }

    private string FileAt(string path) {
        var full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "validated");
        return full;
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}
