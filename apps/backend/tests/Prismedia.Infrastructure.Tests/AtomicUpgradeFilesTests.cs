using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

public sealed class AtomicUpgradeFilesTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtensionCapitalizationPreservesTheExactOwnedPath(bool allowFormatChange) {
        var root = Directory.CreateTempSubdirectory("atomic-extension-case-").FullName;
        try {
            var owned = Path.Combine(root, "owned.MKV");
            var incoming = Path.Combine(root, "incoming.mkv");
            await File.WriteAllTextAsync(owned, "original video");
            await File.WriteAllTextAsync(incoming, "new video");
            var verifier = new TestVideoPayloadVerifier();
            var bin = new MergedImportTestSupport.NoRecycleBin();
            var files = new AtomicUpgradeFiles(new OwnedFileReplacer(bin,
                NullLogger<OwnedFileReplacer>.Instance, verifier), bin, verifier);
            var parent = Guid.NewGuid();
            var entity = Guid.NewGuid();
            var target = new UpgradeReplaceTarget(parent, entity, owned, default, "Movie 1080p WEB-DL", incoming,
                "transfer", null, EntityKind.Movie);
            var plan = await files.PrepareAsync(target, default, allowFormatChange);
            var checkpoint = new AtomicUpgradeCheckpoint(Guid.NewGuid(), Guid.NewGuid(), parent, entity, Guid.NewGuid(),
                EntityKind.Movie, plan, incoming, "transfer", new(target.ChildSelectedTitle!, null, null));
            Assert.Equal(owned, plan.InstallPath);
            var installed = await files.ReplaceAsync(checkpoint, default);
            Assert.True(installed.Succeeded);
            Assert.Equal(owned, installed.SwappedPath);
            Assert.Equal("new video", await File.ReadAllTextAsync(owned));
            var recovered = await files.RecoverAsync(checkpoint, default);
            Assert.Null(recovered.HoldReason);
            Assert.Equal(owned, recovered.Installed!.SwappedPath);
        } finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task VideoContainerChangeNeedsPermissionAndAnEmptyDestination(bool allowed, bool occupied) {
        var root = Directory.CreateTempSubdirectory("atomic-video-format-").FullName;
        try {
            var owned = Path.Combine(root, "owned.avi");
            var incoming = Path.Combine(root, "incoming.mkv");
            var installed = Path.Combine(root, "owned.mkv");
            await File.WriteAllTextAsync(owned, "original video");
            await File.WriteAllTextAsync(incoming, "new video");
            if (occupied) await File.WriteAllTextAsync(installed, "independent video");
            var verifier = new TestVideoPayloadVerifier();
            var files = new AtomicUpgradeFiles(new OwnedFileReplacer(new MergedImportTestSupport.NoRecycleBin(),
                NullLogger<OwnedFileReplacer>.Instance, verifier), new MergedImportTestSupport.NoRecycleBin(), verifier);
            var parent = Guid.NewGuid();
            var entity = Guid.NewGuid();
            var target = new UpgradeReplaceTarget(parent, entity, owned, default, "Movie 1080p WEB-DL", incoming,
                "transfer", null, EntityKind.Movie);
            if (!allowed || occupied) {
                await Assert.ThrowsAsync<IOException>(() => files.PrepareAsync(target, default, allowed));
                Assert.Equal("original video", await File.ReadAllTextAsync(owned));
                Assert.Equal("new video", await File.ReadAllTextAsync(incoming));
                if (occupied) Assert.Equal("independent video", await File.ReadAllTextAsync(installed));
                Assert.Empty(verifier.Paths);
                return;
            }
            var plan = await files.PrepareAsync(target, default, allowed);
            var checkpoint = new AtomicUpgradeCheckpoint(Guid.NewGuid(), Guid.NewGuid(), parent, entity, Guid.NewGuid(),
                EntityKind.Movie, plan, incoming, "transfer", new(target.ChildSelectedTitle!, null, null));
            verifier.Failure = "Damaged video";
            Assert.False((await files.ReplaceAsync(checkpoint, default)).Succeeded);
            Assert.True(File.Exists(owned));
            Assert.True(File.Exists(incoming));
            Assert.False(File.Exists(installed));
            verifier.Failure = null;
            Assert.True((await files.ReplaceAsync(checkpoint, default)).Succeeded);
            // Simulate interruption after installing the new extension but before retiring the old one.
            File.Copy(checkpoint.BackupPath, owned);
            var recovered = await files.RecoverAsync(checkpoint, default);
            Assert.Null(recovered.HoldReason);
            Assert.Equal(installed, recovered.Installed!.SwappedPath);
            Assert.False(File.Exists(owned));
            Assert.Equal("new video", await File.ReadAllTextAsync(installed));
            await files.CompleteAsync(checkpoint, default);
            Assert.Equal("original video", await File.ReadAllTextAsync(OwnedFileReplacementArtifacts.BackupPath(owned)));
        } finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ConfiguredBinReceivesTheOriginalOnlyAfterInstallationCommits() {
        var bin = new RecordingBin();
        using var fixture = new Fixture(bin);
        var checkpoint = await fixture.PrepareAsync();
        Assert.True((await fixture.Files.ReplaceAsync(checkpoint, default)).Succeeded);
        Assert.Empty(bin.Received);
        Assert.Equal("old-owned", await File.ReadAllTextAsync(checkpoint.BackupPath));

        await fixture.Files.CompleteAsync(checkpoint, default);

        Assert.Equal(checkpoint.BackupPath, Assert.Single(bin.Received));
        Assert.Equal("old-owned", await File.ReadAllTextAsync(checkpoint.BackupPath + ".recycled"));
        Assert.False(File.Exists(checkpoint.BackupPath));
        Assert.False(File.Exists(OwnedFileReplacementArtifacts.BackupPath(checkpoint.Files.OwnedPath)));
        Assert.Equal("new-upgrade", await File.ReadAllTextAsync(checkpoint.Files.OwnedPath));
        await fixture.Files.CompleteAsync(checkpoint, default);
        Assert.Single(bin.Received);
    }

    [Fact]
    public async Task LateCleanupCannotReplaceANewerOwnedFilesStandardBackup() {
        using var fixture = new Fixture();
        var checkpoint = await fixture.PrepareAsync();
        Assert.True((await fixture.Files.ReplaceAsync(checkpoint, default)).Succeeded);
        var backup = OwnedFileReplacementArtifacts.BackupPath(checkpoint.Files.OwnedPath);
        await File.WriteAllTextAsync(backup, "newer-original");
        await File.WriteAllTextAsync(checkpoint.Files.OwnedPath, "another-installed-version");

        await fixture.Files.CompleteAsync(checkpoint, default);

        Assert.Equal("newer-original", await File.ReadAllTextAsync(backup));
        Assert.Equal("old-owned", await File.ReadAllTextAsync(checkpoint.BackupPath));
        Assert.Equal("another-installed-version", await File.ReadAllTextAsync(checkpoint.Files.OwnedPath));
    }

    [Fact]
    public async Task CommittedReplacementRetainsOnlyTheLatestOriginalAtTheStandardBackupPath() {
        using var fixture = new Fixture();
        var checkpoint = await fixture.PrepareAsync();
        var standardBackup = OwnedFileReplacementArtifacts.BackupPath(checkpoint.Files.OwnedPath);
        await File.WriteAllTextAsync(standardBackup, "older-original");
        Assert.True((await fixture.Files.ReplaceAsync(checkpoint, default)).Succeeded);
        // Housekeeping must wait until the caller commits the installation receipt.
        Assert.Equal("older-original", await File.ReadAllTextAsync(standardBackup));
        Assert.Equal("old-owned", await File.ReadAllTextAsync(checkpoint.BackupPath));

        await fixture.Files.CompleteAsync(checkpoint, default);

        Assert.Equal("old-owned", await File.ReadAllTextAsync(standardBackup));
        Assert.False(File.Exists(checkpoint.BackupPath));
        Assert.False(File.Exists(checkpoint.EvidencePath));
        Assert.Equal("new-upgrade", await File.ReadAllTextAsync(checkpoint.Files.OwnedPath));
    }

    [Fact]
    public async Task EvidenceFailureCannotConsumeTheOnlyDownloadedCandidate() {
        using var fixture = new Fixture();
        var checkpoint = await fixture.PrepareAsync();
        Directory.CreateDirectory(checkpoint.EvidencePath);
        Assert.False((await fixture.Files.ReplaceAsync(checkpoint, default)).Succeeded);
        Assert.Equal("new-upgrade", await File.ReadAllTextAsync(checkpoint.Files.IncomingPath));
        Assert.Equal("old-owned", await File.ReadAllTextAsync(checkpoint.Files.OwnedPath));
        Assert.False(File.Exists(OwnedFileReplacementArtifacts.StagedPath(checkpoint.Files.OwnedPath)));
    }

    [Fact]
    public async Task InterruptedCandidateHandoffCanResumeWhenAllCopiesAreProvenEqual() {
        using var fixture = new Fixture();
        var checkpoint = await fixture.PrepareAsync();
        File.Copy(checkpoint.Files.IncomingPath, checkpoint.EvidencePath);
        var staged = OwnedFileReplacementArtifacts.StagedPath(checkpoint.Files.OwnedPath);
        File.Copy(checkpoint.Files.IncomingPath, staged);
        var recovery = await fixture.Files.RecoverAsync(checkpoint, default);
        Assert.Null(recovery.HoldReason);
        Assert.Null(recovery.Installed);
        Assert.False(File.Exists(staged));
        Assert.Equal("new-upgrade", await File.ReadAllTextAsync(checkpoint.Files.IncomingPath));
    }

    [Fact]
    public async Task PreparationAndUntouchedRecoveryDoNotMutateEitherFile() {
        using var fixture = new Fixture();
        var checkpoint = await fixture.PrepareAsync();
        var recovery = await fixture.Files.RecoverAsync(checkpoint, default);
        Assert.Null(recovery.Installed);
        Assert.Null(recovery.HoldReason);
        Assert.Equal("old-owned", await File.ReadAllTextAsync(checkpoint.Files.OwnedPath));
        Assert.Equal("new-upgrade", await File.ReadAllTextAsync(checkpoint.Files.IncomingPath));
        Assert.False(File.Exists(checkpoint.BackupPath));
        Assert.False(File.Exists(checkpoint.EvidencePath));
    }

    [Fact]
    public async Task InstallationCanBeProvenAfterItsDownloadWasConsumed() {
        using var fixture = new Fixture();
        var checkpoint = await fixture.PrepareAsync();
        Assert.True((await fixture.Files.ReplaceAsync(checkpoint, default)).Succeeded);
        var recovered = await fixture.Files.RecoverAsync(checkpoint, default);
        Assert.Null(recovered.HoldReason);
        Assert.Equal(checkpoint.Files.OwnedPath, recovered.Installed?.SwappedPath);
        Assert.Equal("old-owned", await File.ReadAllTextAsync(checkpoint.BackupPath));
        Assert.False(File.Exists(checkpoint.Files.IncomingPath));
    }

    [Fact]
    public async Task ProvenStagedCandidateReturnsToItsDownloadBeforeRevalidation() {
        using var fixture = new Fixture();
        var checkpoint = await fixture.PrepareAsync();
        File.Copy(checkpoint.Files.IncomingPath, checkpoint.EvidencePath);
        File.Move(checkpoint.Files.IncomingPath, OwnedFileReplacementArtifacts.StagedPath(checkpoint.Files.OwnedPath));
        var recovery = await fixture.Files.RecoverAsync(checkpoint, default);
        Assert.Null(recovery.HoldReason);
        Assert.Null(recovery.Installed);
        Assert.Equal("new-upgrade", await File.ReadAllTextAsync(checkpoint.Files.IncomingPath));
        Assert.Equal("old-owned", await File.ReadAllTextAsync(checkpoint.Files.OwnedPath));
        Assert.True((await fixture.Files.ReplaceAsync(checkpoint, default)).Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnprovenStageIsRetainedWithoutChangingTheOwnedFile(bool conflictingIncoming) {
        using var fixture = new Fixture();
        var checkpoint = await fixture.PrepareAsync();
        var staged = OwnedFileReplacementArtifacts.StagedPath(checkpoint.Files.OwnedPath);
        File.Move(checkpoint.Files.IncomingPath, staged);
        if (conflictingIncoming) {
            File.Copy(staged, checkpoint.EvidencePath);
            await File.WriteAllTextAsync(checkpoint.Files.IncomingPath, "another-download");
        }
        var recovery = await fixture.Files.RecoverAsync(checkpoint, default);
        Assert.NotNull(recovery.HoldReason);
        Assert.Null(recovery.Installed);
        Assert.Equal("new-upgrade", await File.ReadAllTextAsync(staged));
        Assert.Equal("old-owned", await File.ReadAllTextAsync(checkpoint.Files.OwnedPath));
        if (conflictingIncoming) Assert.Equal("another-download", await File.ReadAllTextAsync(checkpoint.Files.IncomingPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedInstalledBytesCannotBeMistakenForTheCheckpointedUpgrade(bool preserveTimestamp) {
        using var fixture = new Fixture();
        var checkpoint = await fixture.PrepareAsync();
        Assert.True((await fixture.Files.ReplaceAsync(checkpoint, default)).Succeeded);
        // Replace the inode: incoming evidence can be a hardlink to the original installation.
        File.Delete(checkpoint.Files.OwnedPath);
        await File.WriteAllTextAsync(checkpoint.Files.OwnedPath, "bad-upgrade");
        if (preserveTimestamp) File.SetLastWriteTimeUtc(checkpoint.Files.OwnedPath, checkpoint.Files.Incoming.LastWriteTimeUtc);
        var recovery = await fixture.Files.RecoverAsync(checkpoint, default);
        Assert.NotNull(recovery.HoldReason);
        Assert.Null(recovery.Installed);
        Assert.Equal("bad-upgrade", await File.ReadAllTextAsync(checkpoint.Files.OwnedPath));
        Assert.Equal("old-owned", await File.ReadAllTextAsync(checkpoint.BackupPath));
    }

    [Fact]
    public async Task ChangedPreparationCannotOverwriteEitherCurrentFile() {
        using var fixture = new Fixture();
        var checkpoint = await fixture.PrepareAsync();
        await File.WriteAllTextAsync(checkpoint.Files.OwnedPath, "newly-edited-owned-file");
        Assert.False((await fixture.Files.ReplaceAsync(checkpoint, default)).Succeeded);
        Assert.Equal("newly-edited-owned-file", await File.ReadAllTextAsync(checkpoint.Files.OwnedPath));
        Assert.Equal("new-upgrade", await File.ReadAllTextAsync(checkpoint.Files.IncomingPath));
        Assert.False(File.Exists(checkpoint.BackupPath));
    }

    private sealed class RecordingBin : IRecycleBin {
        public List<string> Received { get; } = [];
        public Task<string?> TryMoveToBinAsync(string path, CancellationToken token) {
            Received.Add(path);
            File.Move(path, path + ".recycled");
            return Task.FromResult<string?>(path + ".recycled");
        }
        public Task<int> CleanupAsync(CancellationToken token) => Task.FromResult(0);
    }

    private sealed class Fixture(IRecycleBin? bin = null) : IDisposable {
        private readonly string root = Directory.CreateTempSubdirectory("atomic-replacement-").FullName;
        public AtomicUpgradeFiles Files { get; } = new(new OwnedFileReplacer(new MergedImportTestSupport.NoRecycleBin(),
            NullLogger<OwnedFileReplacer>.Instance, new TestVideoPayloadVerifier()), bin ?? new MergedImportTestSupport.NoRecycleBin(), new TestVideoPayloadVerifier());
        public async Task<AtomicUpgradeCheckpoint> PrepareAsync() {
            var owned = Path.Combine(root, "owned.epub");
            var incoming = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "download")).FullName, "incoming.epub");
            await File.WriteAllTextAsync(owned, "old-owned");
            await File.WriteAllTextAsync(incoming, "new-upgrade");
            var parent = Guid.NewGuid();
            var entity = Guid.NewGuid();
            var target = new UpgradeReplaceTarget(parent, entity, owned,
                new(BookSourceTier.Web, BookFormatTier.Reflowable), "Book (retail) (epub)", incoming,
                "transfer", null, EntityKind.Book);
            var plan = await Files.PrepareAsync(target, default);
            return new(Guid.NewGuid(), Guid.NewGuid(), parent, entity, Guid.NewGuid(), EntityKind.Book,
                plan, incoming, "transfer", new(target.ChildSelectedTitle!, null, null));
        }
        public void Dispose() => Directory.Delete(root, true);
    }
}
