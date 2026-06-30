using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Exercises the destructive owned-file swap against the real filesystem (temp directories). The load-bearing
/// guarantees: a same-extension upgrade replaces the file in place and KEEPS the original as a recoverable
/// backup; a format change is refused (manual); and the owned file is never lost.
/// </summary>
public sealed class OwnedFileReplacerTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "prismedia-replacer-" + Guid.NewGuid().ToString("N"));
    private readonly OwnedFileReplacer _replacer = new(NullLogger<OwnedFileReplacer>.Instance);

    [Fact]
    public async Task SameExtensionUpgradeReplacesInPlaceAndKeepsABackup() {
        var library = Dir("library");
        var download = Dir("download");
        var owned = WriteFile(library, "Book.epub", "old web copy");
        WriteFile(download, "Book.retail.epub", "new retail copy, larger");

        var result = await _replacer.ReplaceAsync(library, download, BookFormatTier.Reflowable, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(owned, result.SwappedPath);
        Assert.Equal("new retail copy, larger", File.ReadAllText(owned)); // the better file is now at the owned path
        Assert.True(File.Exists(owned + ".prismedia-bak"));               // the original is preserved
        Assert.Equal("old web copy", File.ReadAllText(owned + ".prismedia-bak"));
    }

    [Fact]
    public async Task FormatChangeIsRefusedAndOwnedFileUntouched() {
        var library = Dir("library");
        var download = Dir("download");
        var owned = WriteFile(library, "Book.pdf", "owned pdf");
        WriteFile(download, "Book.epub", "incoming epub");

        var result = await _replacer.ReplaceAsync(library, download, BookFormatTier.Fixed, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("owned pdf", File.ReadAllText(owned)); // untouched
        Assert.False(File.Exists(owned + ".prismedia-bak"));
    }

    [Fact]
    public async Task MissingOwnedFileFails() {
        var library = Dir("library"); // empty
        var download = Dir("download");
        WriteFile(download, "Book.epub", "incoming");

        var result = await _replacer.ReplaceAsync(library, download, BookFormatTier.Reflowable, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AmbiguousOwnedFolderFails() {
        var library = Dir("library");
        var download = Dir("download");
        WriteFile(library, "Book.epub", "a");
        WriteFile(library, "Other.epub", "b"); // two importable files → ambiguous
        WriteFile(download, "New.epub", "c");

        var result = await _replacer.ReplaceAsync(library, download, BookFormatTier.Reflowable, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    private string Dir(string name) {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteFile(string dir, string name, string content) {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose() {
        if (Directory.Exists(_root)) {
            Directory.Delete(_root, recursive: true);
        }
    }
}
