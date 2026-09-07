using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Domain.Entities;
using Prismedia.Application.Acquisition;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Media.Sidecars;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Exercises the destructive owned-file swap against the real filesystem (temp directories). The load-bearing
/// guarantees: a same-extension upgrade replaces the file in place and KEEPS the original as a recoverable
/// backup; a format change is refused (manual); and the owned file is never lost.
/// </summary>
public sealed class OwnedFileReplacerTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "prismedia-replacer-" + Guid.NewGuid().ToString("N"));
    private readonly OwnedFileReplacer _replacer = new(new BinOff(), NullLogger<OwnedFileReplacer>.Instance, new TestVideoPayloadVerifier());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlacementRejectsStaleOrUnrelatedFullDecodeEvidence(bool differentFile) {
        var owned = WriteFile(Dir("library"), "Owned.mkv", "owned bytes");
        var incoming = WriteFile(Dir("download"), "Incoming.mkv", "incoming bytes");
        var other = WriteFile(Dir("other"), "Other.mkv", "incoming bytes");
        var verifier = new TestVideoPayloadVerifier();
        var verification = await VerifiedVideoPayload.VerifyAsync(verifier, incoming, default);
        Assert.NotNull(verification.Verified);
        if (!differentFile) await File.WriteAllTextAsync(incoming, "changed candidate bytes");
        var result = await _replacer.ReplaceRetainingBackupAsync(owned, differentFile ? other : incoming,
            BookFormatTier.Unknown, default, EntityKind.Movie, verifiedVideo: verification.Verified);
        Assert.False(result.Succeeded);
        Assert.Equal("owned bytes", await File.ReadAllTextAsync(owned));
        Assert.True(File.Exists(incoming));
        Assert.True(File.Exists(other));
        Assert.False(File.Exists(owned + ".prismedia-bak"));
    }

    [Fact]
    public async Task VideoChangedWhileDecodingCannotProduceReusableVerificationEvidence() {
        var incoming = WriteFile(Dir("download"), "Incoming.mkv", "initial bytes");
        var verifier = new TestVideoPayloadVerifier { BeforeResult = () => File.WriteAllTextAsync(incoming, "changed candidate bytes") };
        var result = await VerifiedVideoPayload.VerifyAsync(verifier, incoming, default);
        Assert.Null(result.Verified);
        Assert.NotNull(result.FailureReason);
        Assert.True(File.Exists(incoming));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedVideoChangedDuringDecodeIsPreserved(bool retainBackup) {
        var owned = WriteFile(Dir("library"), "Owned.mkv", "old owned bytes");
        var incoming = WriteFile(Dir("download"), "Incoming.mkv", "verified candidate bytes");
        var verifier = new TestVideoPayloadVerifier { BeforeResult = () => File.WriteAllTextAsync(owned, "newer independently repaired owned bytes") };
        var replacer = new OwnedFileReplacer(new BinOff(), NullLogger<OwnedFileReplacer>.Instance, verifier);

        var result = retainBackup
            ? await replacer.ReplaceRetainingBackupAsync(owned, incoming, BookFormatTier.Unknown, default, EntityKind.Movie)
            : await replacer.ReplaceAsync(owned, incoming, BookFormatTier.Unknown, default, EntityKind.Movie);

        Assert.False(result.Succeeded);
        Assert.Equal("newer independently repaired owned bytes", await File.ReadAllTextAsync(owned));
        Assert.Equal("verified candidate bytes", await File.ReadAllTextAsync(incoming));
        Assert.False(File.Exists(owned + ".prismedia-bak"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacementDestinationCreatedDuringDecodeIsPreserved(bool evidenceFile) {
        var owned = WriteFile(Dir("library"), "Owned.mkv", "owned bytes");
        var incoming = WriteFile(Dir("download"), "Incoming.mp4", "verified candidate bytes");
        var destination = evidenceFile ? owned + ".attempt-evidence" : Path.ChangeExtension(owned, ".mp4");
        var verifier = new TestVideoPayloadVerifier { BeforeResult = () => File.WriteAllTextAsync(destination, "independent file bytes") };
        var replacer = new OwnedFileReplacer(new BinOff(), NullLogger<OwnedFileReplacer>.Instance, verifier);

        var result = await replacer.ReplaceRetainingBackupAsync(owned, incoming, BookFormatTier.Unknown, default,
            EntityKind.Movie, allowFormatChange: true, incomingEvidencePath: evidenceFile ? destination : null);

        Assert.False(result.Succeeded);
        Assert.Equal("independent file bytes", await File.ReadAllTextAsync(destination));
        Assert.Equal("owned bytes", await File.ReadAllTextAsync(owned));
        Assert.Equal("verified candidate bytes", await File.ReadAllTextAsync(incoming));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedDecodeCannotStageOrReplaceOwnedVideo(bool formatChange) {
        var owned = WriteFile(Dir("library"), "Owned.mkv", "healthy owned bytes");
        var incoming = WriteFile(Dir("download"), formatChange ? "Incoming.mp4" : "Incoming.mkv", "damaged candidate bytes");
        var verifier = new TestVideoPayloadVerifier("The downloaded video could not be decoded completely.");
        var replacer = new OwnedFileReplacer(new BinOff(), NullLogger<OwnedFileReplacer>.Instance, verifier);

        var result = await replacer.ReplaceAsync(owned, incoming, BookFormatTier.Unknown, default,
            EntityKind.Movie, allowFormatChange: formatChange);

        Assert.False(result.Succeeded);
        Assert.Single(verifier.Paths);
        Assert.Equal("healthy owned bytes", await File.ReadAllTextAsync(owned));
        Assert.Equal("damaged candidate bytes", await File.ReadAllTextAsync(incoming));
        Assert.False(File.Exists(owned + ".prismedia-new"));
        Assert.False(File.Exists(owned + ".prismedia-bak"));
    }

    [Theory]
    [InlineData(EntityKind.Movie, ".mkv")]
    [InlineData(EntityKind.Book, ".epub")]
    public async Task CancellationBeforeReplacementPreservesBothFiles(EntityKind kind, string extension) {
        var owned = WriteFile(Dir("library"), "Owned" + extension, "owned bytes");
        var incoming = WriteFile(Dir("download"), "Incoming" + extension, "candidate bytes");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _replacer.ReplaceAsync(owned, incoming, BookFormatTier.Unknown, cancellation.Token, kind));

        Assert.Equal("owned bytes", await File.ReadAllTextAsync(owned));
        Assert.Equal("candidate bytes", await File.ReadAllTextAsync(incoming));
        Assert.False(File.Exists(owned + ".prismedia-new"));
        Assert.False(File.Exists(owned + ".prismedia-bak"));
    }

    [Fact]
    public async Task FailedInstallReturnsTheDownloadedCandidateForRetry() {
        var owned = WriteFile(Dir("library"), "Owned.mkv", "owned bytes");
        var incoming = WriteFile(Dir("download"), "Incoming.mp4", "downloaded upgrade bytes");
        Directory.CreateDirectory(Path.ChangeExtension(owned, ".mp4"));

        var result = await _replacer.ReplaceAsync(owned, incoming, BookFormatTier.Unknown, default,
            EntityKind.Movie, allowFormatChange: true);

        Assert.False(result.Succeeded);
        Assert.Equal("owned bytes", await File.ReadAllTextAsync(owned));
        Assert.True(File.Exists(incoming));
        Assert.Equal("downloaded upgrade bytes", await File.ReadAllTextAsync(incoming));
    }

    [Theory]
    [InlineData(EntityKind.Movie, ".mkv")]
    [InlineData(EntityKind.Book, ".epub")]
    public async Task FailedBackupStagingReturnsTheDownloadedCandidateForRetry(EntityKind kind, string extension) {
        var owned = WriteFile(Dir("library"), "Owned" + extension, "owned bytes");
        var incoming = WriteFile(Dir("download"), "Incoming" + extension, "downloaded upgrade bytes");
        Directory.CreateDirectory(owned + ".prismedia-bak");

        var result = await _replacer.ReplaceAsync(owned, incoming, BookFormatTier.Unknown, default, kind);

        Assert.False(result.Succeeded);
        Assert.Equal("owned bytes", await File.ReadAllTextAsync(owned));
        Assert.True(File.Exists(incoming));
        Assert.Equal("downloaded upgrade bytes", await File.ReadAllTextAsync(incoming));
        Assert.False(File.Exists(owned + ".prismedia-new"));
    }

    [Theory]
    [InlineData(EntityKind.Movie, ".mkv")]
    [InlineData(EntityKind.Book, ".epub")]
    public async Task ExistingStagedCandidateIsRetainedInsteadOfOverwritten(EntityKind kind, string extension) {
        var owned = WriteFile(Dir("library"), "Owned" + extension, "owned bytes");
        var incoming = WriteFile(Dir("download"), "Incoming" + extension, "current candidate bytes");
        await File.WriteAllTextAsync(owned + ".prismedia-new", "previous interrupted candidate bytes");

        var result = await _replacer.ReplaceAsync(owned, incoming, BookFormatTier.Unknown, default, kind);

        Assert.False(result.Succeeded);
        Assert.Equal("owned bytes", await File.ReadAllTextAsync(owned));
        Assert.Equal("current candidate bytes", await File.ReadAllTextAsync(incoming));
        Assert.Equal("previous interrupted candidate bytes", await File.ReadAllTextAsync(owned + ".prismedia-new"));
    }

    [Theory]
    [InlineData(EntityKind.Movie, ".mkv")]
    [InlineData(EntityKind.Book, ".epub")]
    public async Task RecycleFailureDoesNotRollBackAnInstalledUpgrade(EntityKind kind, string extension) {
        var owned = WriteFile(Dir("library"), "Owned" + extension, "owned bytes");
        var incoming = WriteFile(Dir("download"), "Incoming" + extension, "downloaded upgrade bytes");
        var replacer = new OwnedFileReplacer(new FailingBin(), NullLogger<OwnedFileReplacer>.Instance, new TestVideoPayloadVerifier());

        var result = await replacer.ReplaceAsync(owned, incoming, BookFormatTier.Unknown, default, kind);

        Assert.True(result.Succeeded);
        Assert.Equal("downloaded upgrade bytes", await File.ReadAllTextAsync(owned));
        Assert.Equal("owned bytes", await File.ReadAllTextAsync(owned + ".prismedia-bak"));
    }

    private sealed class FailingBin : Prismedia.Application.Acquisition.IRecycleBin {
        public Task<string?> TryMoveToBinAsync(string path, CancellationToken cancellationToken) => throw new IOException("Injected recycle failure");
        public Task<int> CleanupAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    [Fact]
    public async Task SameExtensionUpgradeReplacesInPlaceAndKeepsABackup() {
        var library = Dir("library");
        var download = Dir("download");
        var owned = WriteFile(library, "Book.epub", "old web copy");
        WriteFile(download, "Book.retail.epub", "new retail copy, larger");

        var result = await _replacer.ReplaceAsync(library, download, BookFormatTier.Reflowable, CancellationToken.None, EntityKind.Book);

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

        var result = await _replacer.ReplaceAsync(library, download, BookFormatTier.Fixed, CancellationToken.None, EntityKind.Book);

        Assert.False(result.Succeeded);
        Assert.Equal("owned pdf", File.ReadAllText(owned)); // untouched
        Assert.False(File.Exists(owned + ".prismedia-bak"));
    }

    [Fact]
    public async Task MissingOwnedFileFails() {
        var library = Dir("library"); // empty
        var download = Dir("download");
        WriteFile(download, "Book.epub", "incoming");

        var result = await _replacer.ReplaceAsync(library, download, BookFormatTier.Reflowable, CancellationToken.None, EntityKind.Book);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AmbiguousOwnedFolderFails() {
        var library = Dir("library");
        var download = Dir("download");
        WriteFile(library, "Book.epub", "a");
        WriteFile(library, "Other.epub", "b"); // two importable files → ambiguous
        WriteFile(download, "New.epub", "c");

        var result = await _replacer.ReplaceAsync(library, download, BookFormatTier.Reflowable, CancellationToken.None, EntityKind.Book);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task VideoSameExtensionUpgradeReplacesInPlaceAndKeepsABackup() {
        var library = Dir("library");
        var download = Dir("download");
        var owned = WriteFile(library, "Movie (2020).mkv", "old 720p copy");
        WriteFile(download, "Movie.2020.1080p.BluRay.mkv", "new 1080p copy, larger");
        var sample = WriteFile(download, "Movie.2020.sample.mkv", "sample bytes");

        // Video passes a pass-through format tier; the kind selects the video file finder and swap rules.
        var result = await _replacer.ReplaceAsync(library, download, BookFormatTier.Unknown, CancellationToken.None, EntityKind.Movie);
        Assert.Equal("sample bytes", await File.ReadAllTextAsync(sample));

        Assert.True(result.Succeeded);
        Assert.Equal(owned, result.SwappedPath);
        Assert.Equal("new 1080p copy, larger", File.ReadAllText(owned)); // the better file is now at the owned path
        Assert.True(File.Exists(owned + ".prismedia-bak"));               // the original is preserved
        Assert.Equal("old 720p copy", File.ReadAllText(owned + ".prismedia-bak"));
    }

    [Fact]
    public async Task VideoUpgradeCarriesAdjacentSubtitleSidecarsOntoTheOwnedBasename() {
        var library = Dir("library-sidecars");
        var download = Dir("download-sidecars");
        WriteFile(library, "Movie (2020).mkv", "old copy");
        WriteFile(library, "Movie (2020).eng.srt", "old subtitle bytes");
        WriteFile(download, "Release.1080p.mkv", "new copy");
        WriteFile(download, "Release.1080p.eng.srt", "subtitle bytes");
        var replacer = new OwnedFileReplacer(
            new BinOff(),
            NullLogger<OwnedFileReplacer>.Instance, new TestVideoPayloadVerifier(),
            new SubtitleSidecarDiscovery());

        var result = await replacer.ReplaceAsync(
            library,
            download,
            BookFormatTier.Unknown,
            CancellationToken.None,
            EntityKind.Movie);

        Assert.True(result.Succeeded);
        var carried = Path.Combine(library, "Movie (2020).eng.srt");
        Assert.True(File.Exists(carried));
        Assert.Equal("subtitle bytes", File.ReadAllText(carried));
        Assert.Equal("old subtitle bytes", File.ReadAllText(carried + ".prismedia-bak"));
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
        var replacer = new OwnedFileReplacer(bin, NullLogger<OwnedFileReplacer>.Instance, new TestVideoPayloadVerifier());
        var result = await replacer.ReplaceAsync(library, download, Prismedia.Domain.Entities.BookFormatTier.Unknown, CancellationToken.None, EntityKind.Book);

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
