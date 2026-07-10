using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Media.Adapters;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Import readiness coverage for every first-party non-TV acquisition engine. Each engine must bind
/// its existing Wanted Entity to real Source ownership before it writes Imported; TV has its own
/// checkpoint/materializer suite because one file may satisfy several episode Entities.
/// </summary>
public sealed class ImportedEntityMaterializationTests : IDisposable {
    private readonly string _workRoot = Directory.CreateTempSubdirectory("prismedia-import-ready-").FullName;

    public void Dispose() {
        try {
            Directory.Delete(_workRoot, recursive: true);
        } catch {
            // best-effort test cleanup
        }
    }

    [Fact]
    public async Task BookImportBindsWantedEntityBeforeImported() {
        await using var db = CreateContext();
        var rootPath = Directory.CreateDirectory(Path.Combine(_workRoot, "books")).FullName;
        var payloadPath = Directory.CreateDirectory(Path.Combine(_workRoot, "book-download")).FullName;
        var sourcePath = Path.Combine(payloadPath, "Novel.epub");
        await File.WriteAllTextAsync(sourcePath, "epub-bytes");
        var root = new RootPersistence(rootPath, scanBooks: true);
        var unrelatedPath = Path.Combine(rootPath, "Unrelated.epub");
        await File.WriteAllTextAsync(unrelatedPath, "existing-book");
        var unrelatedId = AddSourceEntity(db, EntityKind.Book, "Unrelated", unrelatedPath);
        var wantedId = await SeedWantedAcquisitionAsync(db, EntityKind.Book, "Novel");
        var store = AcquisitionTestFactory.Store(db);
        var materializer = BookMaterializer(db, root);
        var engine = new BookAcquisitionImportEngine(
            store,
            new EfBookAcquisitionProfileStore(db),
            root,
            new SingleBookPlanner(sourcePath),
            new ImportFileMover(),
            materializer,
            Torrents(store),
            NullLogger<BookAcquisitionImportEngine>.Instance);
        var import = ImportContext(db, EntityKind.Book, wantedId, "Novel", payloadPath, author: "Author");

        await engine.ImportAsync(JobContext(db, import.Id), import, CancellationToken.None);

        var entity = await db.Entities.AsNoTracking().SingleAsync(row => row.Id == wantedId);
        Assert.False(entity.IsWanted);
        Assert.True(await HasSourceInSubtreeAsync(db, wantedId));
        Assert.True(await db.Entities.AsNoTracking().AnyAsync(row => row.Id == unrelatedId));
        Assert.Equal(AcquisitionStatus.Imported, await StatusOfAsync(db, import.Id));
    }

    [Fact]
    public async Task MovieImportBindsWantedWrapperBeforeImportedWhenHousekeepingQueueFails() {
        await using var db = CreateContext();
        var rootPath = Directory.CreateDirectory(Path.Combine(_workRoot, "movies")).FullName;
        var payloadPath = Directory.CreateDirectory(Path.Combine(_workRoot, "movie-download")).FullName;
        await File.WriteAllTextAsync(Path.Combine(payloadPath, "Film.2020.mkv"), "video-bytes");
        var root = new RootPersistence(rootPath, scanVideos: true);
        var unrelatedFolder = Directory.CreateDirectory(Path.Combine(rootPath, "Unrelated")).FullName;
        var unrelatedPath = Path.Combine(unrelatedFolder, "Unrelated.mkv");
        await File.WriteAllTextAsync(unrelatedPath, "existing-video");
        var unrelatedId = AddSourceEntity(db, EntityKind.Video, "Unrelated", unrelatedPath);
        var wantedId = await SeedWantedAcquisitionAsync(db, EntityKind.Movie, "Film");
        var store = AcquisitionTestFactory.Store(db);
        var materializer = MovieMaterializer(db, root);
        var engine = new MovieAcquisitionImportEngine(
            store,
            new EfBookAcquisitionProfileStore(db),
            root,
            new DownloadPayloadReader(),
            new ImportFileMover(),
            Torrents(store),
            new EfImportTargetIndex(db),
            new EfAcquisitionBlocklistStore(db),
            new EfAcquisitionHistoryStore(db),
            materializer,
            NullLogger<MovieAcquisitionImportEngine>.Instance);
        var import = ImportContext(db, EntityKind.Movie, wantedId, "Film", payloadPath, year: 2020);
        var failingQueue = new ThrowingEnqueueJobQueue();

        await engine.ImportAsync(JobContext(db, import.Id, failingQueue), import, CancellationToken.None);

        var entity = await db.Entities.AsNoTracking().SingleAsync(row => row.Id == wantedId);
        Assert.False(entity.IsWanted);
        Assert.True(await HasSourceInSubtreeAsync(db, wantedId));
        Assert.Contains(await db.Entities.AsNoTracking().ToArrayAsync(), row =>
            row.ParentEntityId == wantedId && row.KindCode == EntityKindRegistry.Video.Code);
        Assert.True(await db.Entities.AsNoTracking().AnyAsync(row => row.Id == unrelatedId));
        Assert.True(failingQueue.EnqueueAttempts > 0);
        Assert.Equal(AcquisitionStatus.Imported, await StatusOfAsync(db, import.Id));
    }

    [Fact]
    public async Task AlbumImportBindsWantedWrapperAndTracksBeforeImported() {
        await using var db = CreateContext();
        var rootPath = Directory.CreateDirectory(Path.Combine(_workRoot, "music")).FullName;
        var payloadPath = Directory.CreateDirectory(Path.Combine(_workRoot, "album-download")).FullName;
        await File.WriteAllTextAsync(Path.Combine(payloadPath, "01 - Track.flac"), "audio-bytes");
        var root = new RootPersistence(rootPath, scanAudio: true);
        var unrelatedFolder = Directory.CreateDirectory(Path.Combine(rootPath, "Other Artist", "Other Album")).FullName;
        var unrelatedPath = Path.Combine(unrelatedFolder, "01 - Other.flac");
        await File.WriteAllTextAsync(unrelatedPath, "existing-audio");
        var unrelatedId = AddSourceEntity(db, EntityKind.AudioTrack, "Other Track", unrelatedPath);
        var artistId = AddWantedEntity(db, EntityKind.MusicArtist, "Artist");
        var albumId = AddWantedEntity(db, EntityKind.AudioLibrary, "Album", artistId);
        var acquisitionId = await AddAcquisitionAsync(db, EntityKind.AudioLibrary, albumId, "Album");
        var store = AcquisitionTestFactory.Store(db);
        var materializer = AlbumMaterializer(db, root);
        var engine = new MusicAcquisitionImportEngine(
            store,
            new EfBookAcquisitionProfileStore(db),
            root,
            new DownloadPayloadReader(),
            new ImportFileMover(),
            Torrents(store),
            new EfImportTargetIndex(db),
            new EfAcquisitionBlocklistStore(db),
            new EfAcquisitionHistoryStore(db),
            materializer,
            NullLogger<MusicAcquisitionImportEngine>.Instance);
        var import = new AcquisitionImportContext(
            acquisitionId,
            "Album",
            Author: "Artist",
            Series: null,
            Year: null,
            PosterUrl: null,
            ExternalIdentity: null,
            ProfileId: null,
            ContentPath: payloadPath,
            ClientItemId: null,
            DownloadClientConfigId: null,
            Kind: EntityKind.AudioLibrary,
            EntityId: albumId,
            TargetLibraryRootId: root.Root.Id);

        await engine.ImportAsync(JobContext(db, import.Id), import, CancellationToken.None);

        Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == artistId)).IsWanted);
        Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == albumId)).IsWanted);
        Assert.True(await HasSourceInSubtreeAsync(db, albumId));
        Assert.Contains(await db.Entities.AsNoTracking().ToArrayAsync(), row =>
            row.ParentEntityId == albumId && row.KindCode == EntityKindRegistry.AudioTrack.Code);
        Assert.True(await db.Entities.AsNoTracking().AnyAsync(row => row.Id == unrelatedId));
        Assert.Equal(AcquisitionStatus.Imported, await StatusOfAsync(db, acquisitionId));
    }

    [Fact]
    public async Task AlbumCheckpointIncludesAudioAndCoverWhileReadinessUsesAudioOnly() {
        await using var db = CreateContext();
        var rootPath = Directory.CreateDirectory(Path.Combine(_workRoot, "music-checkpoint")).FullName;
        var payloadPath = Directory.CreateDirectory(Path.Combine(_workRoot, "album-checkpoint-download")).FullName;
        await File.WriteAllTextAsync(Path.Combine(payloadPath, "01 - Track.flac"), "audio-bytes");
        await File.WriteAllTextAsync(Path.Combine(payloadPath, "cover.jpg"), "cover-bytes");
        var root = new RootPersistence(rootPath, scanAudio: true);
        var albumId = await SeedWantedAcquisitionAsync(db, EntityKind.AudioLibrary, "Album");
        var store = AcquisitionTestFactory.Store(db);
        var engine = new MusicAcquisitionImportEngine(
            store,
            new EfBookAcquisitionProfileStore(db),
            root,
            new DownloadPayloadReader(),
            new ImportFileMover(),
            Torrents(store),
            new EfImportTargetIndex(db),
            new EfAcquisitionBlocklistStore(db),
            new EfAcquisitionHistoryStore(db),
            new FailingMaterializer(),
            NullLogger<MusicAcquisitionImportEngine>.Instance);
        var import = ImportContext(db, EntityKind.AudioLibrary, albumId, "Album", payloadPath, author: "Artist");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.ImportAsync(JobContext(db, import.Id), import, CancellationToken.None));

        var checkpoint = Assert.IsType<ImportPlacementCheckpoint>(
            (await store.GetImportContextAsync(import.Id, CancellationToken.None))?.ImportPlacementCheckpoint);
        Assert.Equal(2, checkpoint.Units.Count);
        Assert.Single(checkpoint.Units, unit => unit.IsMedia);
        Assert.Single(checkpoint.Units, unit => !unit.IsMedia);
        Assert.All(checkpoint.Units, unit => {
            Assert.Equal(unit.TargetAbsolutePath, unit.FinalPath);
            Assert.True(File.Exists(unit.TargetAbsolutePath));
        });
        Assert.NotEqual(AcquisitionStatus.Imported, await StatusOfAsync(db, import.Id));
    }

    [Fact]
    public async Task MaterializationFailureKeepsExactMoveCheckpointAndRetryFinishesWithoutDuplicate() {
        await using var db = CreateContext();
        var rootPath = Directory.CreateDirectory(Path.Combine(_workRoot, "failed-books")).FullName;
        var payloadPath = Directory.CreateDirectory(Path.Combine(_workRoot, "failed-book-download")).FullName;
        var sourcePath = Path.Combine(payloadPath, "Novel.epub");
        await File.WriteAllTextAsync(sourcePath, "epub-bytes");
        var root = new RootPersistence(rootPath, scanBooks: true);
        var wantedId = await SeedWantedAcquisitionAsync(db, EntityKind.Book, "Novel");
        var store = AcquisitionTestFactory.Store(db);
        var engine = new BookAcquisitionImportEngine(
            store,
            new EfBookAcquisitionProfileStore(db),
            root,
            new SingleBookPlanner(sourcePath),
            new ImportFileMover(),
            new FailingMaterializer(),
            Torrents(store),
            NullLogger<BookAcquisitionImportEngine>.Instance);
        var import = ImportContext(db, EntityKind.Book, wantedId, "Novel", payloadPath, author: "Author");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.ImportAsync(JobContext(db, import.Id), import, CancellationToken.None));

        Assert.NotEqual(AcquisitionStatus.Imported, await StatusOfAsync(db, import.Id));
        Assert.False(File.Exists(sourcePath));
        var persisted = Assert.IsType<ImportPlacementCheckpoint>(
            (await store.GetImportContextAsync(import.Id, CancellationToken.None))?.ImportPlacementCheckpoint);
        var placed = Assert.Single(persisted.Units);
        Assert.Equal(placed.TargetAbsolutePath, placed.FinalPath);
        Assert.True(File.Exists(placed.TargetAbsolutePath));

        // The job handler records the thrown attempt as Failed; its next queue job atomically claims
        // this exact plan. Reproduce that boundary, then prove the engine never replans the consumed source.
        await store.SetStatusAsync(import.Id, AcquisitionStatus.Failed, "Synthetic materialization failure.", CancellationToken.None);
        var retryJobId = Guid.NewGuid();
        Assert.True(await store.TryClaimImportPlacementCheckpointAsync(
            import.Id,
            persisted,
            retryJobId,
            CancellationToken.None));
        var claimed = persisted with { ClaimJobId = retryJobId };
        var retryEngine = new BookAcquisitionImportEngine(
            store,
            new EfBookAcquisitionProfileStore(db),
            root,
            new SingleBookPlanner(sourcePath),
            new ImportFileMover(),
            BookMaterializer(db, root),
            Torrents(store),
            NullLogger<BookAcquisitionImportEngine>.Instance);

        await retryEngine.ImportAsync(
            JobContext(retryJobId),
            import with { ImportPlacementCheckpoint = claimed },
            CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Imported, await StatusOfAsync(db, import.Id));
        Assert.Single(Directory.GetFiles(rootPath, "*.epub", SearchOption.AllDirectories));
        Assert.Null((await store.GetImportContextAsync(import.Id, CancellationToken.None))?.ImportPlacementCheckpoint);
    }

    [Fact]
    public async Task ReadinessRejectsExpectedPathOwnedOutsideLinkedSubtreeEvenWhenSubtreeHasOtherSource() {
        await using var db = CreateContext();
        var linkedId = AddWantedEntity(db, EntityKind.Movie, "Linked");
        db.Entities.Local.Single(entity => entity.Id == linkedId).IsWanted = false;
        var unrelatedInsidePath = Path.Combine(_workRoot, "linked-other.mkv");
        var expectedOutsidePath = Path.Combine(_workRoot, "outside-owner.mkv");
        await File.WriteAllTextAsync(unrelatedInsidePath, "inside");
        await File.WriteAllTextAsync(expectedOutsidePath, "outside");
        AddSourceEntity(db, EntityKind.Video, "Unrelated child", unrelatedInsidePath, linkedId);
        AddSourceEntity(db, EntityKind.Video, "Outside owner", expectedOutsidePath);
        await db.SaveChangesAsync();

        var readiness = new EfImportedEntityReadinessPersistence(
            db,
            new EfEntityHierarchyReader(db));

        Assert.False(await readiness.IsReadyAsync(
            linkedId,
            [expectedOutsidePath],
            CancellationToken.None));
    }

    [Fact]
    public async Task ReadinessDoesNotMatchCaseDistinctLinuxSourcePath() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        await using var db = CreateContext();
        var linkedId = AddWantedEntity(db, EntityKind.Movie, "Case-sensitive movie");
        db.Entities.Local.Single(entity => entity.Id == linkedId).IsWanted = false;
        var actualPath = Path.Combine(_workRoot, "case-sensitive.mkv");
        var differentCasePath = Path.Combine(_workRoot, "CASE-SENSITIVE.mkv");
        await File.WriteAllTextAsync(actualPath, "video");
        AddSourceEntity(db, EntityKind.Video, "Case-sensitive child", actualPath, linkedId);
        await db.SaveChangesAsync();

        var readiness = new EfImportedEntityReadinessPersistence(
            db,
            new EfEntityHierarchyReader(db));

        Assert.False(await readiness.IsReadyAsync(
            linkedId,
            [differentCasePath],
            CancellationToken.None));
    }

    [Fact]
    public async Task ReadinessMatchesCaseVariantWindowsSourcePathAfterDatabaseLookup() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        await using var db = CreateContext();
        var linkedId = AddWantedEntity(db, EntityKind.Movie, "Case-insensitive movie");
        db.Entities.Local.Single(entity => entity.Id == linkedId).IsWanted = false;
        var actualPath = Path.Combine(_workRoot, "case-insensitive.mkv");
        var differentCasePath = Path.Combine(_workRoot, "CASE-INSENSITIVE.mkv");
        await File.WriteAllTextAsync(actualPath, "video");
        AddSourceEntity(db, EntityKind.Video, "Case-insensitive child", actualPath, linkedId);
        await db.SaveChangesAsync();

        var readiness = new EfImportedEntityReadinessPersistence(
            db,
            new EfEntityHierarchyReader(db));

        Assert.True(await readiness.IsReadyAsync(
            linkedId,
            [differentCasePath],
            CancellationToken.None));
    }

    private static IImportedEntityMaterializer BookMaterializer(
        PrismediaDbContext db,
        RootPersistence root) {
        var persistence = new LibraryScanPersistenceService(db);
        var hints = new AcquisitionHintApplier(db);
        var scan = new ScanBookJobHandler(
            NullLogger<ScanBookJobHandler>.Instance,
            Discovery(),
            root,
            persistence,
            persistence,
            acquisitionHints: hints);
        return Materializer(db, new ImportedBookMaterializationPolicy(scan));
    }

    private static IImportedEntityMaterializer MovieMaterializer(
        PrismediaDbContext db,
        RootPersistence root) {
        var persistence = new LibraryScanPersistenceService(db);
        var hints = new AcquisitionHintApplier(db);
        var scan = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            Discovery(),
            root,
            persistence,
            persistence,
            acquisitionHints: hints,
            scanGate: new VideoScanConcurrencyGate());
        return Materializer(db, new ImportedMovieMaterializationPolicy(scan));
    }

    private static IImportedEntityMaterializer AlbumMaterializer(
        PrismediaDbContext db,
        RootPersistence root) {
        var persistence = new LibraryScanPersistenceService(db);
        var hints = new AcquisitionHintApplier(db);
        var scan = new ScanAudioJobHandler(
            NullLogger<ScanAudioJobHandler>.Instance,
            Discovery(),
            root,
            persistence,
            persistence,
            acquisitionHints: hints);
        return Materializer(db, new ImportedAlbumMaterializationPolicy(scan));
    }

    private static IImportedEntityMaterializer Materializer(
        PrismediaDbContext db,
        IImportedEntityMaterializationPolicy policy) =>
        new ImportedEntityMaterializer(
            [policy],
            new EfImportedEntityReadinessPersistence(db, new EfEntityHierarchyReader(db)));

    private static FileDiscoveryAdapter Discovery() => new(new FileDiscoveryService());

    private static ImportedTorrentRemover Torrents(IAcquisitionStore store) => new(
        store,
        new MergedImportTestSupport.ThrowingClientConfigStore(),
        new MergedImportTestSupport.ThrowingClientFactory(),
        NullLogger<ImportedTorrentRemover>.Instance);

    private static async Task<Guid> SeedWantedAcquisitionAsync(
        PrismediaDbContext db,
        EntityKind kind,
        string title) {
        var entityId = AddWantedEntity(db, kind, title);
        await AddAcquisitionAsync(db, kind, entityId, title);
        return entityId;
    }

    private static Guid AddWantedEntity(
        PrismediaDbContext db,
        EntityKind kind,
        string title,
        Guid? parentId = null) {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind.ToCode(),
            Title = title,
            ParentEntityId = parentId,
            IsWanted = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        return id;
    }

    private static Guid AddSourceEntity(
        PrismediaDbContext db,
        EntityKind kind,
        string title,
        string sourcePath,
        Guid? parentId = null) {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind.ToCode(),
            Title = title,
            ParentEntityId = parentId,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = sourcePath,
            CreatedAt = now,
            UpdatedAt = now
        });
        return id;
    }

    private static async Task<Guid> AddAcquisitionAsync(
        PrismediaDbContext db,
        EntityKind kind,
        Guid entityId,
        string title) {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Acquisitions.Add(new AcquisitionRow {
            Id = id,
            EntityId = entityId,
            Kind = kind,
            Status = AcquisitionStatus.Importing,
            Title = title,
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static AcquisitionImportContext ImportContext(
        PrismediaDbContext db,
        EntityKind kind,
        Guid entityId,
        string title,
        string payloadPath,
        string? author = null,
        int? year = null) {
        var acquisition = db.Acquisitions.Local.Single(row => row.EntityId == entityId);
        return new AcquisitionImportContext(
            acquisition.Id,
            title,
            author,
            Series: null,
            year,
            PosterUrl: null,
            ExternalIdentity: null,
            ProfileId: null,
            ContentPath: payloadPath,
            ClientItemId: null,
            DownloadClientConfigId: null,
            Kind: kind,
            EntityId: entityId,
            TargetLibraryRootId: null);
    }

    private static async Task<bool> HasSourceInSubtreeAsync(PrismediaDbContext db, Guid entityId) {
        var ids = await new EfEntityHierarchyReader(db).ListSubtreeIdsAsync(entityId, CancellationToken.None);
        return await db.EntityFiles.AsNoTracking().AnyAsync(file =>
            ids.Contains(file.EntityId) && file.Role == EntityFileRole.Source);
    }

    private static async Task<AcquisitionStatus> StatusOfAsync(PrismediaDbContext db, Guid acquisitionId) =>
        await db.Acquisitions.AsNoTracking()
            .Where(row => row.Id == acquisitionId)
            .Select(row => row.Status)
            .SingleAsync();

    private static JobContext JobContext(
        PrismediaDbContext db,
        Guid acquisitionId,
        IJobQueueService? queue = null) {
        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();
        var acquisition = db.Acquisitions.Local.Single(row => row.Id == acquisitionId);
        acquisition.ImportClaimJobId = jobId;
        db.SaveChanges();
        return new JobContext(
            new JobRunSnapshot(
                jobId, JobType.AcquisitionImport, JobRunStatus.Running, 0, null, "{}",
                null, null, null, now, now, null),
            queue ?? new MergedImportTestSupport.RecordingJobQueue());
    }

    private static JobContext JobContext(Guid jobId, IJobQueueService? queue = null) {
        var now = DateTimeOffset.UtcNow;
        return new JobContext(
            new JobRunSnapshot(
                jobId, JobType.AcquisitionImport, JobRunStatus.Running, 0, null, "{}",
                null, null, null, now, now, null),
            queue ?? new MergedImportTestSupport.RecordingJobQueue());
    }

    private static PrismediaDbContext CreateContext() => new(
        new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class SingleBookPlanner(string sourcePath) : IAcquisitionImportPlanner {
        public Task<ResolvedImportPlan> PlanAsync(
            string contentPath,
            string libraryRootPath,
            BookImportProfile profile,
            ImportTemplateContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ResolvedImportPlan(
                Blocked: false,
                BlockReason: null,
                [new ResolvedImportItem(
                    sourcePath,
                    Path.Combine(libraryRootPath, context.Author ?? "Unknown Author", "Novel.epub"))]));
    }

    private sealed class FailingMaterializer : IImportedEntityMaterializer {
        public Task MaterializeAsync(
            EntityKind kind,
            JobContext context,
            ImportedEntityMaterializationRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic materialization failure.");
    }

    private sealed class ThrowingEnqueueJobQueue : IJobQueueService {
        public int EnqueueAttempts { get; private set; }

        public Task<JobRunSnapshot> EnqueueAsync(
            EnqueueJobRequest request,
            CancellationToken cancellationToken) {
            EnqueueAttempts++;
            throw new InvalidOperationException("Synthetic queue outage.");
        }

        public Task<int> EnqueueBatchAsync(
            IReadOnlyList<EnqueueJobRequest> requests,
            CancellationToken cancellationToken) {
            EnqueueAttempts++;
            throw new InvalidOperationException("Synthetic queue outage.");
        }

        public Task<bool> HasPendingAsync(
            JobType type,
            string? targetEntityId,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task UpdateProgressAsync(
            Guid id,
            int progress,
            string? message,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) => throw new NotSupportedException();
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

    private sealed class RootPersistence : ILibraryScanRootPersistence {
        public RootPersistence(
            string path,
            bool scanVideos = false,
            bool scanAudio = false,
            bool scanBooks = false) {
            Root = new LibraryRootData(
                Guid.NewGuid(),
                path,
                "Imported media",
                Enabled: true,
                Recursive: true,
                ScanVideos: scanVideos,
                ScanImages: false,
                ScanAudio: scanAudio,
                ScanBooks: scanBooks,
                IsNsfw: false);
        }

        public LibraryRootData Root { get; }

        public Task<LibraryRootData?> GetLibraryRootAsync(Guid rootId, CancellationToken cancellationToken) =>
            Task.FromResult<LibraryRootData?>(rootId == Root.Id ? Root : null);

        public Task<IReadOnlyList<LibraryRootData>> GetEnabledRootsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryRootData>>([Root]);

        public Task<LibrarySettingsData> GetSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new LibrarySettingsData(
                AutoGenerateMetadata: false,
                AutoGenerateOshash: false,
                AutoGenerateMd5: false,
                AutoGeneratePreview: false,
                GenerateTrickplay: false,
                TrickplayIntervalSeconds: 10,
                PreviewClipDurationSeconds: 10,
                ThumbnailQuality: 80,
                TrickplayQuality: 80));

        public Task<IReadOnlySet<string>> GetExcludedPathsForRootAsync(
            Guid rootId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        public Task UpdateRootLastScannedAsync(Guid rootId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<int> RemoveEntitiesInExcludedPathsAsync(Guid rootId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<int> RemoveEntitiesOutsideLibraryRootsAsync(CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<int> RemoveOrphanTagsAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
