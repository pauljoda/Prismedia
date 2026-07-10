using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Merged-import coverage for movies and music: an acquisition linked to an entity already on disk
/// merges into the existing folder (replace-if-better for a movie's owned file, additive-only for an
/// album's tracks) instead of creating a template-derived duplicate; a payload with nothing usable
/// fails with the release blocklisted.
/// </summary>
public sealed class MovieMusicMergedImportTests : IDisposable {
    private readonly string _workRoot = Directory.CreateTempSubdirectory("prismedia-merged-import-").FullName;

    public void Dispose() {
        try {
            Directory.Delete(_workRoot, recursive: true);
        } catch {
            // best-effort temp cleanup
        }
    }

    [Fact]
    public async Task MovieUpgradeIsHeldBeforeMutationUntilReplacementHasCrashSafeRecovery() {
        await using var db = CreateContext();
        var world = await MovieWorldAsync(db, ownedFileName: "Film (2020) 720p WEB.mkv", payloadFile: "Film.2020.1080p.BluRay.mkv", releaseTitle: "Film 2020 1080p BluRay");

        await world.Engine.ImportAsync(world.Context, world.Import, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, world.Import.Id));
        Assert.Equal("owned-bytes", await File.ReadAllTextAsync(world.OwnedFilePath));
        Assert.False(File.Exists(world.OwnedFilePath + ".prismedia-bak"));
        // No template-derived duplicate folder appeared beside the existing one.
        Assert.Single(Directory.GetDirectories(world.LibraryRoot));
    }

    [Fact]
    public async Task MovieNonUpgradeFailsAndBlocklists() {
        await using var db = CreateContext();
        var world = await MovieWorldAsync(db, ownedFileName: "Film (2020) 1080p BluRay.mkv", payloadFile: "Film.2020.720p.WEB.mkv", releaseTitle: "Film 2020 720p WEB");

        await world.Engine.ImportAsync(world.Context, world.Import, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Failed, await StatusOf(db, world.Import.Id));
        Assert.Equal(BlocklistReason.NoImportableFiles, Assert.Single(await db.AcquisitionBlocklist.AsNoTracking().ToArrayAsync()).Reason);
        Assert.Equal("owned-bytes", await File.ReadAllTextAsync(world.OwnedFilePath)); // untouched
    }

    [Fact]
    public async Task AlbumMergeAddsOnlyMissingTracksIntoTheExistingFolder() {
        await using var db = CreateContext();
        var world = await MusicWorldAsync(db, ownedTracks: ["01 - One.flac"], payloadTracks: ["01 - One.flac", "02 - Two.flac"]);

        await world.Engine.ImportAsync(world.Context, world.Import, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, world.Import.Id));
        Assert.True(File.Exists(Path.Combine(world.AlbumFolder, "02 - Two.flac")));
        Assert.Equal("owned-bytes", await File.ReadAllTextAsync(Path.Combine(world.AlbumFolder, "01 - One.flac"))); // never replaced
        Assert.Single(Directory.GetDirectories(world.LibraryRoot)); // no duplicate artist folder
    }

    [Fact]
    public async Task AlbumWithNothingNewFailsAndBlocklists() {
        await using var db = CreateContext();
        var world = await MusicWorldAsync(db, ownedTracks: ["01 - One.flac"], payloadTracks: ["01 - One.flac"]);

        await world.Engine.ImportAsync(world.Context, world.Import, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Failed, await StatusOf(db, world.Import.Id));
        Assert.Equal(BlocklistReason.NoImportableFiles, Assert.Single(await db.AcquisitionBlocklist.AsNoTracking().ToArrayAsync()).Reason);
    }

    [Fact]
    public async Task AlbumWithOnlyNewCoverDoesNotMutateArtworkBeforeNothingUsableFailure() {
        await using var db = CreateContext();
        var world = await MusicWorldAsync(
            db,
            ownedTracks: ["01 - One.flac"],
            payloadTracks: ["01 - One.flac"],
            payloadCompanions: ["cover.jpg"]);

        await world.Engine.ImportAsync(world.Context, world.Import, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Failed, await StatusOf(db, world.Import.Id));
        Assert.False(File.Exists(Path.Combine(world.AlbumFolder, "cover.jpg")));
        Assert.Equal(BlocklistReason.NoImportableFiles, Assert.Single(await db.AcquisitionBlocklist.AsNoTracking().ToArrayAsync()).Reason);
    }

    private sealed record MovieWorld(MovieAcquisitionImportEngine Engine, JobContext Context, AcquisitionImportContext Import, string LibraryRoot, string OwnedFilePath);

    private sealed record MusicWorld(MusicAcquisitionImportEngine Engine, JobContext Context, AcquisitionImportContext Import, string LibraryRoot, string AlbumFolder);

    private async Task<MovieWorld> MovieWorldAsync(PrismediaDbContext db, string ownedFileName, string payloadFile, string releaseTitle) {
        var libraryRoot = Directory.CreateDirectory(Path.Combine(_workRoot, "movies")).FullName;
        var movieFolder = Directory.CreateDirectory(Path.Combine(libraryRoot, "Film (2020) [existing]")).FullName;
        var ownedFilePath = Path.Combine(movieFolder, ownedFileName);
        await File.WriteAllTextAsync(ownedFilePath, "owned-bytes");

        var payloadRoot = Directory.CreateDirectory(Path.Combine(_workRoot, "download-movie", "release")).FullName;
        await File.WriteAllTextAsync(Path.Combine(payloadRoot, payloadFile), "payload-bytes");

        var movieId = AddEntity(db, EntityKindRegistry.Movie.Code, null, null, movieFolder);
        AddEntity(db, EntityKindRegistry.Video.Code, movieId, 1, ownedFilePath);
        var acquisitionId = await AddAcquisitionAsync(db, EntityKind.Movie, movieId, "Film", releaseTitle);

        var store = AcquisitionTestFactory.Store(db);
        var engine = new MovieAcquisitionImportEngine(
            store,
            new EfBookAcquisitionProfileStore(db),
            new MergedImportTestSupport.SingleRootPersistence(libraryRoot),
            new DownloadPayloadReader(),
            new ImportFileMover(),
            new ImportedTorrentRemover(store, new MergedImportTestSupport.ThrowingClientConfigStore(), new MergedImportTestSupport.ThrowingClientFactory(), NullLogger<ImportedTorrentRemover>.Instance),
            new EfImportTargetIndex(db),
            new EfAcquisitionBlocklistStore(db),
            new EfAcquisitionHistoryStore(db),
            new ExistingReadyMaterializer(),
            NullLogger<MovieAcquisitionImportEngine>.Instance);

        var import = new AcquisitionImportContext(
            acquisitionId, "Film", Author: null, Series: null, Year: 2020, PosterUrl: null,
            ExternalIdentity: null, ProfileId: null, ContentPath: payloadRoot,
            ClientItemId: null, DownloadClientConfigId: null, Kind: EntityKind.Movie, EntityId: movieId);

        return new MovieWorld(engine, JobContextFor(db, acquisitionId, JobType.AcquisitionImport), import, libraryRoot, ownedFilePath);
    }

    private async Task<MusicWorld> MusicWorldAsync(
        PrismediaDbContext db,
        string[] ownedTracks,
        string[] payloadTracks,
        string[]? payloadCompanions = null) {
        var libraryRoot = Directory.CreateDirectory(Path.Combine(_workRoot, "music")).FullName;
        var artistFolder = Directory.CreateDirectory(Path.Combine(libraryRoot, "The Artist [existing]")).FullName;
        var albumFolder = Directory.CreateDirectory(Path.Combine(artistFolder, "Album [existing]")).FullName;

        var artistId = AddEntity(db, EntityKindRegistry.MusicArtist.Code, null, null, artistFolder);
        var albumId = AddEntity(db, EntityKindRegistry.AudioLibrary.Code, artistId, null, albumFolder);
        var position = 1;
        foreach (var track in ownedTracks) {
            var trackPath = Path.Combine(albumFolder, track);
            await File.WriteAllTextAsync(trackPath, "owned-bytes");
            AddEntity(db, EntityKindRegistry.AudioTrack.Code, albumId, position++, trackPath);
        }

        var payloadRoot = Directory.CreateDirectory(Path.Combine(_workRoot, "download-music", "release")).FullName;
        foreach (var track in payloadTracks) {
            await File.WriteAllTextAsync(Path.Combine(payloadRoot, track), "payload-bytes");
        }
        foreach (var companion in payloadCompanions ?? []) {
            await File.WriteAllTextAsync(Path.Combine(payloadRoot, companion), "companion-bytes");
        }

        var acquisitionId = await AddAcquisitionAsync(db, EntityKind.AudioLibrary, albumId, "Album", "Artist - Album FLAC");

        var store = AcquisitionTestFactory.Store(db);
        var engine = new MusicAcquisitionImportEngine(
            store,
            new EfBookAcquisitionProfileStore(db),
            new MergedImportTestSupport.SingleRootPersistence(libraryRoot),
            new DownloadPayloadReader(),
            new ImportFileMover(),
            new ImportedTorrentRemover(store, new MergedImportTestSupport.ThrowingClientConfigStore(), new MergedImportTestSupport.ThrowingClientFactory(), NullLogger<ImportedTorrentRemover>.Instance),
            new EfImportTargetIndex(db),
            new EfAcquisitionBlocklistStore(db),
            new EfAcquisitionHistoryStore(db),
            new ExistingReadyMaterializer(),
            NullLogger<MusicAcquisitionImportEngine>.Instance);

        var import = new AcquisitionImportContext(
            acquisitionId, "Album", Author: "The Artist", Series: null, Year: null, PosterUrl: null,
            ExternalIdentity: null, ProfileId: null, ContentPath: payloadRoot,
            ClientItemId: null, DownloadClientConfigId: null, Kind: EntityKind.AudioLibrary, EntityId: albumId);

        return new MusicWorld(engine, JobContextFor(db, acquisitionId, JobType.AcquisitionImport), import, libraryRoot, albumFolder);
    }

    private static async Task<Guid> AddAcquisitionAsync(PrismediaDbContext db, EntityKind kind, Guid entityId, string title, string releaseTitle) {
        var acquisitionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, Status = AcquisitionStatus.Importing, Title = title, Kind = kind, EntityId = entityId,
            ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        await AcquisitionTestFactory.Store(db).SetSelectedReleaseAsync(
            acquisitionId, new SelectedRelease(releaseTitle, "Indexer", "hash-1"), CancellationToken.None);
        return acquisitionId;
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

    private static JobContext JobContextFor(PrismediaDbContext db, Guid acquisitionId, JobType type) {
        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();
        db.Acquisitions.Local.Single(row => row.Id == acquisitionId).ImportClaimJobId = jobId;
        db.SaveChanges();
        var job = new JobRunSnapshot(jobId, type, JobRunStatus.Running, 0, null, "{}", null, null, null, now, now, null);
        return new JobContext(job, new MergedImportTestSupport.RecordingJobQueue());
    }

    private static async Task<AcquisitionStatus> StatusOf(PrismediaDbContext db, Guid acquisitionId) =>
        await db.Acquisitions.AsNoTracking().Where(row => row.Id == acquisitionId).Select(row => row.Status).SingleAsync();

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class ExistingReadyMaterializer : IImportedEntityMaterializer {
        public Task MaterializeAsync(
            EntityKind kind,
            JobContext context,
            ImportedEntityMaterializationRequest request,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
