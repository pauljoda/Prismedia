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
    private readonly OwnedFileReplacer _replacer = new(new BinOff(), NullLogger<OwnedFileReplacer>.Instance);

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

    [Fact]
    public async Task VideoSameExtensionUpgradeReplacesInPlaceAndKeepsABackup() {
        var library = Dir("library");
        var download = Dir("download");
        var owned = WriteFile(library, "Movie (2020).mkv", "old 720p copy");
        WriteFile(download, "Movie.2020.1080p.BluRay.mkv", "new 1080p copy, larger");

        // Video passes a pass-through format tier; the kind selects the video file finder and swap rules.
        var result = await _replacer.ReplaceAsync(library, download, BookFormatTier.Unknown, CancellationToken.None, EntityKind.Movie);

        Assert.True(result.Succeeded);
        Assert.Equal(owned, result.SwappedPath);
        Assert.Equal("new 1080p copy, larger", File.ReadAllText(owned)); // the better file is now at the owned path
        Assert.True(File.Exists(owned + ".prismedia-bak"));               // the original is preserved
        Assert.Equal("old 720p copy", File.ReadAllText(owned + ".prismedia-bak"));
    }

    [Fact]
    public async Task WindowsExtensionCaseChangeDoesNotDeleteTheInstalledUpgrade() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        var library = Dir("library-case");
        var download = Dir("download-case");
        WriteFile(library, "Movie (2020).MKV", "old copy");
        WriteFile(download, "Movie.2020.1080p.mkv", "new copy");

        var result = await _replacer.ReplaceAsync(
            library,
            download,
            BookFormatTier.Unknown,
            CancellationToken.None,
            EntityKind.Movie);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.SwappedPath);
        Assert.True(File.Exists(result.SwappedPath));
        Assert.Equal("new copy", File.ReadAllText(result.SwappedPath));
    }

    [Fact]
    public async Task VideoFormatChangeIsRefusedAndOwnedFileUntouched() {
        var library = Dir("library");
        var download = Dir("download");
        var owned = WriteFile(library, "Movie (2020).mkv", "owned mkv");
        WriteFile(download, "Movie.2020.1080p.mp4", "incoming mp4");

        // An mkv → mp4 upgrade is refused (same reason as books: entity/playback-progress continuity).
        var result = await _replacer.ReplaceAsync(library, download, BookFormatTier.Unknown, CancellationToken.None, EntityKind.Video);

        Assert.False(result.Succeeded);
        Assert.Equal("owned mkv", File.ReadAllText(owned)); // untouched
        Assert.False(File.Exists(owned + ".prismedia-bak"));
    }

    [Fact]
    public async Task ConsentedFormatChangeInstallsUnderTheOwnedBasenameAndRetiresTheOldFile() {
        var library = Dir("library");
        var download = Dir("download");
        var owned = WriteFile(library, "Movie (2020).mkv", "owned mkv");
        WriteFile(download, "Movie.2020.2160p.mp4", "incoming 2160p mp4");

        // The user's explicit "import anyway": mkv → mp4 installs at the owned basename with the new
        // extension, the old file is retired, and the previous copy stays recoverable as the backup.
        var result = await _replacer.ReplaceAsync(
            library, download, BookFormatTier.Unknown, CancellationToken.None, EntityKind.Video, allowFormatChange: true);

        var installed = Path.Combine(library, "Movie (2020).mp4");
        Assert.True(result.Succeeded);
        Assert.Equal(installed, result.SwappedPath);
        Assert.Equal("incoming 2160p mp4", File.ReadAllText(installed));
        Assert.False(File.Exists(owned));                    // the old-format file is retired
        Assert.True(File.Exists(owned + ".prismedia-bak"));  // and stays recoverable
        Assert.Equal("owned mkv", File.ReadAllText(owned + ".prismedia-bak"));
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

    [Fact]
    public async Task WithARecycleBinTheBackupIsHandedOffInsteadOfLingering() {
        var library = Dir("library");
        var download = Dir("download");
        var owned = Path.Combine(library, "book.epub");
        await File.WriteAllTextAsync(owned, "old");
        var incoming = Path.Combine(download, "book.epub");
        await File.WriteAllTextAsync(incoming, "new-better");

        var bin = new CapturingBin();
        var replacer = new OwnedFileReplacer(bin, NullLogger<OwnedFileReplacer>.Instance);
        var result = await replacer.ReplaceAsync(library, download, Prismedia.Domain.Entities.BookFormatTier.Unknown, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("new-better", await File.ReadAllTextAsync(owned));
        // The bin took the backup, so no .prismedia-bak lingers beside the upgraded file.
        Assert.Single(bin.Binned);
        Assert.False(File.Exists(owned + ".prismedia-bak"));
        Assert.Equal("old", await File.ReadAllTextAsync(bin.Binned[0]));
    }

    /// <summary>A bin that accepts everything, moving files into a temp folder like the real one would.</summary>
    private sealed class CapturingBin : Prismedia.Application.Acquisition.IRecycleBin {
        public List<string> Binned { get; } = [];
        public Task<string?> TryMoveToBinAsync(string filePath, CancellationToken cancellationToken) {
            var target = Path.Combine(Path.GetTempPath(), "prismedia-bin-" + Guid.NewGuid().ToString("N") + Path.GetExtension(filePath));
            File.Move(filePath, target);
            Binned.Add(target);
            return Task.FromResult<string?>(target);
        }
        public Task<int> CleanupAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class BinOff : Prismedia.Application.Acquisition.IRecycleBin {
        public Task<string?> TryMoveToBinAsync(string filePath, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<int> CleanupAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
