using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class HeldTvImportRecoveryTests : IDisposable {
    private readonly string root = Directory.CreateTempSubdirectory("prismedia-held-tv-").FullName;
    private readonly Guid rootId = Guid.NewGuid();
    public void Dispose() => Directory.Delete(root, true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MonitoringARetainedForeignSeasonResumesOnlyItsConfidentMissingEpisodes(bool mislabeled) {
        await using var db = CreateContext();
        var (service, acquisition, episodes) = await SeedAsync(db);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        foreach (var episode in episodes) episode.IsWanted = false;
        var original = Path.Combine(root, "payload", "Show.S01E01E02.First.Story.Second.Story.mkv");
        File.Delete(original);
        var foreignName = mislabeled ? "Show.S01E49.Hidden.Garden.mkv" : "Show.S02E03.Hidden.Garden.mkv";
        await File.WriteAllTextAsync(Path.Combine(root, "payload", foreignName), "retained foreign episode");
        var season = new EntityRow {
            Id = Guid.NewGuid(), ParentEntityId = (await db.Entities.SingleAsync(row => row.Id == acquisition.EntityId)).ParentEntityId,
            KindCode = EntityKind.VideoSeason.ToCode(), Title = "Season 2", SortOrder = 2, IsWanted = true
        };
        var extra = new EntityRow {
            Id = Guid.NewGuid(), ParentEntityId = season.Id, KindCode = EntityKind.VideoEpisode.ToCode(),
            Title = "Hidden Garden", SortOrder = 3, IsWanted = true
        };
        db.Entities.AddRange(season, extra);
        acquisition.FinalSourcePath = Path.Combine(root, "previous.mkv");
        await File.WriteAllTextAsync(acquisition.FinalSourcePath, "previous imported video");
        acquisition.ImportResultJson = AcquisitionImportFileLedgerJson.Serialize(
            new AcquisitionImportFileLedger(AcquisitionImportPhase.Imported, []).RetainUnmappedTvVideos([new(foreignName, 23)]));
        await db.SaveChangesAsync();

        await service.RecoverAsync(default);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        await new EfMonitorStore(db).StartForEntityAsync(season.Id, EntityKind.VideoSeason, "Season 2", null, null, default);
        await service.RecoverAsync(default);

        Assert.Equal(AcquisitionStatus.Downloaded, acquisition.Status);
        Assert.NotNull(acquisition.ImportRecoveryFingerprint);
        var fingerprint = acquisition.ImportRecoveryFingerprint;
        acquisition.Status = AcquisitionStatus.ManualImportRequired;
        await db.SaveChangesAsync();
        await service.RecoverAsync(default);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        Assert.Equal(fingerprint, acquisition.ImportRecoveryFingerprint);
        Assert.Equal("previous imported video", await File.ReadAllTextAsync(acquisition.FinalSourcePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task APartialImportCanHealOnlyWhenItsLedgerRetainsReviewVideos(bool retained) {
        await using var db = CreateContext();
        var (service, acquisition, episodes) = await SeedAsync(db);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        acquisition.FinalSourcePath = Path.Combine(root, "already-imported.mkv");
        await File.WriteAllTextAsync(acquisition.FinalSourcePath, "previous library bytes");
        if (retained) {
            acquisition.ImportResultJson = AcquisitionImportFileLedgerJson.Serialize(
                new AcquisitionImportFileLedger(AcquisitionImportPhase.Imported, []).RetainUnmappedTvVideos([
                    new("Show.S01E01E02.First.Story.Second.Story.mkv", 10)
                ]));
        }
        await db.SaveChangesAsync();

        await service.RecoverAsync(default);

        Assert.Equal(retained ? AcquisitionStatus.Downloaded : AcquisitionStatus.ManualImportRequired, acquisition.Status);
        Assert.Equal("previous library bytes", await File.ReadAllTextAsync(acquisition.FinalSourcePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialImportRecoveryRechecksTheExactRetainedEvidence(bool postgres) {
        await using var database = postgres ? await PostgresTestDatabase.CreateAsync() : null;
        await using var db = database?.CreateContext() ?? CreateContext();
        var (_, acquisition, _) = await SeedAsync(db);
        acquisition.FinalSourcePath = Path.Combine(root, "previous.mkv");
        acquisition.ImportResultJson = AcquisitionImportFileLedgerJson.Serialize(
            new AcquisitionImportFileLedger(AcquisitionImportPhase.Imported, []).RetainUnmappedTvVideos([new("extra.mkv", 10)]));
        await db.SaveChangesAsync();
        var store = new EfHeldTvImportRecoveryStore(db);
        var held = Assert.Single(await store.ListAsync(default));
        Assert.True(await store.TryResumeAsync(held, "mapping-one", default));
        acquisition.Status = AcquisitionStatus.ManualImportRequired;
        acquisition.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var newer = Assert.Single(await store.ListAsync(default));
        acquisition.ImportResultJson = AcquisitionImportFileLedgerJson.Serialize(new(AcquisitionImportPhase.Imported, []));
        await db.SaveChangesAsync();

        Assert.False(await store.TryResumeAsync(newer, "mapping-two", default));
        Assert.Empty(await store.ListAsync(default));
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
    }

    [Fact]
    public async Task CorrectedEpisodeIdentitiesResumeTheRetainedPayloadOnlyOncePerMapping() {
        await using var db = CreateContext();
        var (service, acquisition, episodes) = await SeedAsync(db);
        await service.RecoverAsync(CancellationToken.None);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);

        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        await db.SaveChangesAsync();
        await service.RecoverAsync(CancellationToken.None);
        Assert.Equal(AcquisitionStatus.Downloaded, acquisition.Status);
        Assert.NotNull(acquisition.ImportRecoveryFingerprint);
        Assert.Single(await AcquisitionTestFactory.Store(db).ListDownloadedCompletionWorkAsync(CancellationToken.None));

        // Model an importer that still needs review for another reason. Restarting the recovery service
        // with the same mapping must leave that hold alone rather than repeatedly queuing import jobs.
        acquisition.Status = AcquisitionStatus.ManualImportRequired;
        acquisition.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await CreateService(db).RecoverAsync(CancellationToken.None);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);

        episodes[1].Title = "Second Story Revised";
        await db.SaveChangesAsync();
        await CreateService(db).RecoverAsync(CancellationToken.None);
        Assert.Equal(AcquisitionStatus.Downloaded, acquisition.Status);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PausedMonitoringAndManualReleaseChoicesStayHeld(bool paused, bool manualPick) {
        await using var db = CreateContext();
        var (service, acquisition, episodes) = await SeedAsync(db);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        if (paused) (await db.Monitors.SingleAsync()).Status = MonitorStatus.Paused;
        await db.SaveChangesAsync();
        if (manualPick) await AcquisitionTestFactory.Store(db).SetSelectedReleaseAsync(acquisition.Id,
            new SelectedRelease("Show S01 720p WEB", "Indexer", "held-test", ManualPick: true), CancellationToken.None);

        await service.RecoverAsync(CancellationToken.None);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        Assert.Null(acquisition.ImportRecoveryFingerprint);
    }

    [Fact]
    public async Task DangerousCompanionsPreventAutomaticRecovery() {
        await using var db = CreateContext();
        var (service, acquisition, episodes) = await SeedAsync(db);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        await db.SaveChangesAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "payload", "setup.exe"), "test");

        await service.RecoverAsync(CancellationToken.None);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        Assert.Null(acquisition.ImportRecoveryFingerprint);
    }

    [Fact]
    public async Task AnExplicitImportRetryRetainsManualAuthorityAfterTheJobHistoryIsGone() {
        await using var db = CreateContext();
        var (service, acquisition, episodes) = await SeedAsync(db);
        var store = AcquisitionTestFactory.Store(db);
        Assert.True(await store.TryClaimInitialImportAsync(acquisition.Id, Guid.NewGuid(), true, CancellationToken.None));
        await store.SetStatusAsync(acquisition.Id, AcquisitionStatus.ManualImportRequired, "Review remains necessary.", CancellationToken.None);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        await db.SaveChangesAsync();

        await service.RecoverAsync(CancellationToken.None);

        Assert.True(acquisition.ImportManualReview);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
    }

    [Theory]
    [InlineData(1, false, AcquisitionStatus.Downloaded)]
    [InlineData(2, false, AcquisitionStatus.ManualImportRequired)]
    [InlineData(2, true, AcquisitionStatus.Downloaded)]
    public async Task ACombinedFileIsReconsideredOnlyWhenItCanFillAMissingOwner(int ownedEpisodes, bool missingFile, AcquisitionStatus expected) {
        await using var db = CreateContext();
        var (service, acquisition, episodes) = await SeedAsync(db);
        var season = await db.Entities.SingleAsync(entity => entity.Id == acquisition.EntityId);
        var folder = Directory.CreateDirectory(Path.Combine(root, "Show")).FullName;
        var seasonFolder = Directory.CreateDirectory(Path.Combine(folder, "Season 1")).FullName;
        db.EntitySources.AddRange(
            new EntitySourceRow { EntityId = season.ParentEntityId!.Value, Code = EntitySourceCode.Folder.ToCode(), Value = folder },
            new EntitySourceRow { EntityId = season.Id, Code = EntitySourceCode.Folder.ToCode(), Value = seasonFolder });
        var ownedPath = Path.Combine(seasonFolder, "Show - S01E01.mkv");
        await File.WriteAllTextAsync(ownedPath, "test bytes");
        for (var index = 0; index < episodes.Length; index++) {
            episodes[index].SortOrder = index + 1;
            if (index < ownedEpisodes) db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(), EntityId = episodes[index].Id, Role = EntityFileRole.Source, Path = ownedPath
            });
        }
        await db.SaveChangesAsync();

        if (missingFile) File.Delete(ownedPath);
        await service.RecoverAsync(CancellationToken.None);

        Assert.Equal(expected, acquisition.Status);
        if (missingFile) Assert.False(File.Exists(ownedPath));
        else Assert.Equal("test bytes", await File.ReadAllTextAsync(ownedPath));
        Assert.Equal(ownedEpisodes, await db.EntityFiles.CountAsync());
    }

    [Fact]
    public async Task ACompletedOlderAttemptCannotAuthorizeImportOfANewerPartialTransfer() {
        await using var db = CreateContext();
        var (service, acquisition, episodes) = await SeedAsync(db);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        var completed = await db.DownloadTransfers.SingleAsync();
        var store = new EfHeldTvImportRecoveryStore(db);
        var held = Assert.Single(await store.ListAsync(CancellationToken.None));
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisition.Id, ClientItemId = "partial-retry",
            ContentPath = completed.ContentPath, Progress = 0.5,
            CreatedAt = completed.CreatedAt.AddSeconds(1), UpdatedAt = completed.UpdatedAt.AddSeconds(1)
        });
        await db.SaveChangesAsync();

        Assert.False(await store.TryResumeAsync(held, "changed-mapping", CancellationToken.None));
        Assert.Empty(await store.ListAsync(CancellationToken.None));
        await service.RecoverAsync(CancellationToken.None);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        Assert.Null(acquisition.ImportRecoveryFingerprint);
    }

    [Fact]
    public async Task ResumeCannotOverwriteANewerHoldOrCancellation() {
        await using var db = CreateContext();
        var (_, acquisition, _) = await SeedAsync(db);
        var store = new EfHeldTvImportRecoveryStore(db);
        var held = Assert.Single(await store.ListAsync(CancellationToken.None));
        acquisition.UpdatedAt = held.HeldAt.AddSeconds(1);
        await db.SaveChangesAsync();
        Assert.False(await store.TryResumeAsync(held, "mapping-a", CancellationToken.None));
        var newer = Assert.Single(await store.ListAsync(CancellationToken.None));
        acquisition.Status = AcquisitionStatus.Cancelled;
        await db.SaveChangesAsync();
        Assert.False(await store.TryResumeAsync(newer, "mapping-a", CancellationToken.None));
        Assert.Equal(AcquisitionStatus.Cancelled, acquisition.Status);
    }

    private async Task<(HeldTvImportRecoveryService Service, AcquisitionRow Acquisition, EntityRow[] Episodes)> SeedAsync(PrismediaDbContext db) {
        var now = DateTimeOffset.UtcNow;
        var series = new EntityRow { Id = Guid.NewGuid(), KindCode = EntityKind.VideoSeries.ToCode(), Title = "Show", CreatedAt = now, UpdatedAt = now };
        var season = new EntityRow { Id = Guid.NewGuid(), KindCode = EntityKind.VideoSeason.ToCode(), Title = "Season 1", ParentEntityId = series.Id, SortOrder = 1, CreatedAt = now, UpdatedAt = now };
        EntityRow[] episodes = [
            new() { Id = Guid.NewGuid(), KindCode = EntityKind.VideoEpisode.ToCode(), Title = "First Story", ParentEntityId = season.Id, IsWanted = true, CreatedAt = now, UpdatedAt = now },
            new() { Id = Guid.NewGuid(), KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Second Story", ParentEntityId = season.Id, IsWanted = true, CreatedAt = now, UpdatedAt = now }
        ];
        db.Entities.AddRange([series, season, .. episodes]);
        var acquisition = new AcquisitionRow {
            Id = Guid.NewGuid(), EntityId = season.Id, Kind = EntityKind.VideoSeason,
            Title = "Season 1", Series = "Show", SeasonNumber = 1,
            Status = AcquisitionStatus.ManualImportRequired, CreatedAt = now, UpdatedAt = now
        };
        db.Acquisitions.Add(acquisition);
        var payload = Directory.CreateDirectory(Path.Combine(root, "payload")).FullName;
        await File.WriteAllTextAsync(Path.Combine(payload, "Show.S01E01E02.First.Story.Second.Story.mkv"), "test bytes");
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisition.Id, ClientItemId = "retained-payload",
            ContentPath = payload, Progress = 1, CreatedAt = now, UpdatedAt = now
        });
        db.Monitors.Add(new MonitorRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisition.Id, EntityId = season.Id,
            Kind = EntityKind.VideoSeason, Title = "Season 1", Status = MonitorStatus.Active, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        await AcquisitionTestFactory.Store(db).SetSelectedReleaseAsync(acquisition.Id,
            new SelectedRelease("Show S01 720p WEB", "Indexer", "held-test"), CancellationToken.None);
        return (CreateService(db), acquisition, episodes);
    }

    private HeldTvImportRecoveryService CreateService(PrismediaDbContext db) => new(
        new EfHeldTvImportRecoveryStore(db), AcquisitionTestFactory.Store(db), new EfImportTargetIndex(db),
        new DownloadPayloadReader(), new EfBookAcquisitionProfileStore(db), new Roots(root, rootId), new EfMonitorStore(db),
        NullLogger<HeldTvImportRecoveryService>.Instance);

    private static PrismediaDbContext CreateContext() => new(new DbContextOptionsBuilder<PrismediaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Roots(string path, Guid id) : ILibraryScanRootPersistence {
        private readonly LibraryRootData library = new(id, path, "Library", true, true, true, false, false, false, false);
        public Task<LibraryRootData?> GetLibraryRootAsync(Guid rootId, CancellationToken cancellationToken) => Task.FromResult<LibraryRootData?>(library);
        public Task<IReadOnlyList<LibraryRootData>> GetEnabledRootsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LibraryRootData>>([library]);
        public Task<LibrarySettingsData> GetSettingsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateRootLastScannedAsync(Guid rootId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetExcludedPathsForRootAsync(Guid rootId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveEntitiesInExcludedPathsAsync(Guid rootId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveEntitiesOutsideLibraryRootsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveOrphanTagsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
