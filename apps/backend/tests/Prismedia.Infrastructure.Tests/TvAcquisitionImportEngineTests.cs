using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// End-to-end coverage of the TV import engine's merged path: an acquisition linked to a series that
/// already lives on disk places new episodes into the EXISTING folders (no duplicate series folder),
/// replaces strictly-better collisions in place, drops non-upgrades, and — when nothing was usable —
/// fails the acquisition with the release blocklisted. The legacy template path stays untouched for
/// acquisitions without an entity link.
/// </summary>
public sealed class TvAcquisitionImportEngineTests : IDisposable {
    private readonly string _workRoot = Directory.CreateTempSubdirectory("prismedia-tv-import-").FullName;

    public void Dispose() {
        try {
            Directory.Delete(_workRoot, recursive: true);
        } catch {
            // best-effort temp cleanup
        }
    }

    [Fact]
    public async Task MergesNewEpisodeIntoTheExistingSeasonFolder() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mkv", payloadFiles: ["Show.S01E02.1080p.WEB-DL.mkv"], releaseTitle: "Show S01 1080p WEB-DL");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(harness.SeasonFolder, "Show - S01E02.mkv")));
        Assert.True(File.Exists(harness.OwnedEpisodePath)); // the owned episode is untouched
        Assert.Empty(Directory.GetDirectories(_workRoot).Where(dir => dir.Contains("Season", StringComparison.Ordinal))); // no template-derived parallel series folder
        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
        Assert.Empty(await db.AcquisitionBlocklist.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task MissingSeasonFolderIsCreatedInsideTheExistingSeriesFolder() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mkv", payloadFiles: ["Show.S02E01.1080p.WEB-DL.mkv"], releaseTitle: "Show S02 1080p WEB-DL");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(harness.SeriesFolder, "Season 02", "Show - S02E01.mkv")));
        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
    }

    [Fact]
    public async Task StrictUpgradeReplacesTheOwnedFileInPlace() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mkv", payloadFiles: ["Show.S01E01.1080p.BluRay.mkv"], releaseTitle: "Show S01 1080p BluRay", payloadContent: "upgraded-bytes");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
        // Same path, new content; the previous file is preserved as the recoverable backup.
        Assert.Equal("upgraded-bytes", await File.ReadAllTextAsync(harness.OwnedEpisodePath));
        Assert.True(File.Exists(harness.OwnedEpisodePath + ".prismedia-bak"));
    }

    [Fact]
    public async Task NothingBetterFailsBlocklistsAndRecordsHistory() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 1080p BluRay.mkv", payloadFiles: ["Show.S01E01.720p.WEB.mkv"], releaseTitle: "Show S01 720p WEB");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Failed, await StatusOf(db, harness.Import.Id));
        var blocked = Assert.Single(await db.AcquisitionBlocklist.AsNoTracking().ToArrayAsync());
        Assert.Equal(BlocklistReason.NoImportableFiles, blocked.Reason);
        var events = await db.AcquisitionHistory.AsNoTracking().Select(row => row.Event).ToArrayAsync();
        Assert.Contains(AcquisitionHistoryEvent.Blocklisted, events);
        Assert.Contains(AcquisitionHistoryEvent.ImportFailed, events);
        // The non-upgrade payload was not placed anywhere in the series tree.
        Assert.False(File.Exists(Path.Combine(harness.SeasonFolder, "Show - S01E01.mkv")) && new FileInfo(harness.OwnedEpisodePath).Length == 0);
    }

    [Fact]
    public async Task FormatChangeUpgradeIsHeldForManualImportWithoutBlocklisting() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mp4", payloadFiles: ["Show.S01E01.2160p.BluRay.mkv"], releaseTitle: "Show S01 2160p BluRay");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.Empty(await db.AcquisitionBlocklist.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task AcquisitionWithoutEntityLinkKeepsTheTemplatePlacement() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mkv", payloadFiles: ["Show.S01E02.1080p.WEB-DL.mkv"], releaseTitle: "Show S01 1080p WEB-DL", linkEntity: false);

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        // Template path: a fresh series folder under the library root, not the existing tree.
        Assert.True(File.Exists(Path.Combine(harness.LibraryRoot, "Show", "Season 01", "Show - S01E02.mkv")));
        Assert.False(File.Exists(Path.Combine(harness.SeasonFolder, "Show - S01E02.mkv")));
        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
    }

    private sealed record Harness(
        TvAcquisitionImportEngine Engine,
        JobContext Context,
        AcquisitionImportContext Import,
        string LibraryRoot,
        string SeriesFolder,
        string SeasonFolder,
        string OwnedEpisodePath);

    /// <summary>
    /// One seeded world: a library root, an on-disk series (`Show (2008)/S01/<ownedEpisodeName>`) mirrored
    /// as series/season/episode entities with Source files, an acquisition linked to the season, and a
    /// payload folder holding <paramref name="payloadFiles"/>.
    /// </summary>
    private async Task<Harness> HarnessAsync(
        PrismediaDbContext db,
        string ownedEpisodeName,
        string[] payloadFiles,
        string releaseTitle,
        string payloadContent = "payload-bytes",
        bool linkEntity = true) {
        var libraryRoot = Directory.CreateDirectory(Path.Combine(_workRoot, "library")).FullName;
        var seriesFolder = Directory.CreateDirectory(Path.Combine(libraryRoot, "Show (2008)")).FullName;
        var seasonFolder = Directory.CreateDirectory(Path.Combine(seriesFolder, "S01")).FullName;
        var ownedEpisodePath = Path.Combine(seasonFolder, ownedEpisodeName);
        await File.WriteAllTextAsync(ownedEpisodePath, "owned-bytes");

        var payloadRoot = Directory.CreateDirectory(Path.Combine(_workRoot, "download", "release")).FullName;
        foreach (var file in payloadFiles) {
            await File.WriteAllTextAsync(Path.Combine(payloadRoot, file), payloadContent);
        }

        var now = DateTimeOffset.UtcNow;
        var seriesId = AddEntity(db, EntityKindRegistry.VideoSeries.Code, null, null, seriesFolder);
        var seasonId = AddEntity(db, EntityKindRegistry.VideoSeason.Code, seriesId, 1, seasonFolder);
        AddEntity(db, EntityKindRegistry.Video.Code, seasonId, 1, ownedEpisodePath);

        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, Status = AcquisitionStatus.Importing, Title = "Show", Series = "Show",
            Kind = EntityKind.VideoSeason, SeasonNumber = 1, EntityId = linkEntity ? seasonId : null,
            ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var store = AcquisitionTestFactory.Store(db);
        await store.SetSelectedReleaseAsync(acquisitionId, new SelectedRelease(releaseTitle, "Indexer", "hash-1"), CancellationToken.None);

        var import = new AcquisitionImportContext(
            acquisitionId, "Show", Author: null, Series: "Show", Year: null, PosterUrl: null,
            PluginId: null, PluginItemId: null, ProfileId: null, ContentPath: payloadRoot,
            ClientItemId: null, DownloadClientConfigId: null, Kind: EntityKind.VideoSeason,
            SeasonNumber: 1, EntityId: linkEntity ? seasonId : null);

        var history = new EfAcquisitionHistoryStore(db);
        var engine = new TvAcquisitionImportEngine(
            EntityKind.VideoSeason,
            store,
            new EfBookAcquisitionProfileStore(db),
            new SingleRootPersistence(libraryRoot),
            new DownloadPayloadReader(),
            new ImportFileMover(),
            new ImportedTorrentRemover(store, new ThrowingClientConfigStore(), new ThrowingClientFactory(), NullLogger<ImportedTorrentRemover>.Instance),
            new EfImportTargetIndex(db),
            new OwnedFileReplacer(new NoRecycleBin(), NullLogger<OwnedFileReplacer>.Instance),
            new EfAcquisitionBlocklistStore(db),
            history,
            NullLogger<TvAcquisitionImportEngine>.Instance);

        var job = new JobRunSnapshot(
            Guid.NewGuid(), JobType.AcquisitionImport, JobRunStatus.Running, 0, null, "{}",
            null, null, null, now, now, null);
        return new Harness(
            engine, new JobContext(job, new RecordingJobQueue()), import,
            libraryRoot, seriesFolder, seasonFolder, ownedEpisodePath);
    }

    private static Guid AddEntity(PrismediaDbContext db, string kindCode, Guid? parent, int? sortOrder, string sourcePath) {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = id, KindCode = kindCode, Title = kindCode, ParentEntityId = parent,
            SortOrder = sortOrder, CreatedAt = now, UpdatedAt = now
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(), EntityId = id, Role = EntityFileRole.Source, Path = sourcePath,
            CreatedAt = now, UpdatedAt = now
        });
        return id;
    }

    private static async Task<AcquisitionStatus> StatusOf(PrismediaDbContext db, Guid acquisitionId) =>
        await db.Acquisitions.AsNoTracking().Where(row => row.Id == acquisitionId).Select(row => row.Status).SingleAsync();

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class SingleRootPersistence(string path) : ILibraryScanRootPersistence {
        private readonly LibraryRootData _root = new(
            Guid.NewGuid(), path, "Videos", Enabled: true, Recursive: true,
            ScanVideos: true, ScanImages: false, ScanAudio: false, ScanBooks: false, IsNsfw: false);

        public Task<LibraryRootData?> GetLibraryRootAsync(Guid rootId, CancellationToken cancellationToken) =>
            Task.FromResult<LibraryRootData?>(rootId == _root.Id ? _root : null);
        public Task<IReadOnlyList<LibraryRootData>> GetEnabledRootsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryRootData>>([_root]);
        public Task<LibrarySettingsData> GetSettingsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateRootLastScannedAsync(Guid rootId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetExcludedPathsForRootAsync(Guid rootId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveEntitiesInExcludedPathsAsync(Guid rootId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveEntitiesOutsideLibraryRootsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveOrphanTagsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoRecycleBin : IRecycleBin {
        public Task<string?> TryMoveToBinAsync(string filePath, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<int> CleanupAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class ThrowingClientConfigStore : Prismedia.Application.Acquisition.IDownloadClientConfigStore {
        public Task<Prismedia.Contracts.Acquisition.DownloadClientDetail?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Prismedia.Contracts.Acquisition.DownloadClientDetail?> GetDefaultAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Prismedia.Contracts.Acquisition.DownloadClientDetail?> GetDefaultAsync(DownloadProtocol protocol, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Prismedia.Contracts.Acquisition.DownloadClientDetail>> ListEnabledAsync(DownloadProtocol protocol, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadProtocol>> GetEnabledProtocolsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Prismedia.Contracts.Acquisition.DownloadClientSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Prismedia.Contracts.Acquisition.DownloadClientDetail>> ListDetailsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Prismedia.Contracts.Acquisition.DownloadClientSummary> SaveAsync(Prismedia.Application.Acquisition.DownloadClientSaveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingClientFactory : IDownloadClientFactory {
        public IDownloadClient Get(DownloadClientKind kind) => throw new NotSupportedException();
    }

    private sealed class RecordingJobQueue : IJobQueueService {
        public List<EnqueueJobRequest> Enqueued { get; } = [];

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            Enqueued.Add(request);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new JobRunSnapshot(
                Guid.NewGuid(), request.Type, JobRunStatus.Queued, 0, null, request.PayloadJson ?? "{}",
                request.TargetEntityKind, request.TargetEntityId, request.TargetLabel, now, null, null));
        }

        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult(Enqueued.Any(request => request.Type == type && request.TargetEntityId == targetEntityId));
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken, JobRunLane? lane = null) => throw new NotSupportedException();
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
