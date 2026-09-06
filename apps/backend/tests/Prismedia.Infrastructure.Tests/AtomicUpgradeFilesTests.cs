using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

public sealed class AtomicUpgradeFilesTests {
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

    private sealed class Fixture : IDisposable {
        private readonly string root = Directory.CreateTempSubdirectory("atomic-replacement-").FullName;
        public AtomicUpgradeFiles Files { get; } = new(new OwnedFileReplacer(new MergedImportTestSupport.NoRecycleBin(),
            NullLogger<OwnedFileReplacer>.Instance));
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
