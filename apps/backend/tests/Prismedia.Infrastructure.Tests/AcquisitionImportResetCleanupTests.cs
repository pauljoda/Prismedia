using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class AcquisitionImportResetCleanupTests {
    [Fact]
    public async Task PartialPlacementResetDeletesCheckpointFilesAndRestoresWantedCatalogState() {
        var boundary = TempBoundary();
        try {
            var payloadRoot = Path.Combine(boundary, "payload");
            var libraryRoot = Path.Combine(boundary, "library");
            var source = Path.Combine(payloadRoot, "Dune.epub");
            var target = Path.Combine(libraryRoot, "Frank Herbert", "Dune.epub");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(source, "download");
            await File.WriteAllTextAsync(target, "download");

            await using var db = CreateContext();
            var entityId = Guid.NewGuid();
            var rootId = Guid.NewGuid();
            AddEntity(db, entityId, isWanted: false);
            db.EntityFiles.Add(SourceFile(entityId, target));
            db.ScannedFiles.Add(new ScannedFileRow {
                LibraryRootId = rootId,
                ScanKind = JobType.ScanBook.ToCode(),
                Path = target,
                SizeBytes = 8,
                ModifiedTicks = 1,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();

            var checkpoint = new ImportPlacementCheckpoint(
                EntityKind.Book,
                rootId,
                libraryRoot,
                payloadRoot,
                ImportMode.Move,
                Path.GetDirectoryName(target)!,
                Path.GetDirectoryName(target)!,
                "Imported.",
                [new ImportPlacementCheckpointUnit("Dune.epub", source, target, true, target)],
                AttemptId: Guid.NewGuid(),
                ClaimJobId: Guid.NewGuid());
            var import = Context(entityId, payloadRoot, checkpoint);

            await Cleaner(db).CleanupAsync(import, CancellationToken.None);

            Assert.False(File.Exists(source));
            Assert.False(File.Exists(target));
            Assert.Empty(await db.EntityFiles.ToArrayAsync());
            Assert.Empty(await db.ScannedFiles.ToArrayAsync());
            Assert.True((await db.Entities.SingleAsync()).IsWanted);
        } finally {
            DeleteBoundary(boundary);
        }
    }

    [Fact]
    public async Task InterruptedUpgradeResetRestoresTheRetainedOriginalFile() {
        var boundary = TempBoundary();
        try {
            var payloadRoot = Path.Combine(boundary, "payload");
            var seriesRoot = Path.Combine(boundary, "library", "Show");
            var source = Path.Combine(payloadRoot, "Show.S01E01.mkv");
            var previous = Path.Combine(seriesRoot, "Season 01", "Show - S01E01.mkv");
            var attemptId = Guid.NewGuid();
            var backup = OwnedFileReplacementArtifacts.CheckpointBackupPath(previous, attemptId);
            var evidence = OwnedFileReplacementArtifacts.CheckpointEvidencePath(previous, attemptId);
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            Directory.CreateDirectory(Path.GetDirectoryName(previous)!);
            await File.WriteAllTextAsync(source, "incoming");
            await File.WriteAllTextAsync(previous, "incoming");
            await File.WriteAllTextAsync(backup, "original");
            await File.WriteAllTextAsync(evidence, "incoming");

            await using var db = CreateContext();
            var entityId = Guid.NewGuid();
            AddEntity(db, entityId, isWanted: false);
            db.EntityFiles.Add(SourceFile(entityId, previous));
            await db.SaveChangesAsync();

            var checkpoint = new TvImportCheckpoint(
                Guid.NewGuid(),
                seriesRoot,
                ImportMode.Move,
                AllowFormatChange: false,
                "Imported.",
                PreferSingleFileFinalSource: true,
                [new TvImportCheckpointUnit(
                    "Show.S01E01.mkv",
                    previous,
                    1,
                    1,
                    [],
                    PreviousFilePath: previous,
                    FinalPath: previous,
                    SourceAbsolutePath: source,
                    ReplacementBackupPath: backup,
                    ReplacementEvidencePath: evidence)],
                AttemptId: attemptId,
                ClaimJobId: Guid.NewGuid());
            var import = Context(entityId, payloadRoot, tvCheckpoint: checkpoint);

            await Cleaner(db).CleanupAsync(import, CancellationToken.None);

            Assert.Equal("original", await File.ReadAllTextAsync(previous));
            Assert.False(File.Exists(source));
            Assert.False(File.Exists(backup));
            Assert.False(File.Exists(evidence));
            Assert.Equal(previous, (await db.EntityFiles.SingleAsync()).Path);
            Assert.False((await db.Entities.SingleAsync()).IsWanted);
        } finally {
            DeleteBoundary(boundary);
        }
    }

    private static AcquisitionImportResetCleanup Cleaner(PrismediaDbContext db) =>
        new(db, new VideoScanConcurrencyGate(), NullLogger<AcquisitionImportResetCleanup>.Instance);

    private static AcquisitionImportContext Context(
        Guid entityId,
        string contentPath,
        ImportPlacementCheckpoint? placementCheckpoint = null,
        TvImportCheckpoint? tvCheckpoint = null) =>
        new(
            Guid.NewGuid(),
            "Title",
            null,
            null,
            null,
            null,
            null,
            null,
            ContentPath: contentPath,
            ClientItemId: "client-item",
            DownloadClientConfigId: Guid.NewGuid(),
            Kind: placementCheckpoint?.Kind ?? EntityKind.Video,
            EntityId: entityId,
            TvImportCheckpoint: tvCheckpoint,
            ImportPlacementCheckpoint: placementCheckpoint);

    private static void AddEntity(PrismediaDbContext db, Guid id, bool isWanted) =>
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = EntityKindRegistry.Book.Code,
            Title = "Title",
            IsWanted = isWanted,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

    private static EntityFileRow SourceFile(Guid entityId, string path) => new() {
        Id = Guid.NewGuid(),
        EntityId = entityId,
        Role = EntityFileRole.Source,
        Path = path,
        Source = FileSourceKind.Scan.ToCode(),
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static string TempBoundary() =>
        Path.Combine(Path.GetTempPath(), $"prismedia-import-reset-{Guid.NewGuid():N}");

    private static void DeleteBoundary(string boundary) {
        try {
            if (Directory.Exists(boundary)) {
                Directory.Delete(boundary, recursive: true);
            }
        } catch {
            // A failed assertion should remain the test failure; temp cleanup is best effort.
        }
    }
}
