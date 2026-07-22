using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Handlers.Probe;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;
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
    public async Task MovieImportBindsWantedWrapperWithoutQueueingAFullLibraryScan() {
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
        var queue = new MergedImportTestSupport.RecordingJobQueue();

        await engine.ImportAsync(JobContext(db, import.Id, queue), import, CancellationToken.None);

        var entity = await db.Entities.AsNoTracking().SingleAsync(row => row.Id == wantedId);
        Assert.False(entity.IsWanted);
        Assert.True(await HasSourceInSubtreeAsync(db, wantedId));
        Assert.Contains(await db.Entities.AsNoTracking().ToArrayAsync(), row =>
            row.ParentEntityId == wantedId && row.KindCode == EntityKindRegistry.Video.Code);
        Assert.True(await db.Entities.AsNoTracking().AnyAsync(row => row.Id == unrelatedId));
        Assert.DoesNotContain(queue.Enqueued, request => request.Type == JobType.ScanLibrary);
        Assert.Equal(AcquisitionStatus.Imported, await StatusOfAsync(db, import.Id));
    }

    [Fact]
    public async Task AlbumImportReusesWantedTrackAndImmediatelyProcessesProbe() {
        await using var db = CreateContext();
        var rootPath = Directory.CreateDirectory(Path.Combine(_workRoot, "music")).FullName;
        var payloadPath = Directory.CreateDirectory(Path.Combine(_workRoot, "album-download")).FullName;
        await File.WriteAllTextAsync(Path.Combine(payloadPath, "01 Drag Me Under [262 kbps].opus"), "audio-bytes");
        var root = new RootPersistence(rootPath, scanAudio: true, autoGenerateMetadata: true);
        var unrelatedFolder = Directory.CreateDirectory(Path.Combine(rootPath, "Other Artist", "Other Album")).FullName;
        var unrelatedPath = Path.Combine(unrelatedFolder, "01 - Other.flac");
        await File.WriteAllTextAsync(unrelatedPath, "existing-audio");
        var unrelatedId = AddSourceEntity(db, EntityKind.AudioTrack, "Other Track", unrelatedPath);
        var artistId = AddWantedEntity(db, EntityKind.MusicArtist, "Artist");
        var albumId = AddWantedEntity(db, EntityKind.AudioLibrary, "Album", artistId);
        var wantedTrackId = AddWantedEntity(db, EntityKind.AudioTrack, "Drag Me Under", albumId);
        var now = DateTimeOffset.UtcNow;
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = wantedTrackId,
            Provider = ExternalIdProviders.MusicBrainz,
            Value = "recording-identity",
            CreatedAt = now,
            UpdatedAt = now
        });
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

        var queue = new MergedImportTestSupport.RecordingJobQueue();
        await engine.ImportAsync(JobContext(db, import.Id, queue), import, CancellationToken.None);

        Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == artistId)).IsWanted);
        Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == albumId)).IsWanted);
        Assert.True(await HasSourceInSubtreeAsync(db, albumId));
        var tracks = await db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId == albumId && row.KindCode == EntityKindRegistry.AudioTrack.Code)
            .ToArrayAsync();
        var retainedTrack = Assert.Single(tracks);
        Assert.Equal(wantedTrackId, retainedTrack.Id);
        Assert.Equal("Drag Me Under", retainedTrack.Title);
        Assert.False(retainedTrack.IsWanted);
        Assert.True(await HasSourceInSubtreeAsync(db, wantedTrackId));
        Assert.True(await db.EntityExternalIds.AsNoTracking().AnyAsync(row =>
            row.EntityId == wantedTrackId && row.Provider == ExternalIdProviders.MusicBrainz));

        var probeRequest = Assert.Single(queue.Enqueued, request =>
            request.Type == JobType.ProbeAudio && request.TargetEntityId == wantedTrackId.ToString());
        Assert.Equal(JobPriorities.AcquisitionProbe, probeRequest.Priority);
        var persistence = new LibraryScanPersistenceService(db);
        var probeHandler = new ProbeAudioJobHandler(
            NullLogger<ProbeAudioJobHandler>.Instance,
            new SuccessfulAudioProbe(),
            persistence,
            root,
            persistence);
        var probeJob = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ProbeAudio,
            JobRunStatus.Running,
            0,
            null,
            probeRequest.PayloadJson ?? "{}",
            probeRequest.TargetEntityKind,
            probeRequest.TargetEntityId,
            probeRequest.TargetLabel,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);
        await probeHandler.HandleAsync(new JobContext(probeJob, queue), CancellationToken.None);
        var technical = await db.EntityTechnical.AsNoTracking().SingleAsync(row => row.EntityId == wantedTrackId);
        Assert.Equal(201.5, technical.DurationSeconds);
        Assert.Equal(MediaCodecs.Opus, technical.Codec);
        Assert.True(await db.Entities.AsNoTracking().AnyAsync(row => row.Id == unrelatedId));
        Assert.Equal(AcquisitionStatus.Imported, await StatusOfAsync(db, acquisitionId));
    }

    [Fact]
    public async Task AudioTrackImportBindsWantedTrackBeforeImported() {
        await using var db = CreateContext();
        var rootPath = Directory.CreateDirectory(Path.Combine(_workRoot, "track-music")).FullName;
        var payloadPath = Directory.CreateDirectory(Path.Combine(_workRoot, "track-download")).FullName;
        await File.WriteAllTextAsync(Path.Combine(payloadPath, "01 - Had Enough.mp3"), "audio-bytes");
        var root = new RootPersistence(rootPath, scanAudio: true);
        var artistId = AddWantedEntity(db, EntityKind.MusicArtist, "Divide Music");
        var albumId = AddWantedEntity(db, EntityKind.AudioLibrary, "Had Enough", artistId);
        var trackId = AddWantedEntity(db, EntityKind.AudioTrack, "Had Enough", albumId);
        var acquisitionId = await AddAcquisitionAsync(db, EntityKind.AudioTrack, trackId, "Had Enough");
        var store = AcquisitionTestFactory.Store(db);
        var materializer = AlbumMaterializer(db, root, EntityKind.AudioTrack);
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
            NullLogger<MusicAcquisitionImportEngine>.Instance,
            kind: EntityKind.AudioTrack);
        var import = new AcquisitionImportContext(
            acquisitionId,
            "Had Enough",
            Author: "Divide Music",
            Series: "Had Enough",
            Year: 2025,
            PosterUrl: null,
            ExternalIdentity: null,
            ProfileId: null,
            ContentPath: payloadPath,
            ClientItemId: null,
            DownloadClientConfigId: null,
            Kind: EntityKind.AudioTrack,
            EntityId: trackId,
            TargetLibraryRootId: root.Root.Id);

        await engine.ImportAsync(JobContext(db, import.Id), import, CancellationToken.None);

        Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == artistId)).IsWanted);
        Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == albumId)).IsWanted);
        var track = await db.Entities.AsNoTracking().SingleAsync(row => row.Id == trackId);
        Assert.False(track.IsWanted);
        Assert.True(await HasSourceInSubtreeAsync(db, trackId));
        Assert.Equal(3, await db.Entities.AsNoTracking().CountAsync());
        Assert.Equal(AcquisitionStatus.Imported, await StatusOfAsync(db, acquisitionId));
    }

    [Fact]
    public async Task AudioRescanRepairsEnrichedFilenameDuplicateOntoProviderBackedWantedTrack() {
        await using var db = CreateContext();
        var rootPath = Directory.CreateDirectory(Path.Combine(_workRoot, "divide-repair")).FullName;
        var artistPath = Directory.CreateDirectory(Path.Combine(rootPath, "Divide Music")).FullName;
        var albumPath = Directory.CreateDirectory(Path.Combine(artistPath, "WAR")).FullName;
        var artistId = AddSourceEntity(db, EntityKind.MusicArtist, "Divide Music", artistPath);
        var albumId = AddSourceEntity(db, EntityKind.AudioLibrary, "WAR", albumPath, artistId);
        var wantedTrackId = AddWantedEntity(db, EntityKind.AudioTrack, "WAR", albumId);
        var sourcePath = Path.Combine(albumPath, "01 - WAR.mp3");
        await File.WriteAllTextAsync(sourcePath, "audio-bytes");
        var duplicateId = AddSourceEntity(
            db,
            EntityKind.AudioTrack,
            "01 - WAR",
            sourcePath,
            albumId);
        var now = DateTimeOffset.UtcNow;
        var sourceFileId = db.EntityFiles.Local.Single(file =>
            file.EntityId == duplicateId && file.Role == EntityFileRole.Source).Id;
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = wantedTrackId,
            Provider = ExternalIdProviders.MusicBrainz,
            Value = "war-recording",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.AudioTrackDetails.Add(new AudioTrackDetailRow {
            EntityId = duplicateId,
            EmbeddedArtist = "Divide Music",
            EmbeddedAlbum = "WAR"
        });
        db.EntityTechnical.Add(new EntityTechnicalRow {
            EntityId = duplicateId,
            DurationSeconds = 206.04,
            BitRate = 320_000,
            SampleRate = 48_000,
            Channels = 2,
            Codec = MediaCodecs.Mp3,
            UpdatedAt = now
        });
        db.EntityFileFingerprints.Add(new EntityFileFingerprintRow {
            Id = Guid.NewGuid(),
            EntityId = duplicateId,
            EntityFileId = sourceFileId,
            Algorithm = FingerprintAlgorithm.Md5,
            Value = "war-md5",
            CreatedAt = now
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = duplicateId,
            Role = EntityFileRole.Waveform,
            Path = AssetPathService.AudioWaveformUrl(duplicateId),
            MimeType = "application/json",
            CreatedAt = now,
            UpdatedAt = now
        });
        var oldProbeId = Guid.NewGuid();
        db.JobRuns.Add(new JobRunRow {
            Id = oldProbeId,
            Type = JobType.ProbeAudio,
            Status = JobRunStatus.Queued,
            PayloadJson = "{}",
            Priority = JobPriorities.Probe,
            Attempts = 0,
            MaxAttempts = 3,
            TargetEntityKind = EntityKindRegistry.AudioTrack.Code,
            TargetEntityId = duplicateId.ToString(),
            TargetLabel = "01 - WAR",
            AvailableAt = now,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        var root = new RootPersistence(rootPath, scanAudio: true);
        var persistence = new LibraryScanPersistenceService(db);
        var scan = new ScanAudioJobHandler(
            NullLogger<ScanAudioJobHandler>.Instance,
            Discovery(),
            root,
            persistence,
            persistence,
            acquisitionHints: new AcquisitionHintApplier(db));
        var queue = new MergedImportTestSupport.RecordingJobQueue();
        await scan.MaterializeImportedPathsAsync(
            JobContext(Guid.NewGuid(), queue),
            Guid.NewGuid(),
            root.Root,
            [sourcePath],
            CancellationToken.None);

        var retained = await db.Entities.AsNoTracking().SingleAsync(row => row.Id == wantedTrackId);
        Assert.Equal("WAR", retained.Title);
        Assert.False(retained.IsWanted);
        Assert.True(await db.EntityExternalIds.AsNoTracking().AnyAsync(row =>
            row.EntityId == wantedTrackId
            && row.Provider == ExternalIdProviders.MusicBrainz
            && row.Value == "war-recording"));
        Assert.False(await db.Entities.AsNoTracking().AnyAsync(row => row.Id == duplicateId));
        var source = await db.EntityFiles.AsNoTracking().SingleAsync(file =>
            file.EntityId == wantedTrackId && file.Role == EntityFileRole.Source);
        Assert.Equal(sourceFileId, source.Id);
        Assert.Equal(sourcePath, source.Path);
        Assert.False(await db.EntityFiles.AsNoTracking().AnyAsync(file =>
            file.EntityId == wantedTrackId && file.Role == EntityFileRole.Waveform));
        var technical = await db.EntityTechnical.AsNoTracking().SingleAsync(row => row.EntityId == wantedTrackId);
        Assert.Equal(206.04, technical.DurationSeconds);
        Assert.Equal(MediaCodecs.Mp3, technical.Codec);
        var fingerprint = await db.EntityFileFingerprints.AsNoTracking().SingleAsync(row =>
            row.EntityId == wantedTrackId && row.Algorithm == FingerprintAlgorithm.Md5);
        Assert.Equal(sourceFileId, fingerprint.EntityFileId);
        Assert.Equal("war-md5", fingerprint.Value);
        var detail = await db.AudioTrackDetails.AsNoTracking().SingleAsync(row => row.EntityId == wantedTrackId);
        Assert.Equal("Divide Music", detail.EmbeddedArtist);
        Assert.Equal("WAR", detail.EmbeddedAlbum);
        Assert.Single(await db.Entities.AsNoTracking().Where(row =>
            row.ParentEntityId == albumId && row.KindCode == EntityKindRegistry.AudioTrack.Code).ToArrayAsync());
        Assert.Contains(queue.Enqueued, request =>
            request.Type == JobType.GenerateAudioWaveform
            && request.TargetEntityId == wantedTrackId.ToString());
        var oldProbe = await db.JobRuns.AsNoTracking().SingleAsync(row => row.Id == oldProbeId);
        Assert.Equal(JobRunStatus.Cancelled, oldProbe.Status);
    }

    [Fact]
    public async Task AudioRescanDoesNotDiscardFilenameDuplicateWithPlaybackHistory() {
        await using var db = CreateContext();
        var albumId = AddWantedEntity(db, EntityKind.AudioLibrary, "WAR");
        var wantedTrackId = AddWantedEntity(db, EntityKind.AudioTrack, "WAR", albumId);
        var sourcePath = Path.Combine(_workRoot, "01 - WAR.mp3");
        await File.WriteAllTextAsync(sourcePath, "audio-bytes");
        var duplicateId = AddSourceEntity(db, EntityKind.AudioTrack, "01 - WAR", sourcePath, albumId);
        var now = DateTimeOffset.UtcNow;
        db.EntityPlaybackEvents.Add(new EntityPlaybackEventRow {
            Id = Guid.NewGuid(),
            EntityId = duplicateId,
            Kind = PlaybackEventKind.Completed,
            OccurredAt = now,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        var reconciliation = await new AcquisitionHintApplier(db).ReconcileWantedAudioTrackAsync(
            albumId,
            sourcePath,
            "01 - WAR",
            0,
            CancellationToken.None);

        Assert.Null(reconciliation);
        Assert.True((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == wantedTrackId)).IsWanted);
        Assert.True(await db.Entities.AsNoTracking().AnyAsync(row => row.Id == duplicateId));
        Assert.True(await db.EntityPlaybackEvents.AsNoTracking().AnyAsync(row => row.EntityId == duplicateId));
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
        RootPersistence root,
        EntityKind kind = EntityKind.AudioLibrary) {
        var persistence = new LibraryScanPersistenceService(db);
        var hints = new AcquisitionHintApplier(db);
        var scan = new ScanAudioJobHandler(
            NullLogger<ScanAudioJobHandler>.Instance,
            Discovery(),
            root,
            persistence,
            persistence,
            acquisitionHints: hints);
        return Materializer(db, new ImportedAlbumMaterializationPolicy(scan, kind));
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

    private sealed class RootPersistence : ILibraryScanRootPersistence {
        private readonly bool _autoGenerateMetadata;

        public RootPersistence(
            string path,
            bool scanVideos = false,
            bool scanAudio = false,
            bool scanBooks = false,
            bool autoGenerateMetadata = false) {
            _autoGenerateMetadata = autoGenerateMetadata;
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
                AutoGenerateMetadata: _autoGenerateMetadata,
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

    private sealed class SuccessfulAudioProbe : IMediaProbe {
        public Task<AudioProbeData?> ProbeAudioAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult<AudioProbeData?>(new AudioProbeData(
                DurationSeconds: 201.5,
                FileSize: 11,
                BitRate: 262_000,
                Codec: MediaCodecs.Opus,
                Container: null,
                SampleRate: 48_000,
                Channels: 2,
                Artist: "Divide Music",
                Album: "Drag Me Under",
                Title: "Drag Me Under",
                TrackNumber: "1"));

        public Task<VideoProbeData?> ProbeVideoAsync(string filePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ImageProbeData?> ProbeImageAsync(string filePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SubtitleStreamData>> ProbeSubtitleStreamsAsync(
            string filePath,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
