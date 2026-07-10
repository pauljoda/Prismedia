using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Files;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Exercises media-entity deletion over the real EF stores (in-memory provider): the descendant tree is
/// removed with the entity, only top-level source paths inside a watched root are deleted from disk,
/// monitors are torn down, acquisitions are handed to the teardown port, and the entity's provider
/// identities are suppressed so a monitored container never re-requests the deleted work.
/// </summary>
public sealed class MediaEntityDeletionServiceTests {
    [Theory]
    [InlineData(EntityKind.Audio, true)]
    [InlineData(EntityKind.BookChapter, false)]
    [InlineData(EntityKind.BookPage, false)]
    [InlineData(EntityKind.Collection, false)]
    public void DeleteFilesGateUsesTheEntityKindRegistry(EntityKind kind, bool expected) {
        Assert.Equal(expected, MediaEntityDeletionService.IsDeletableKind(kind.ToCode()));
    }

    [Fact]
    public async Task DeletesTheTreeItsFilesAndTearsDownAcquisitionState() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/tv", "TV", true, true, false, false, false, false);
        // The paused monitor marks an unmonitored tree: full removal, and the stale row goes with it.
        var (seriesId, seasonId, episodeId) = await SeedSeriesTreeAsync(db, root.Path, MonitorStatus.Paused);

        var storage = new RecordingStorage();
        var suppressions = new RecordingSuppressions();
        var acquisitions = new RecordingAcquisitions([seasonId]);
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, suppressions, acquisitions, new RecordingRecovery(), new NullJobQueue(),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(seriesId, deleteFiles: true, CancellationToken.None);

        Assert.True(result.Deleted);
        // Only the series folder is deleted — it subsumes the season folder and episode file.
        Assert.Equal(["/media/tv/Clifford the Big Red Dog"], storage.DeletedPaths);
        Assert.Equal(1, result.FilesDeleted);
        Assert.Empty(await db.Entities.Where(row => row.Id == seriesId || row.Id == seasonId || row.Id == episodeId).ToArrayAsync());
        Assert.Empty(await db.Monitors.ToArrayAsync());
        Assert.Contains(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), acquisitions.Deleted);
        Assert.Contains("tmdb:8379", suppressions.Suppressed);
    }

    [Theory]
    [InlineData(MonitorStatus.Paused)]
    [InlineData(MonitorStatus.Active)]
    public async Task LibraryOnlyDeletionIsRejectedWithoutDiskOrLifecycleMutation(MonitorStatus monitorStatus) {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/tv", "TV", true, true, false, false, false, false);
        var (seriesId, _, _) = await SeedSeriesTreeAsync(db, root.Path, monitorStatus);
        var monitor = await db.Monitors.SingleAsync(row => row.EntityId == seriesId);
        monitor.AcquisitionId = RecordingAcquisitions.AcquisitionId;
        db.Acquisitions.Add(new AcquisitionRow {
            Id = RecordingAcquisitions.AcquisitionId,
            EntityId = seriesId,
            Kind = EntityKind.VideoSeries,
            Status = AcquisitionStatus.Imported,
            Title = "Clifford the Big Red Dog",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var entityIds = await db.Entities.AsNoTracking().Select(row => row.Id).OrderBy(value => value).ToArrayAsync();
        var fileIds = await db.EntityFiles.AsNoTracking().Select(row => row.Id).OrderBy(value => value).ToArrayAsync();
        var monitorState = await db.Monitors.AsNoTracking()
            .Select(row => new { row.Id, row.EntityId, row.AcquisitionId, row.Status })
            .SingleAsync();
        var acquisitionState = await db.Acquisitions.AsNoTracking()
            .Select(row => new { row.Id, row.Status, row.TeardownIntent })
            .SingleAsync();

        var storage = new RecordingStorage();
        var acquisitions = new RecordingAcquisitions([seriesId]);
        var recovery = new RecordingRecovery();
        var suppressions = new RecordingSuppressions();
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, suppressions, acquisitions,
            recovery, new NullJobQueue(),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(seriesId, deleteFiles: false, CancellationToken.None);

        Assert.False(result.Deleted);
        Assert.Equal(MediaEntityDeleteFailureKind.NotDeletable, result.FailureKind);
        Assert.Contains("Library-only", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(storage.AttemptedPaths);
        Assert.Equal(entityIds, await db.Entities.AsNoTracking().Select(row => row.Id).OrderBy(value => value).ToArrayAsync());
        Assert.Equal(fileIds, await db.EntityFiles.AsNoTracking().Select(row => row.Id).OrderBy(value => value).ToArrayAsync());
        Assert.Equal(
            monitorState,
            await db.Monitors.AsNoTracking()
                .Select(row => new { row.Id, row.EntityId, row.AcquisitionId, row.Status })
                .SingleAsync());
        Assert.Equal(
            acquisitionState,
            await db.Acquisitions.AsNoTracking()
                .Select(row => new { row.Id, row.Status, row.TeardownIntent })
                .SingleAsync());
        Assert.Empty(acquisitions.ClaimedTeardowns);
        Assert.Empty(acquisitions.ConfirmedTransferRemovals);
        Assert.Empty(acquisitions.Deleted);
        Assert.Empty(acquisitions.Reacquired);
        Assert.Empty(recovery.Requested);
        Assert.Empty(suppressions.SuppressedIdentities);
    }

    [Fact]
    public async Task ActivelyMonitoredContentRevertsToWantedInsteadOfBeingRemoved() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/tv", "TV", true, true, false, false, false, false);
        var (seriesId, seasonId, episodeId) = await SeedSeriesTreeAsync(db, root.Path, MonitorStatus.Active);
        db.Monitors.Add(new MonitorRow {
            Id = Guid.NewGuid(),
            Kind = EntityKind.VideoSeason,
            AcquisitionId = RecordingAcquisitions.AcquisitionId,
            EntityId = seasonId,
            Status = MonitorStatus.Active,
            Title = "Season 1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.Monitors.Add(new MonitorRow {
            Id = Guid.NewGuid(),
            Kind = EntityKind.VideoSeason,
            AcquisitionId = RecordingAcquisitions.OlderAcquisitionId,
            Status = MonitorStatus.Paused,
            Title = "Old Season 1 request",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var storage = new RecordingStorage();
        var suppressions = new RecordingSuppressions();
        var acquisitions = new RecordingAcquisitions([seasonId], db);
        acquisitions.AcquisitionIdsByEntity[seasonId] = [
            RecordingAcquisitions.AcquisitionId,
            RecordingAcquisitions.OlderAcquisitionId
        ];
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, suppressions, acquisitions,
            new RecordingRecovery(), new NullJobQueue(),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        // Deleting the directly monitored SEASON: disk state goes, library state reverts to wanted so
        // the monitoring loop re-acquires it. The ancestor series monitor is not deletion authority.
        var result = await service.DeleteAsync(seasonId, deleteFiles: true, CancellationToken.None);

        Assert.True(result.Deleted);
        Assert.True(result.Reverted);
        Assert.Equal(["/media/tv/Clifford the Big Red Dog/Season 01"], storage.DeletedPaths);
        // The directly monitored season survives as Wanted. Its unmonitored episode branch is removed;
        // the season acquisition will materialize the correct structural children again after import.
        var season = await db.Entities.SingleAsync(row => row.Id == seasonId);
        Assert.True(season.IsWanted);
        Assert.Null(await db.Entities.SingleOrDefaultAsync(row => row.Id == episodeId));
        Assert.Empty(await db.EntityFiles.Where(file => file.EntityId == seasonId).ToArrayAsync());
        // The series and both its container monitor and the season's acquisition monitor stay. The
        // acquisition is replaced only after the entity is durably Wanted + fileless, then immediately
        // re-searched; it is never hard-deleted into an orphaned UI state.
        Assert.NotNull(await db.Entities.SingleOrDefaultAsync(row => row.Id == seriesId));
        var monitors = await db.Monitors.ToArrayAsync();
        Assert.Contains(monitors, monitor => monitor.EntityId == seriesId && monitor.Status == MonitorStatus.Active);
        Assert.Contains(monitors, monitor => monitor.AcquisitionId == RecordingAcquisitions.AcquisitionId);
        Assert.DoesNotContain(monitors, monitor => monitor.AcquisitionId == RecordingAcquisitions.OlderAcquisitionId);
        Assert.Equal([RecordingAcquisitions.AcquisitionId], acquisitions.Reacquired);
        Assert.Equal([RecordingAcquisitions.OlderAcquisitionId], acquisitions.Deleted);
        Assert.True(acquisitions.ReacquireSawWantedFileless);
        Assert.Empty(suppressions.Suppressed);
    }

    [Fact]
    public async Task ActiveDescendantAcquisitionBlocksMonitoredDeleteBeforeAnyMutation() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/tv", "TV", true, true, false, false, false, false);
        var (_, seasonId, episodeId) = await SeedSeriesTreeAsync(db, root.Path, MonitorStatus.Active);
        db.Monitors.Add(new MonitorRow {
            Id = Guid.NewGuid(),
            Kind = EntityKind.VideoSeason,
            EntityId = seasonId,
            Status = MonitorStatus.Active,
            Title = "Season 1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.Monitors.Add(new MonitorRow {
            Id = Guid.NewGuid(),
            Kind = EntityKind.Video,
            EntityId = episodeId,
            Status = MonitorStatus.Active,
            Title = "Little Clifford",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var sourceFileIds = await db.EntityFiles
            .Where(file => file.EntityId == seasonId || file.EntityId == episodeId)
            .Select(file => file.Id)
            .OrderBy(value => value)
            .ToArrayAsync();
        var monitorIds = await db.Monitors.Select(monitor => monitor.Id).ToArrayAsync();

        var storage = new RecordingStorage();
        var acquisitions = new RecordingAcquisitions([seasonId, episodeId]);
        acquisitions.AcquisitionIdsByEntity[seasonId] = [RecordingAcquisitions.AcquisitionId];
        acquisitions.AcquisitionIdsByEntity[episodeId] = [RecordingAcquisitions.ActiveAcquisitionId];
        acquisitions.IneligibleReacquireIds.Add(RecordingAcquisitions.ActiveAcquisitionId);
        var jobs = new NullJobQueue(hasPending: false);
        var cacheRoot = Path.Combine(Path.GetTempPath(), "prismedia-delete-tests", Guid.NewGuid().ToString());
        var cacheDirectory = Path.Combine(cacheRoot, "videos", seasonId.ToString());
        var cacheFile = Path.Combine(cacheDirectory, "preview.m3u8");
        Directory.CreateDirectory(cacheDirectory);
        await File.WriteAllTextAsync(cacheFile, "still owned by the imported entity");
        try {
            var service = new MediaEntityDeletionService(
                db, new FakeRoots(root), storage, new RecordingSuppressions(), acquisitions,
                new RecordingRecovery(), jobs,
                new Prismedia.Infrastructure.Media.Processing.AssetPathService(cacheRoot),
                new EfEntityHierarchyReader(db),
                NullLogger<MediaEntityDeletionService>.Instance);

            var result = await service.DeleteAsync(seasonId, deleteFiles: true, CancellationToken.None);

            Assert.False(result.Deleted);
            Assert.Equal(MediaEntityDeleteFailureKind.Conflict, result.FailureKind);
            Assert.Contains("downloading", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(storage.DeletedPaths);
            Assert.True(File.Exists(cacheFile));
            Assert.Equal(
                sourceFileIds,
                await db.EntityFiles
                    .Where(file => file.EntityId == seasonId || file.EntityId == episodeId)
                    .Select(file => file.Id)
                    .OrderBy(value => value)
                    .ToArrayAsync());
            Assert.All(
                await db.Entities.Where(entity => entity.Id == seasonId || entity.Id == episodeId).ToArrayAsync(),
                entity => Assert.False(entity.IsWanted));
            Assert.Equal(monitorIds, await db.Monitors.Select(monitor => monitor.Id).ToArrayAsync());
            Assert.Empty(acquisitions.Deleted);
            Assert.Empty(acquisitions.Reacquired);
            Assert.Empty(jobs.Enqueued);
        } finally {
            Directory.Delete(cacheRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RemoteTransferFailureBlocksUnmonitoredRemovalBeforeDiskOrDatabaseMutation() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/tv", "TV", true, true, false, false, false, false);
        var (seriesId, seasonId, _) = await SeedSeriesTreeAsync(db, root.Path, MonitorStatus.Paused);
        var entityIds = await db.Entities.Select(row => row.Id).OrderBy(value => value).ToArrayAsync();
        var sourceFileIds = await db.EntityFiles.Select(row => row.Id).OrderBy(value => value).ToArrayAsync();
        var monitorIds = await db.Monitors.Select(row => row.Id).OrderBy(value => value).ToArrayAsync();
        var storage = new RecordingStorage();
        var suppressions = new RecordingSuppressions();
        var acquisitions = new RecordingAcquisitions([seasonId]);
        acquisitions.TransferRemovalFailureIds.Add(RecordingAcquisitions.AcquisitionId);
        var recovery = new RecordingRecovery();
        var jobs = new NullJobQueue(hasPending: false);
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, suppressions, acquisitions, recovery, jobs,
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(seriesId, deleteFiles: true, CancellationToken.None);

        Assert.False(result.Deleted);
        Assert.Equal(MediaEntityDeleteFailureKind.Conflict, result.FailureKind);
        Assert.Contains("download client", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([RecordingAcquisitions.AcquisitionId], acquisitions.ConfirmedTransferRemovals);
        Assert.Empty(storage.AttemptedPaths);
        Assert.Equal(entityIds, await db.Entities.Select(row => row.Id).OrderBy(value => value).ToArrayAsync());
        Assert.Equal(sourceFileIds, await db.EntityFiles.Select(row => row.Id).OrderBy(value => value).ToArrayAsync());
        Assert.Equal(monitorIds, await db.Monitors.Select(row => row.Id).OrderBy(value => value).ToArrayAsync());
        Assert.Empty(acquisitions.Deleted);
        Assert.Empty(acquisitions.Reacquired);
        Assert.Empty(recovery.Requested);
        Assert.Empty(suppressions.SuppressedIdentities);
        Assert.Empty(jobs.Enqueued);
    }

    [Fact]
    public async Task RemoteTransferFailureBlocksMonitoredReacquisitionBeforeDiskOrDatabaseMutation() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/movies", "Movies", true, true, false, false, false, false);
        var entityId = Guid.NewGuid();
        var monitorId = Guid.NewGuid();
        var sourceFileId = Guid.NewGuid();
        db.Entities.Add(NewEntity(entityId, EntityKindRegistry.Movie.Code, "Arrival"));
        db.EntityFiles.Add(new EntityFileRow {
            Id = sourceFileId,
            EntityId = entityId,
            Role = EntityFileRole.Source,
            Path = "/media/movies/Arrival/Arrival.mkv",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            Kind = EntityKind.Movie,
            EntityId = entityId,
            Status = MonitorStatus.Active,
            Title = "Arrival",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        var suppressions = new RecordingSuppressions();
        var acquisitions = new RecordingAcquisitions([entityId]);
        acquisitions.TransferRemovalFailureIds.Add(RecordingAcquisitions.AcquisitionId);
        var recovery = new RecordingRecovery();
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, suppressions, acquisitions, recovery, new NullJobQueue(hasPending: false),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(entityId, deleteFiles: true, CancellationToken.None);

        Assert.False(result.Deleted);
        Assert.Equal(MediaEntityDeleteFailureKind.Conflict, result.FailureKind);
        Assert.Equal([RecordingAcquisitions.AcquisitionId], acquisitions.ConfirmedTransferRemovals);
        Assert.Empty(storage.AttemptedPaths);
        Assert.False((await db.Entities.SingleAsync(row => row.Id == entityId)).IsWanted);
        Assert.NotNull(await db.EntityFiles.SingleOrDefaultAsync(row => row.Id == sourceFileId));
        Assert.NotNull(await db.Monitors.SingleOrDefaultAsync(row => row.Id == monitorId));
        Assert.Empty(acquisitions.Deleted);
        Assert.Empty(acquisitions.Reacquired);
        Assert.Empty(recovery.Requested);
        Assert.Empty(suppressions.SuppressedIdentities);
    }

    [Fact]
    public async Task StorageFailureLeavesRemoteRemovedLifecycleClaimedAndRetryReacquires() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/movies", "Movies", true, true, false, false, false, false);
        var entityId = Guid.NewGuid();
        var monitorId = Guid.NewGuid();
        var path = "/media/movies/Arrival/Arrival.mkv";
        db.Entities.Add(NewEntity(entityId, EntityKindRegistry.Movie.Code, "Arrival"));
        db.EntityFiles.Add(NewSourceFile(entityId, path));
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            Kind = EntityKind.Movie,
            EntityId = entityId,
            AcquisitionId = RecordingAcquisitions.AcquisitionId,
            Status = MonitorStatus.Active,
            Title = "Arrival",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        storage.FailPaths.Add(path);
        var acquisitions = new RecordingAcquisitions([entityId], db);
        var recovery = new RecordingRecovery(
            db,
            async () => Assert.Null((await db.Entities.FindAsync(entityId))?.LifecycleClaimKind));
        var jobs = new NullJobQueue(hasPending: false);
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, new RecordingSuppressions(), acquisitions, recovery, jobs,
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var failed = await service.DeleteAsync(entityId, deleteFiles: true, CancellationToken.None);

        Assert.False(failed.Deleted);
        Assert.Equal(MediaEntityDeleteFailureKind.Conflict, failed.FailureKind);
        Assert.Equal(MonitorStatus.DeletingFiles, (await db.Monitors.SingleAsync(row => row.Id == monitorId)).Status);
        Assert.Equal(AcquisitionTeardownIntent.Reacquire, acquisitions.TeardownClaims[RecordingAcquisitions.AcquisitionId]);
        Assert.Equal([RecordingAcquisitions.AcquisitionId], acquisitions.ConfirmedTransferRemovals);
        Assert.Empty(acquisitions.Reacquired);
        Assert.Empty(recovery.Requested);
        Assert.Empty(jobs.Enqueued);
        Assert.NotNull(await db.EntityFiles.SingleOrDefaultAsync(row => row.EntityId == entityId));
        var claimedEntity = await db.Entities.SingleAsync(row => row.Id == entityId);
        Assert.Equal(EntityLifecycleClaimKind.DeletingFiles, claimedEntity.LifecycleClaimKind);
        Assert.NotNull(claimedEntity.LifecycleClaimId);
        Assert.NotNull(claimedEntity.LifecycleClaimedAt);

        storage.FailPaths.Clear();
        var retried = await service.DeleteAsync(entityId, deleteFiles: true, CancellationToken.None);

        Assert.True(retried.Deleted);
        Assert.True(retried.Reverted);
        Assert.Equal(MonitorStatus.Active, (await db.Monitors.SingleAsync(row => row.Id == monitorId)).Status);
        var recoveredEntity = await db.Entities.SingleAsync(row => row.Id == entityId);
        Assert.True(recoveredEntity.IsWanted);
        Assert.Null(recoveredEntity.LifecycleClaimKind);
        Assert.Null(recoveredEntity.LifecycleClaimId);
        Assert.Null(recoveredEntity.LifecycleClaimedAt);
        Assert.Empty(await db.EntityFiles.Where(row => row.EntityId == entityId && row.Role == EntityFileRole.Source).ToArrayAsync());
        Assert.Equal([RecordingAcquisitions.AcquisitionId], acquisitions.Reacquired);
        Assert.Equal(
            [RecordingAcquisitions.AcquisitionId, RecordingAcquisitions.AcquisitionId],
            acquisitions.ConfirmedTransferRemovals);
    }

    [Fact]
    public async Task DeleteFilesTearsDownInFlightUpgradeChildAndClearsStableMonitorPointer() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/movies", "Movies", true, true, false, false, false, false);
        var entityId = Guid.NewGuid();
        var monitorId = Guid.NewGuid();
        var parentAcquisitionId = RecordingAcquisitions.AcquisitionId;
        var upgradeChildId = RecordingAcquisitions.ActiveAcquisitionId;
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(NewEntity(entityId, EntityKindRegistry.Movie.Code, "Arrival"));
        db.EntityFiles.Add(NewSourceFile(entityId, "/media/movies/Arrival/Arrival.mkv"));
        db.Acquisitions.AddRange(
            new AcquisitionRow {
                Id = parentAcquisitionId,
                EntityId = entityId,
                Kind = EntityKind.Movie,
                Status = AcquisitionStatus.Imported,
                Title = "Arrival",
                ExternalIdsJson = "{}",
                SourceUrlsJson = "[]",
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now.AddDays(-1)
            },
            new AcquisitionRow {
                Id = upgradeChildId,
                UpgradeOfAcquisitionId = parentAcquisitionId,
                Kind = EntityKind.Movie,
                Status = AcquisitionStatus.Queued,
                Title = "Arrival upgrade",
                ExternalIdsJson = "{}",
                SourceUrlsJson = "[]",
                CreatedAt = now,
                UpdatedAt = now
            });
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            Kind = EntityKind.Movie,
            EntityId = entityId,
            AcquisitionId = parentAcquisitionId,
            UpgradeChildAcquisitionId = upgradeChildId,
            Status = MonitorStatus.Active,
            Title = "Arrival",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var acquisitions = new RecordingAcquisitions([entityId], db);
        acquisitions.AcquisitionIdsByEntity[entityId] = [parentAcquisitionId, upgradeChildId];
        var service = new MediaEntityDeletionService(
            db,
            new FakeRoots(root),
            new RecordingStorage(),
            new RecordingSuppressions(),
            acquisitions,
            new RecordingRecovery(db),
            new NullJobQueue(),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(entityId, deleteFiles: true, CancellationToken.None);

        Assert.True(result.Deleted);
        Assert.Contains((parentAcquisitionId, AcquisitionTeardownIntent.Reacquire), acquisitions.ClaimedTeardowns);
        Assert.Contains((upgradeChildId, AcquisitionTeardownIntent.Remove), acquisitions.ClaimedTeardowns);
        Assert.Contains(upgradeChildId, acquisitions.ConfirmedTransferRemovals);
        Assert.Contains(upgradeChildId, acquisitions.Deleted);
        var monitor = await db.Monitors.SingleAsync(row => row.Id == monitorId);
        Assert.Null(monitor.UpgradeChildAcquisitionId);
        Assert.Equal(MonitorStatus.Active, monitor.Status);
    }

    [Fact]
    public async Task CanonicalStorageNotFoundIsIdempotentSuccessOnCrashRetry() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/movies", "Movies", true, true, false, false, false, false);
        var entityId = Guid.NewGuid();
        var path = "/media/movies/Arrival/Arrival.mkv";
        db.Entities.Add(NewEntity(entityId, EntityKindRegistry.Movie.Code, "Arrival"));
        db.EntityFiles.Add(NewSourceFile(entityId, path));
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        storage.NotFoundPaths.Add(path);
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, new RecordingSuppressions(), new RecordingAcquisitions([]),
            new RecordingRecovery(), new NullJobQueue(hasPending: false),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(entityId, deleteFiles: true, CancellationToken.None);

        Assert.True(result.Deleted);
        Assert.Equal(1, result.FilesDeleted);
        Assert.Equal([path], storage.AttemptedPaths);
        Assert.Empty(await db.Entities.Where(row => row.Id == entityId).ToArrayAsync());
    }

    [Fact]
    public async Task ArchiveMemberSourcesDeleteTheirPhysicalArchiveExactlyOnce() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/books", "Books", true, true, false, false, false, false);
        var bookId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var archive = "/media/books/Comic/Volume 1.cbz";
        db.Entities.AddRange(
            NewEntity(bookId, EntityKindRegistry.Book.Code, "Comic"),
            NewEntity(chapterId, EntityKindRegistry.BookChapter.Code, "Chapter 1", bookId),
            NewEntity(pageId, EntityKindRegistry.BookPage.Code, "Page 1", chapterId));
        db.EntityFiles.AddRange(
            NewSourceFile(bookId, archive),
            NewSourceFile(chapterId, $"{archive}::chapter-1"),
            NewSourceFile(pageId, $"{archive}::001.jpg"));
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, new RecordingSuppressions(), new RecordingAcquisitions([]),
            new RecordingRecovery(), new NullJobQueue(hasPending: false),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(bookId, deleteFiles: true, CancellationToken.None);

        Assert.True(result.Deleted);
        Assert.Equal([archive], storage.DeletedPaths);
        Assert.Empty(await db.Entities.Where(row => row.Id == bookId || row.Id == chapterId || row.Id == pageId).ToArrayAsync());
    }

    [Fact]
    public async Task SharedDeletionRootRefusesOutsideOwnedMediaBeforeTouchingDisk() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/mixed", "Mixed", true, true, false, false, false, false);
        var galleryId = Guid.NewGuid();
        var movieId = Guid.NewGuid();
        var sharedFolder = "/media/mixed/shared";
        db.Entities.AddRange(
            NewEntity(galleryId, EntityKindRegistry.Gallery.Code, "Gallery"),
            NewEntity(movieId, EntityKindRegistry.Movie.Code, "Movie"));
        db.EntityFiles.AddRange(
            NewSourceFile(galleryId, sharedFolder),
            NewSourceFile(movieId, $"{sharedFolder}/movie.mkv"));
        var monitorId = Guid.NewGuid();
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            Kind = EntityKind.Gallery,
            EntityId = galleryId,
            Status = MonitorStatus.Active,
            Title = "Gallery",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        var acquisitions = new RecordingAcquisitions([galleryId]);
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, new RecordingSuppressions(), acquisitions,
            new RecordingRecovery(), new NullJobQueue(),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(galleryId, deleteFiles: true, CancellationToken.None);

        Assert.False(result.Deleted);
        Assert.Equal(MediaEntityDeleteFailureKind.Conflict, result.FailureKind);
        Assert.Contains("owned by Entity", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(storage.AttemptedPaths);
        Assert.Empty(acquisitions.ClaimedTeardowns);
        Assert.Equal(MonitorStatus.Active, (await db.Monitors.SingleAsync(row => row.Id == monitorId)).Status);
        Assert.Equal(2, await db.Entities.CountAsync());
        Assert.Equal(2, await db.EntityFiles.CountAsync());
    }

    [Fact]
    public async Task StructuralAncestorFolderMayContainADeletedChildSource() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/images", "Images", true, true, false, false, false, false);
        var galleryId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var galleryFolder = "/media/images/Gallery";
        var imagePath = $"{galleryFolder}/one.jpg";
        db.Entities.AddRange(
            NewEntity(galleryId, EntityKindRegistry.Gallery.Code, "Gallery"),
            NewEntity(imageId, EntityKindRegistry.Image.Code, "One", galleryId));
        db.EntityFiles.AddRange(
            NewSourceFile(galleryId, galleryFolder),
            NewSourceFile(imageId, imagePath));
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, new RecordingSuppressions(), new RecordingAcquisitions([]),
            new RecordingRecovery(), new NullJobQueue(),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(imageId, deleteFiles: true, CancellationToken.None);

        Assert.True(result.Deleted);
        Assert.Equal([imagePath], storage.DeletedPaths);
        Assert.NotNull(await db.Entities.SingleOrDefaultAsync(row => row.Id == galleryId));
        Assert.Null(await db.Entities.SingleOrDefaultAsync(row => row.Id == imageId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkDeleteCollapsesSeriesAndSeasonToTheTopmostRootRegardlessOfOrder(
        bool descendantFirst) {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/tv", "TV", true, true, false, false, false, false);
        var seriesId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var seriesPath = "/media/tv/Show";
        db.Entities.AddRange(
            NewEntity(seriesId, EntityKindRegistry.VideoSeries.Code, "Show"),
            NewEntity(seasonId, EntityKindRegistry.VideoSeason.Code, "Season 1", seriesId));
        db.EntityFiles.AddRange(
            NewSourceFile(seriesId, seriesPath),
            NewSourceFile(seasonId, $"{seriesPath}/Season 01"));
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, new RecordingSuppressions(), new RecordingAcquisitions([]),
            new RecordingRecovery(), new NullJobQueue(hasPending: false),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);
        var selected = descendantFirst ? new[] { seasonId, seriesId } : [seriesId, seasonId];

        var result = await service.DeleteManyAsync(selected, deleteFiles: true, CancellationToken.None);

        Assert.Equal(2, result.Deleted);
        Assert.Equal(1, result.FilesDeleted);
        Assert.Empty(result.Failures);
        Assert.Equal([seriesPath], storage.DeletedPaths);
        Assert.Empty(await db.Entities.Where(row => row.Id == seriesId || row.Id == seasonId).ToArrayAsync());
    }

    [Fact]
    public async Task BulkDeleteCollapsesAlbumAndTrackToOnePhysicalOperation() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/music", "Music", true, false, false, true, false, false);
        var albumId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var albumPath = "/media/music/Artist/Album";
        db.Entities.AddRange(
            NewEntity(albumId, EntityKindRegistry.AudioLibrary.Code, "Album"),
            NewEntity(trackId, EntityKindRegistry.AudioTrack.Code, "Track 1", albumId));
        db.EntityFiles.AddRange(
            NewSourceFile(albumId, albumPath),
            NewSourceFile(trackId, $"{albumPath}/01.flac"));
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, new RecordingSuppressions(), new RecordingAcquisitions([]),
            new RecordingRecovery(), new NullJobQueue(hasPending: false),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteManyAsync([trackId, albumId], true, CancellationToken.None);

        Assert.Equal(2, result.Deleted);
        Assert.Empty(result.Failures);
        Assert.Equal([albumPath], storage.DeletedPaths);
    }

    [Fact]
    public async Task BulkDeleteCountsDuplicateSelectionsOnce() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/movies", "Movies", true, true, false, false, false, false);
        var movieId = Guid.NewGuid();
        var moviePath = "/media/movies/Arrival";
        db.Entities.Add(NewEntity(movieId, EntityKindRegistry.Movie.Code, "Arrival"));
        db.EntityFiles.Add(NewSourceFile(movieId, moviePath));
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, new RecordingSuppressions(), new RecordingAcquisitions([]),
            new RecordingRecovery(), new NullJobQueue(hasPending: false),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteManyAsync([movieId, movieId], true, CancellationToken.None);

        Assert.Equal(1, result.Deleted);
        Assert.Empty(result.Failures);
        Assert.Equal([moviePath], storage.DeletedPaths);
    }

    [Fact]
    public async Task BulkDeleteCountsSelectedMonitoredChildAsCoveredWhenTheRootRetainsIt() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/tv", "TV", true, true, false, false, false, false);
        var seriesId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var seriesPath = "/media/tv/Show";
        db.Entities.AddRange(
            NewEntity(seriesId, EntityKindRegistry.VideoSeries.Code, "Show"),
            NewEntity(seasonId, EntityKindRegistry.VideoSeason.Code, "Season 1", seriesId));
        db.EntityFiles.AddRange(
            NewSourceFile(seriesId, seriesPath),
            NewSourceFile(seasonId, $"{seriesPath}/Season 01"));
        db.Monitors.Add(new MonitorRow {
            Id = Guid.NewGuid(),
            Kind = EntityKind.VideoSeason,
            EntityId = seasonId,
            Status = MonitorStatus.Active,
            Title = "Season 1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), new RecordingStorage(), new RecordingSuppressions(), new RecordingAcquisitions([]),
            new RecordingRecovery(), new NullJobQueue(hasPending: false),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteManyAsync([seasonId, seriesId], true, CancellationToken.None);

        Assert.Equal(2, result.Deleted);
        Assert.Equal(1, result.Reverted);
        Assert.Empty(result.Failures);
        Assert.NotNull(await db.Entities.SingleOrDefaultAsync(row => row.Id == seriesId));
        Assert.True((await db.Entities.SingleAsync(row => row.Id == seasonId)).IsWanted);
        Assert.Empty(await db.EntityFiles.Where(row => row.EntityId == seriesId || row.EntityId == seasonId).ToArrayAsync());
    }

    [Fact]
    public async Task EntityOnlyMonitorStaysFrozenAfterStorageFailureThenRecoversOnRetry() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/movies", "Movies", true, true, false, false, false, false);
        var entityId = Guid.NewGuid();
        var monitorId = Guid.NewGuid();
        var path = "/media/movies/Arrival/Arrival.mkv";
        db.Entities.Add(NewEntity(entityId, EntityKindRegistry.Movie.Code, "Arrival"));
        db.EntityFiles.Add(NewSourceFile(entityId, path));
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            Kind = EntityKind.Movie,
            EntityId = entityId,
            Status = MonitorStatus.Active,
            Title = "Arrival",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        storage.FailPaths.Add(path);
        var recovery = new RecordingRecovery(
            db,
            async () => Assert.Null((await db.Entities.FindAsync(entityId))?.LifecycleClaimKind));
        var jobs = new NullJobQueue(hasPending: false);
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, new RecordingSuppressions(), new RecordingAcquisitions([]), recovery, jobs,
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var failed = await service.DeleteAsync(entityId, deleteFiles: true, CancellationToken.None);

        Assert.False(failed.Deleted);
        Assert.Equal(MonitorStatus.DeletingFiles, (await db.Monitors.SingleAsync(row => row.Id == monitorId)).Status);
        Assert.Empty(recovery.Requested);
        Assert.Empty(jobs.Enqueued);

        storage.FailPaths.Clear();
        var retried = await service.DeleteAsync(entityId, deleteFiles: true, CancellationToken.None);

        Assert.True(retried.Deleted);
        Assert.Equal(MonitorStatus.Active, (await db.Monitors.SingleAsync(row => row.Id == monitorId)).Status);
        Assert.Equal([entityId], recovery.Requested);
        Assert.True(recovery.SawWantedFileless);
    }

    [Fact]
    public async Task ReconciliationScanQueuesOnlyAfterEntityCommitAndRecovery() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/movies", "Movies", true, true, false, false, false, false);
        var entityId = Guid.NewGuid();
        db.Entities.Add(NewEntity(entityId, EntityKindRegistry.Movie.Code, "Arrival"));
        db.EntityFiles.Add(NewSourceFile(entityId, "/media/movies/Arrival/Arrival.mkv"));
        db.Monitors.Add(new MonitorRow {
            Id = Guid.NewGuid(), Kind = EntityKind.Movie, EntityId = entityId,
            Status = MonitorStatus.Active, Title = "Arrival",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var jobs = new NullJobQueue(hasPending: false);
        var recovery = new RecordingRecovery(db, async () => {
            Assert.Empty(jobs.Enqueued);
            Assert.True((await db.Entities.SingleAsync(row => row.Id == entityId)).IsWanted);
            Assert.Empty(await db.EntityFiles.Where(row =>
                row.EntityId == entityId && row.Role == EntityFileRole.Source).ToArrayAsync());
        });
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), new RecordingStorage(), new RecordingSuppressions(),
            new RecordingAcquisitions([]), recovery, jobs,
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(entityId, deleteFiles: true, CancellationToken.None);

        Assert.True(result.Deleted);
        Assert.Equal([entityId], recovery.Requested);
        Assert.Contains(jobs.Enqueued, job => job.Type == JobType.ScanLibrary);
    }

    [Fact]
    public async Task PostCommitEntityOnlyRecoveryFailureStillReportsCommittedDeletion() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/movies", "Movies", true, true, false, false, false, false);
        var entityId = Guid.NewGuid();
        var monitorId = Guid.NewGuid();
        db.Entities.Add(NewEntity(entityId, EntityKindRegistry.Movie.Code, "Arrival"));
        db.EntityFiles.Add(NewSourceFile(entityId, "/media/movies/Arrival/Arrival.mkv"));
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            Kind = EntityKind.Movie,
            EntityId = entityId,
            Status = MonitorStatus.Active,
            Title = "Arrival",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var attempts = 0;
        var recovery = new RecordingRecovery(db, () => {
            attempts++;
            throw new IOException("Simulated recovery dependency outage after Entity commit.");
        });
        var jobs = new NullJobQueue(hasPending: false);
        var service = new MediaEntityDeletionService(
            db,
            new FakeRoots(root),
            new RecordingStorage(),
            new RecordingSuppressions(),
            new RecordingAcquisitions([]),
            recovery,
            jobs,
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(entityId, deleteFiles: true, CancellationToken.None);

        Assert.True(result.Deleted);
        Assert.True(result.Reverted);
        Assert.Equal(1, attempts);
        Assert.True((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == entityId)).IsWanted);
        Assert.Empty(await db.EntityFiles.AsNoTracking()
            .Where(row => row.EntityId == entityId && row.Role == EntityFileRole.Source)
            .ToArrayAsync());
        Assert.Equal(
            MonitorStatus.Active,
            (await db.Monitors.AsNoTracking().SingleAsync(row => row.Id == monitorId)).Status);
        Assert.Contains(jobs.Enqueued, job => job.Type == JobType.ScanLibrary);
    }

    [Fact]
    public async Task ReconciliationScanFailureDoesNotMisreportAnAlreadyCommittedDeletion() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/movies", "Movies", true, true, false, false, false, false);
        var entityId = Guid.NewGuid();
        db.Entities.Add(NewEntity(entityId, EntityKindRegistry.Movie.Code, "Arrival"));
        db.EntityFiles.Add(NewSourceFile(entityId, "/media/movies/Arrival/Arrival.mkv"));
        await db.SaveChangesAsync();
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), new RecordingStorage(), new RecordingSuppressions(),
            new RecordingAcquisitions([]), new RecordingRecovery(), new NullJobQueue(hasPending: false, throwOnEnqueue: true),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(entityId, deleteFiles: true, CancellationToken.None);

        Assert.True(result.Deleted);
        Assert.Empty(await db.Entities.Where(row => row.Id == entityId).ToArrayAsync());
        Assert.Empty(await db.EntityFiles.Where(row => row.EntityId == entityId).ToArrayAsync());
    }

    [Fact]
    public async Task MonitorChangeBeforeClaimBlocksBeforeAcquisitionClaimOrExternalEffects() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/movies", "Movies", true, true, false, false, false, false);
        var entityId = Guid.NewGuid();
        var monitorId = Guid.NewGuid();
        db.Entities.Add(NewEntity(entityId, EntityKindRegistry.Movie.Code, "Arrival"));
        db.EntityFiles.Add(NewSourceFile(entityId, "/media/movies/Arrival/Arrival.mkv"));
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            Kind = EntityKind.Movie,
            EntityId = entityId,
            Status = MonitorStatus.Active,
            Title = "Arrival",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var acquisitions = new RecordingAcquisitions([entityId]);
        acquisitions.ReacquireEligibilityObserved = async () => {
            var monitor = await db.Monitors.SingleAsync(row => row.Id == monitorId);
            monitor.Status = MonitorStatus.Paused;
            await db.SaveChangesAsync();
        };
        var storage = new RecordingStorage();
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, new RecordingSuppressions(), acquisitions,
            new RecordingRecovery(), new NullJobQueue(hasPending: false),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(entityId, deleteFiles: true, CancellationToken.None);

        Assert.False(result.Deleted);
        Assert.Equal(MediaEntityDeleteFailureKind.Conflict, result.FailureKind);
        Assert.Equal(MonitorStatus.Paused, (await db.Monitors.SingleAsync(row => row.Id == monitorId)).Status);
        Assert.Empty(acquisitions.ClaimedTeardowns);
        Assert.Empty(acquisitions.ConfirmedTransferRemovals);
        Assert.Empty(storage.AttemptedPaths);
    }

    [Fact]
    public async Task ImportEligibilityChangeAfterEntityClaimBlocksBeforeDiskOrAcquisitionTeardown() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/movies", "Movies", true, true, false, false, false, false);
        var entityId = Guid.NewGuid();
        var monitorId = Guid.NewGuid();
        db.Entities.Add(NewEntity(entityId, EntityKindRegistry.Movie.Code, "Arrival"));
        db.EntityFiles.Add(NewSourceFile(entityId, "/media/movies/Arrival/Arrival.mkv"));
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            Kind = EntityKind.Movie,
            EntityId = entityId,
            Status = MonitorStatus.Active,
            Title = "Arrival",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var acquisitions = new RecordingAcquisitions([entityId]);
        var eligibilityChecks = 0;
        acquisitions.ReacquireEligibilityObserved = () => {
            eligibilityChecks++;
            if (eligibilityChecks == 2) {
                acquisitions.IneligibleReacquireIds.Add(RecordingAcquisitions.AcquisitionId);
            }
            return Task.CompletedTask;
        };
        var storage = new RecordingStorage();
        var service = new MediaEntityDeletionService(
            db,
            new FakeRoots(root),
            storage,
            new RecordingSuppressions(),
            acquisitions,
            new RecordingRecovery(),
            new NullJobQueue(hasPending: false),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(
            entityId,
            deleteFiles: true,
            CancellationToken.None);

        Assert.False(result.Deleted);
        Assert.Equal(MediaEntityDeleteFailureKind.Conflict, result.FailureKind);
        Assert.Equal(2, eligibilityChecks);
        Assert.Empty(acquisitions.ClaimedTeardowns);
        Assert.Empty(acquisitions.ConfirmedTransferRemovals);
        Assert.Empty(storage.AttemptedPaths);
        Assert.Null((await db.Entities.SingleAsync(row => row.Id == entityId)).LifecycleClaimKind);
    }

    [Fact]
    public async Task DeleteFilesCannotAdoptAnUnmonitorStoppingClaim() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/movies", "Movies", true, true, false, false, false, false);
        var entityId = Guid.NewGuid();
        var monitorId = Guid.NewGuid();
        db.Entities.Add(NewEntity(entityId, EntityKindRegistry.Movie.Code, "Arrival"));
        db.EntityFiles.Add(NewSourceFile(entityId, "/media/movies/Arrival/Arrival.mkv"));
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            Kind = EntityKind.Movie,
            EntityId = entityId,
            AcquisitionId = RecordingAcquisitions.AcquisitionId,
            Status = MonitorStatus.Stopping,
            Title = "Arrival",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var acquisitions = new RecordingAcquisitions([entityId]);
        acquisitions.TeardownClaims[RecordingAcquisitions.AcquisitionId] = AcquisitionTeardownIntent.Remove;
        var storage = new RecordingStorage();
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, new RecordingSuppressions(), acquisitions,
            new RecordingRecovery(), new NullJobQueue(hasPending: false),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(entityId, deleteFiles: true, CancellationToken.None);

        Assert.False(result.Deleted);
        Assert.Equal(MediaEntityDeleteFailureKind.Conflict, result.FailureKind);
        Assert.Contains("unmonitored", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(MonitorStatus.Stopping, (await db.Monitors.SingleAsync(row => row.Id == monitorId)).Status);
        Assert.NotNull(await db.EntityFiles.SingleOrDefaultAsync(row => row.EntityId == entityId));
        Assert.Empty(acquisitions.ClaimedTeardowns);
        Assert.Empty(acquisitions.ConfirmedTransferRemovals);
        Assert.Empty(storage.AttemptedPaths);
    }

    [Fact]
    public async Task FilelessClaimedReacquisitionCanResumeAfterLocalReconciliationCrash() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        var monitorId = Guid.NewGuid();
        var entity = NewEntity(entityId, EntityKindRegistry.Movie.Code, "Arrival");
        entity.IsWanted = true;
        db.Entities.Add(entity);
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            Kind = EntityKind.Movie,
            EntityId = entityId,
            AcquisitionId = RecordingAcquisitions.AcquisitionId,
            Status = MonitorStatus.DeletingFiles,
            Title = "Arrival",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var acquisitions = new RecordingAcquisitions([entityId], db);
        acquisitions.TeardownClaims[RecordingAcquisitions.AcquisitionId] = AcquisitionTeardownIntent.Reacquire;
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(), new RecordingStorage(), new RecordingSuppressions(), acquisitions,
            new RecordingRecovery(db), new NullJobQueue(hasPending: false),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(entityId, deleteFiles: true, CancellationToken.None);

        Assert.True(result.Deleted);
        Assert.True(result.Reverted);
        Assert.Equal([RecordingAcquisitions.AcquisitionId], acquisitions.Reacquired);
        Assert.Equal(MonitorStatus.Active, (await db.Monitors.SingleAsync(row => row.Id == monitorId)).Status);
    }

    [Fact]
    public async Task RetryAfterMonitorRetargetCrashFinishesTheDurablyLinkedReplacementExactlyOnce() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/movies", "Movies", true, true, false, false, false, false);
        var entityId = Guid.NewGuid();
        var acquisitionId = Guid.NewGuid();
        var monitorId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(NewEntity(entityId, EntityKindRegistry.Movie.Code, "Arrival"));
        db.EntityFiles.Add(NewSourceFile(entityId, "/media/movies/Arrival/Arrival.mkv"));
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            EntityId = entityId,
            Kind = EntityKind.Movie,
            Status = AcquisitionStatus.Imported,
            Title = "Arrival",
            Year = 2016,
            IdentityNamespace = "tmdb",
            IdentityValue = "329865",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            Kind = EntityKind.Movie,
            EntityId = entityId,
            AcquisitionId = acquisitionId,
            Status = MonitorStatus.Active,
            Title = "Arrival",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var jobs = new MergedImportTestSupport.RecordingJobQueue();
        var monitorStore = new ThrowOnceAfterRetargetMonitorStore(new EfMonitorStore(db));
        var acquisitionService = new AcquisitionService(
            AcquisitionTestFactory.Store(db),
            new ThrowingBlocklistStore(),
            jobs,
            new MergedImportTestSupport.ThrowingClientConfigStore(),
            new MergedImportTestSupport.ThrowingClientFactory(),
            new EmptyImportedFilesReader(),
            new EfAcquisitionHistoryStore(db),
            NullLogger<AcquisitionService>.Instance,
            monitorStore,
            new AcquisitionJobCleanup(db),
            new EfEntityLifecycleMutationLease(db, new EfEntityHierarchyReader(db)));
        var service = new MediaEntityDeletionService(
            db,
            new FakeRoots(root),
            new RecordingStorage(),
            new RecordingSuppressions(),
            acquisitionService,
            new RecordingRecovery(db),
            jobs,
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        await Assert.ThrowsAsync<IOException>(() =>
            service.DeleteAsync(entityId, deleteFiles: true, CancellationToken.None));

        var teardownOwner = await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == acquisitionId);
        var replacementId = Assert.IsType<Guid>(teardownOwner.TeardownReplacementAcquisitionId);
        Assert.Equal(AcquisitionStatus.Stopping, teardownOwner.Status);
        Assert.Equal(AcquisitionTeardownIntent.Reacquire, teardownOwner.TeardownIntent);
        Assert.Equal(
            AcquisitionStatus.Pending,
            (await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == replacementId)).Status);
        var retargetedMonitor = await db.Monitors.AsNoTracking().SingleAsync(row => row.Id == monitorId);
        Assert.Equal((replacementId, MonitorStatus.Active), (retargetedMonitor.AcquisitionId, retargetedMonitor.Status));
        Assert.DoesNotContain(jobs.Enqueued, job => job.Type == JobType.AcquisitionSearch);

        var retried = await service.DeleteAsync(entityId, deleteFiles: true, CancellationToken.None);

        Assert.True(retried.Deleted);
        var survivingAcquisition = await db.Acquisitions.AsNoTracking().SingleAsync();
        Assert.Equal(replacementId, survivingAcquisition.Id);
        Assert.Equal(AcquisitionStatus.Searching, survivingAcquisition.Status);
        Assert.Null(await db.Acquisitions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == acquisitionId));
        retargetedMonitor = await db.Monitors.AsNoTracking().SingleAsync(row => row.Id == monitorId);
        Assert.Equal((replacementId, MonitorStatus.Active), (retargetedMonitor.AcquisitionId, retargetedMonitor.Status));
        var search = Assert.Single(jobs.Enqueued, job => job.Type == JobType.AcquisitionSearch);
        Assert.Equal(replacementId.ToString(), search.TargetEntityId);
        Assert.Equal(replacementId, AcquisitionJobPayload.Parse(search.PayloadJson!).AcquisitionId);
    }

    [Fact]
    public async Task MultiTargetRetrySkipsAnAlreadyReplacedTargetAndFinishesOutstandingClaims() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/tv", "TV", true, true, false, false, false, false);
        var seriesId = Guid.NewGuid();
        var firstSeasonId = Guid.NewGuid();
        var secondSeasonId = Guid.NewGuid();
        var firstMonitorId = Guid.NewGuid();
        var secondMonitorId = Guid.NewGuid();
        db.Entities.AddRange(
            NewEntity(seriesId, EntityKindRegistry.VideoSeries.Code, "Crash Show"),
            NewEntity(firstSeasonId, EntityKindRegistry.VideoSeason.Code, "Season 1", seriesId, 1),
            NewEntity(secondSeasonId, EntityKindRegistry.VideoSeason.Code, "Season 2", seriesId, 2));
        db.EntityFiles.AddRange(
            NewSourceFile(seriesId, "/media/tv/Crash Show"),
            NewSourceFile(firstSeasonId, "/media/tv/Crash Show/Season 01"),
            NewSourceFile(secondSeasonId, "/media/tv/Crash Show/Season 02"));
        db.Monitors.AddRange(
            new MonitorRow {
                Id = firstMonitorId, Kind = EntityKind.VideoSeason, EntityId = firstSeasonId,
                AcquisitionId = RecordingAcquisitions.AcquisitionId, Status = MonitorStatus.Active,
                Title = "Season 1", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            },
            new MonitorRow {
                Id = secondMonitorId, Kind = EntityKind.VideoSeason, EntityId = secondSeasonId,
                AcquisitionId = RecordingAcquisitions.ActiveAcquisitionId, Status = MonitorStatus.Active,
                Title = "Season 2", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();

        var acquisitions = new RecordingAcquisitions([]);
        acquisitions.AcquisitionIdsByEntity[firstSeasonId] = [RecordingAcquisitions.AcquisitionId];
        acquisitions.AcquisitionIdsByEntity[secondSeasonId] = [RecordingAcquisitions.ActiveAcquisitionId];
        var firstReplacementId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000011");
        var secondReplacementId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000012");
        var crashSecondOnce = true;
        acquisitions.ReacquireOverride = async acquisitionId => {
            if (acquisitionId == RecordingAcquisitions.ActiveAcquisitionId && crashSecondOnce) {
                crashSecondOnce = false;
                throw new IOException("Simulated process crash after the first replacement.");
            }

            var isFirst = acquisitionId == RecordingAcquisitions.AcquisitionId;
            var monitorId = isFirst ? firstMonitorId : secondMonitorId;
            var entityId = isFirst ? firstSeasonId : secondSeasonId;
            var replacementId = isFirst ? firstReplacementId : secondReplacementId;
            var monitor = await db.Monitors.SingleAsync(row => row.Id == monitorId);
            monitor.AcquisitionId = replacementId;
            monitor.Status = MonitorStatus.Active;
            monitor.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            acquisitions.AcquisitionIdsByEntity[entityId] = [replacementId];
            acquisitions.TeardownClaims.Remove(acquisitionId);
            return replacementId;
        };
        var recovery = new RecordingRecovery(db);
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), new RecordingStorage(), new RecordingSuppressions(), acquisitions,
            recovery, new NullJobQueue(hasPending: false),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        await Assert.ThrowsAsync<IOException>(() =>
            service.DeleteAsync(seriesId, deleteFiles: true, CancellationToken.None));

        Assert.Equal(MonitorStatus.Active, (await db.Monitors.SingleAsync(row => row.Id == firstMonitorId)).Status);
        Assert.Equal(firstReplacementId, (await db.Monitors.SingleAsync(row => row.Id == firstMonitorId)).AcquisitionId);
        Assert.Equal(MonitorStatus.DeletingFiles, (await db.Monitors.SingleAsync(row => row.Id == secondMonitorId)).Status);
        Assert.Equal(AcquisitionTeardownIntent.Reacquire, acquisitions.TeardownClaims[RecordingAcquisitions.ActiveAcquisitionId]);
        Assert.Empty(await db.EntityFiles.Where(row => row.EntityId == firstSeasonId || row.EntityId == secondSeasonId).ToArrayAsync());

        var retried = await service.DeleteAsync(seriesId, deleteFiles: true, CancellationToken.None);

        Assert.True(retried.Deleted);
        Assert.Equal(MonitorStatus.Active, (await db.Monitors.SingleAsync(row => row.Id == secondMonitorId)).Status);
        Assert.Equal(secondReplacementId, (await db.Monitors.SingleAsync(row => row.Id == secondMonitorId)).AcquisitionId);
        Assert.DoesNotContain(firstReplacementId, acquisitions.ReacquireEligibilityIds);
        Assert.DoesNotContain(firstReplacementId, acquisitions.ConfirmedTransferRemovals);
        Assert.DoesNotContain(acquisitions.ClaimedTeardowns, claim => claim.Id == firstReplacementId);
        Assert.Equal(1, acquisitions.Reacquired.Count(id => id == RecordingAcquisitions.AcquisitionId));
        Assert.Equal(1, acquisitions.Reacquired.Count(id => id == RecordingAcquisitions.ActiveAcquisitionId));
        Assert.Empty(recovery.Requested);
    }

    [Theory]
    [InlineData(ActiveMonitorTarget.EntityId)]
    [InlineData(ActiveMonitorTarget.AcquisitionEntityId)]
    public async Task ActiveMonitorEffectiveTargetsRevertTheEntityToWanted(ActiveMonitorTarget target) {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/movies", "Movies", true, true, false, false, false, false);
        var entityId = Guid.NewGuid();
        db.Entities.Add(NewEntity(entityId, EntityKindRegistry.Movie.Code, "Arrival"));
        db.EntityFiles.Add(NewSourceFile(entityId, "/media/movies/Arrival/Arrival.mkv"));

        var monitor = new MonitorRow {
            Id = Guid.NewGuid(),
            Kind = EntityKind.Movie,
            Status = MonitorStatus.Active,
            Title = "Arrival",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var acquisitions = new RecordingAcquisitions([]);
        switch (target) {
            case ActiveMonitorTarget.EntityId:
                monitor.EntityId = entityId;
                break;
            case ActiveMonitorTarget.AcquisitionEntityId:
                monitor.AcquisitionId = RecordingAcquisitions.AcquisitionId;
                db.Acquisitions.Add(new AcquisitionRow {
                    Id = RecordingAcquisitions.AcquisitionId,
                    Kind = EntityKind.Movie,
                    EntityId = entityId,
                    Status = AcquisitionStatus.Imported,
                    Title = "Arrival",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                acquisitions = new RecordingAcquisitions([entityId], db);
                break;
        }

        db.Monitors.Add(monitor);
        await db.SaveChangesAsync();
        var recovery = new RecordingRecovery(db);
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), new RecordingStorage(), new RecordingSuppressions(), acquisitions,
            recovery, new NullJobQueue(),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(entityId, deleteFiles: true, CancellationToken.None);

        Assert.True(result.Deleted);
        Assert.True(result.Reverted);
        Assert.True((await db.Entities.SingleAsync(row => row.Id == entityId)).IsWanted);
        Assert.Empty(await db.EntityFiles.Where(file => file.EntityId == entityId && file.Role == EntityFileRole.Source).ToArrayAsync());
        if (target == ActiveMonitorTarget.AcquisitionEntityId) {
            Assert.Empty(recovery.Requested);
        } else {
            Assert.Equal([entityId], recovery.Requested);
            Assert.True(recovery.SawWantedFileless);
        }
    }

    [Fact]
    public async Task AncestorMonitorDoesNotOverrideAnExplicitlySuppressedChild() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/tv", "TV", true, true, false, false, false, false);
        var (_, seasonId, episodeId) = await SeedSeriesTreeAsync(db, root.Path, MonitorStatus.Active);
        var seasonIdentity = new ExternalIdentity("tmdbseason", "8379:1");
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = seasonId,
            Provider = seasonIdentity.Namespace,
            Value = seasonIdentity.Value,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var acquisitions = new RecordingAcquisitions([seasonId]);
        var suppressions = new RecordingSuppressions([seasonIdentity]);
        var recovery = new RecordingRecovery();
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), new RecordingStorage(), suppressions, acquisitions,
            recovery, new NullJobQueue(),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(seasonId, deleteFiles: true, CancellationToken.None);

        Assert.True(result.Deleted);
        Assert.False(result.Reverted);
        Assert.Empty(await db.Entities.Where(row => row.Id == seasonId || row.Id == episodeId).ToArrayAsync());
        Assert.Equal([RecordingAcquisitions.AcquisitionId], acquisitions.Deleted);
        Assert.Empty(acquisitions.Reacquired);
        Assert.Empty(recovery.Requested);
        Assert.Contains(seasonIdentity, suppressions.SuppressedIdentities);
    }

    [Fact]
    public async Task MixedTreeRetainsOnlyTheDirectlyMonitoredBranchAndRemovesItsSiblings() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/tv", "TV", true, true, false, false, false, false);
        var tree = await SeedMixedSeriesTreeAsync(db, root.Path);
        var storage = new RecordingStorage();
        var suppressions = new RecordingSuppressions();
        var acquisitions = new RecordingAcquisitions([]);
        acquisitions.AcquisitionIdsByEntity[tree.MonitoredSeasonId] = [
            RecordingAcquisitions.AcquisitionId,
            RecordingAcquisitions.OlderAcquisitionId
        ];
        acquisitions.AcquisitionIdsByEntity[tree.UnmonitoredSeasonId] = [RecordingAcquisitions.ActiveAcquisitionId];
        var recovery = new RecordingRecovery(db);
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, suppressions, acquisitions,
            recovery, new NullJobQueue(),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(tree.SeriesId, deleteFiles: true, CancellationToken.None);

        Assert.True(result.Deleted);
        Assert.True(result.Reverted);
        Assert.Equal([$"{root.Path}/Mixed Show"], storage.DeletedPaths);
        var survivors = await db.Entities.OrderBy(row => row.SortOrder).ToArrayAsync();
        Assert.Equal([tree.SeriesId, tree.MonitoredSeasonId], survivors.Select(row => row.Id));
        Assert.True(survivors.Single(row => row.Id == tree.SeriesId).IsWanted);
        Assert.True(survivors.Single(row => row.Id == tree.MonitoredSeasonId).IsWanted);
        Assert.Empty(await db.EntityFiles.Where(file =>
            (file.EntityId == tree.SeriesId || file.EntityId == tree.MonitoredSeasonId)
            && file.Role == EntityFileRole.Source).ToArrayAsync());
        Assert.Equal([RecordingAcquisitions.AcquisitionId], acquisitions.Reacquired);
        Assert.Equal(
            [RecordingAcquisitions.OlderAcquisitionId, RecordingAcquisitions.ActiveAcquisitionId],
            acquisitions.Deleted);
        Assert.Equal([tree.SeriesId], recovery.Requested);
        Assert.Contains(tree.UnmonitoredSeasonIdentity, suppressions.SuppressedIdentities);
        Assert.DoesNotContain(tree.SeriesIdentity, suppressions.SuppressedIdentities);
        Assert.DoesNotContain(tree.MonitoredSeasonIdentity, suppressions.SuppressedIdentities);
        var monitors = await db.Monitors.ToArrayAsync();
        Assert.Equal(2, monitors.Length);
        Assert.Contains(monitors, monitor => monitor.EntityId == tree.SeriesId && monitor.Status == MonitorStatus.Active);
        Assert.Contains(monitors, monitor => monitor.EntityId == tree.MonitoredSeasonId && monitor.Status == MonitorStatus.Active);
    }

    [Fact]
    public async Task MixedTreeRemovalConflictBlocksBeforeDiskOrEntityMutation() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/tv", "TV", true, true, false, false, false, false);
        var tree = await SeedMixedSeriesTreeAsync(db, root.Path);
        var entityIds = await db.Entities.Select(row => row.Id).OrderBy(value => value).ToArrayAsync();
        var sourceFileIds = await db.EntityFiles
            .Where(file => file.Role == EntityFileRole.Source)
            .Select(file => file.Id)
            .OrderBy(value => value)
            .ToArrayAsync();
        var monitorIds = await db.Monitors.Select(row => row.Id).OrderBy(value => value).ToArrayAsync();
        var storage = new RecordingStorage();
        var suppressions = new RecordingSuppressions();
        var acquisitions = new RecordingAcquisitions([]);
        acquisitions.AcquisitionIdsByEntity[tree.MonitoredSeasonId] = [
            RecordingAcquisitions.AcquisitionId,
            RecordingAcquisitions.OlderAcquisitionId
        ];
        acquisitions.AcquisitionIdsByEntity[tree.UnmonitoredSeasonId] = [RecordingAcquisitions.ActiveAcquisitionId];
        acquisitions.IneligibleRemovalIds.Add(RecordingAcquisitions.ActiveAcquisitionId);
        var recovery = new RecordingRecovery();
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, suppressions, acquisitions,
            recovery, new NullJobQueue(),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(tree.SeriesId, deleteFiles: true, CancellationToken.None);

        Assert.False(result.Deleted);
        Assert.Equal(MediaEntityDeleteFailureKind.Conflict, result.FailureKind);
        Assert.Empty(storage.DeletedPaths);
        Assert.Equal(entityIds, await db.Entities.Select(row => row.Id).OrderBy(value => value).ToArrayAsync());
        Assert.Equal(sourceFileIds, await db.EntityFiles
            .Where(file => file.Role == EntityFileRole.Source)
            .Select(file => file.Id)
            .OrderBy(value => value)
            .ToArrayAsync());
        Assert.Equal(monitorIds, await db.Monitors.Select(row => row.Id).OrderBy(value => value).ToArrayAsync());
        Assert.Empty(acquisitions.Deleted);
        Assert.Empty(acquisitions.Reacquired);
        Assert.Empty(recovery.Requested);
        Assert.Empty(suppressions.SuppressedIdentities);
    }

    [Fact]
    public async Task PhysicalDeletionFailureKeepsTheCompleteDatabaseAndLifecycleState() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/tv", "TV", true, true, false, false, false, false);
        var (seriesId, seasonId, episodeId) = await SeedSeriesTreeAsync(db, root.Path, MonitorStatus.Paused);
        var entityIds = await db.Entities.Select(row => row.Id).OrderBy(value => value).ToArrayAsync();
        var sourceFileIds = await db.EntityFiles
            .Where(file => file.Role == EntityFileRole.Source)
            .Select(file => file.Id)
            .OrderBy(value => value)
            .ToArrayAsync();
        var monitorIds = await db.Monitors.Select(row => row.Id).ToArrayAsync();
        var failedPath = $"{root.Path}/Clifford the Big Red Dog";
        var storage = new RecordingStorage();
        storage.FailPaths.Add(failedPath);
        var acquisitions = new RecordingAcquisitions([seasonId]);
        var suppressions = new RecordingSuppressions();
        var jobs = new NullJobQueue(hasPending: false);
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, suppressions, acquisitions,
            new RecordingRecovery(), jobs,
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(seriesId, deleteFiles: true, CancellationToken.None);

        Assert.False(result.Deleted);
        Assert.Equal(MediaEntityDeleteFailureKind.Conflict, result.FailureKind);
        Assert.Contains(failedPath, result.Message, StringComparison.Ordinal);
        Assert.Equal([failedPath], storage.AttemptedPaths);
        Assert.Empty(storage.DeletedPaths);
        Assert.Equal(entityIds, await db.Entities.Select(row => row.Id).OrderBy(value => value).ToArrayAsync());
        Assert.Equal(sourceFileIds, await db.EntityFiles
            .Where(file => file.Role == EntityFileRole.Source)
            .Select(file => file.Id)
            .OrderBy(value => value)
            .ToArrayAsync());
        Assert.Equal(monitorIds, await db.Monitors.Select(row => row.Id).ToArrayAsync());
        Assert.Empty(acquisitions.Deleted);
        Assert.Empty(acquisitions.Reacquired);
        Assert.Empty(suppressions.SuppressedIdentities);
        Assert.Empty(jobs.Enqueued);
        Assert.NotNull(await db.Entities.SingleOrDefaultAsync(row => row.Id == episodeId));
        var rootClaim = await db.Entities.SingleAsync(row => row.Id == seriesId);
        Assert.Equal(EntityLifecycleClaimKind.DeletingFiles, rootClaim.LifecycleClaimKind);
        Assert.NotNull(rootClaim.LifecycleClaimId);
    }

    [Fact]
    public async Task PartialPhysicalDeletionFailureKeepsDatabaseRowsWithoutReconciliationWork() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/images", "Images", true, false, true, false, false, false);
        var galleryId = Guid.NewGuid();
        var firstImageId = Guid.NewGuid();
        var secondImageId = Guid.NewGuid();
        var firstPath = $"{root.Path}/one.jpg";
        var secondPath = $"{root.Path}/two.jpg";
        db.Entities.AddRange(
            NewEntity(galleryId, EntityKindRegistry.Gallery.Code, "Mixed Gallery"),
            NewEntity(firstImageId, EntityKindRegistry.Image.Code, "One", galleryId, 1),
            NewEntity(secondImageId, EntityKindRegistry.Image.Code, "Two", galleryId, 2));
        db.EntityFiles.AddRange(
            NewSourceFile(firstImageId, firstPath),
            NewSourceFile(secondImageId, secondPath));
        await db.SaveChangesAsync();
        var sourceFileIds = await db.EntityFiles.Select(file => file.Id).OrderBy(value => value).ToArrayAsync();
        var storage = new RecordingStorage();
        storage.FailPaths.Add(secondPath);
        var jobs = new NullJobQueue(hasPending: false);
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, new RecordingSuppressions(),
            new RecordingAcquisitions([]), new RecordingRecovery(), jobs,
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(galleryId, deleteFiles: true, CancellationToken.None);

        Assert.False(result.Deleted);
        Assert.Equal(MediaEntityDeleteFailureKind.Conflict, result.FailureKind);
        Assert.Contains(secondPath, result.Message, StringComparison.Ordinal);
        Assert.Equal([firstPath, secondPath], storage.AttemptedPaths);
        Assert.Equal([firstPath], storage.DeletedPaths);
        Assert.Equal(3, await db.Entities.CountAsync());
        Assert.Equal(sourceFileIds, await db.EntityFiles.Select(file => file.Id).OrderBy(value => value).ToArrayAsync());
        Assert.Empty(jobs.Enqueued);
    }

    [Fact]
    public async Task DirectParentMonitorRetainsOnlyItsOwnEntity() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/tv", "TV", true, true, false, false, false, false);
        var (seriesId, seasonId, episodeId) = await SeedSeriesTreeAsync(db, root.Path, MonitorStatus.Active);
        var recovery = new RecordingRecovery(db);
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), new RecordingStorage(), new RecordingSuppressions(),
            new RecordingAcquisitions([]), recovery, new NullJobQueue(),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(seriesId, deleteFiles: true, CancellationToken.None);

        Assert.True(result.Reverted);
        var rows = await db.Entities.Where(row => row.Id == seriesId || row.Id == seasonId || row.Id == episodeId).ToArrayAsync();
        var series = Assert.Single(rows);
        Assert.Equal(seriesId, series.Id);
        Assert.True(series.IsWanted);
        Assert.Equal([seriesId], recovery.Requested);
    }

    [Fact]
    public async Task DirectParentMonitorDoesNotRetainAChildOffBranch() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/tv", "TV", true, true, false, false, false, false);
        var (seriesId, seasonId, episodeId) = await SeedSeriesTreeAsync(db, root.Path, MonitorStatus.Active);
        var seasonIdentity = new ExternalIdentity("tmdbseason", "8379:1");
        db.EntityExternalIds.Add(NewExternalId(seasonId, seasonIdentity));
        await db.SaveChangesAsync();
        var recovery = new RecordingRecovery(db);
        var suppressions = new RecordingSuppressions([seasonIdentity]);
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), new RecordingStorage(), suppressions,
            new RecordingAcquisitions([]), recovery, new NullJobQueue(),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(seriesId, deleteFiles: true, CancellationToken.None);

        Assert.True(result.Reverted);
        var series = await db.Entities.SingleAsync(row => row.Id == seriesId);
        Assert.True(series.IsWanted);
        Assert.Null(await db.Entities.SingleOrDefaultAsync(row => row.Id == seasonId));
        Assert.Null(await db.Entities.SingleOrDefaultAsync(row => row.Id == episodeId));
        Assert.Equal([seriesId], recovery.Requested);
        Assert.Contains(seasonIdentity, suppressions.SuppressedIdentities);
    }

    [Fact]
    public async Task RefusesUnknownAndNonMediaKinds() {
        await using var db = CreateContext();
        var tagId = Guid.NewGuid();
        db.Entities.Add(NewEntity(tagId, EntityKindRegistry.Tag.Code, "Some Tag"));
        await db.SaveChangesAsync();

        var service = new MediaEntityDeletionService(
            db, new FakeRoots(), new RecordingStorage(), new RecordingSuppressions(),
            new RecordingAcquisitions([]), new RecordingRecovery(), new NullJobQueue(),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var missing = await service.DeleteAsync(Guid.NewGuid(), true, CancellationToken.None);
        Assert.False(missing.Deleted);
        Assert.Equal(MediaEntityDeleteFailureKind.NotFound, missing.FailureKind);
        var tag = await service.DeleteAsync(tagId, true, CancellationToken.None);
        Assert.False(tag.Deleted);
        Assert.Equal(MediaEntityDeleteFailureKind.NotDeletable, tag.FailureKind);
        Assert.NotNull(await db.Entities.FirstOrDefaultAsync(row => row.Id == tagId));
    }

    [Fact]
    public async Task DeleteFilesRefusesAFilelessWantedEntityWithoutLifecycleSideEffects() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        var monitorId = Guid.NewGuid();
        var entity = NewEntity(entityId, EntityKindRegistry.Movie.Code, "Arrival");
        entity.IsWanted = true;
        db.Entities.Add(entity);
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            Kind = EntityKind.Movie,
            EntityId = entityId,
            Status = MonitorStatus.Active,
            Title = "Arrival",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        var acquisitions = new RecordingAcquisitions([entityId]);
        var recovery = new RecordingRecovery();
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(), storage, new RecordingSuppressions(), acquisitions, recovery,
            new NullJobQueue(hasPending: false),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            new EfEntityHierarchyReader(db),
            NullLogger<MediaEntityDeletionService>.Instance);

        var result = await service.DeleteAsync(entityId, deleteFiles: true, CancellationToken.None);

        Assert.False(result.Deleted);
        Assert.Equal(MediaEntityDeleteFailureKind.NotDeletable, result.FailureKind);
        Assert.Contains("no managed source files", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await db.Entities.SingleOrDefaultAsync(row => row.Id == entityId));
        Assert.NotNull(await db.Monitors.SingleOrDefaultAsync(row => row.Id == monitorId));
        Assert.Empty(storage.AttemptedPaths);
        Assert.Empty(acquisitions.ConfirmedTransferRemovals);
        Assert.Empty(acquisitions.Deleted);
        Assert.Empty(acquisitions.Reacquired);
        Assert.Empty(recovery.Requested);
    }

    /// <summary>A series → season → episode tree with source paths under <paramref name="basePath"/>, a container monitor, and a tmdb identity.</summary>
    private static async Task<(Guid SeriesId, Guid SeasonId, Guid EpisodeId)> SeedSeriesTreeAsync(
        PrismediaDbContext db, string basePath, MonitorStatus monitorStatus = MonitorStatus.Paused) {
        var seriesId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        db.Entities.AddRange(
            NewEntity(seriesId, EntityKindRegistry.VideoSeries.Code, "Clifford the Big Red Dog"),
            NewEntity(seasonId, EntityKindRegistry.VideoSeason.Code, "Season 1", seriesId),
            NewEntity(episodeId, EntityKindRegistry.Video.Code, "Little Clifford", seasonId));
        db.EntityFiles.AddRange(
            NewSourceFile(seriesId, $"{basePath}/Clifford the Big Red Dog"),
            NewSourceFile(seasonId, $"{basePath}/Clifford the Big Red Dog/Season 01"),
            NewSourceFile(episodeId, $"{basePath}/Clifford the Big Red Dog/Season 01/Clifford S01E01.mkv"));
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(), EntityId = seriesId, Provider = " TMDB ", Value = " 8379 ",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        db.Monitors.Add(new MonitorRow {
            Id = Guid.NewGuid(), Kind = EntityKind.VideoSeries, EntityId = seriesId, Status = monitorStatus,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return (seriesId, seasonId, episodeId);
    }

    private static async Task<MixedSeriesTree> SeedMixedSeriesTreeAsync(
        PrismediaDbContext db,
        string basePath) {
        var seriesId = Guid.NewGuid();
        var monitoredSeasonId = Guid.NewGuid();
        var monitoredEpisodeId = Guid.NewGuid();
        var unmonitoredSeasonId = Guid.NewGuid();
        var unmonitoredEpisodeId = Guid.NewGuid();
        db.Entities.AddRange(
            NewEntity(seriesId, EntityKindRegistry.VideoSeries.Code, "Mixed Show", sortOrder: 0),
            NewEntity(monitoredSeasonId, EntityKindRegistry.VideoSeason.Code, "Season 1", seriesId, 1),
            NewEntity(monitoredEpisodeId, EntityKindRegistry.Video.Code, "Episode 1", monitoredSeasonId, 1),
            NewEntity(unmonitoredSeasonId, EntityKindRegistry.VideoSeason.Code, "Season 2", seriesId, 2),
            NewEntity(unmonitoredEpisodeId, EntityKindRegistry.Video.Code, "Episode 2", unmonitoredSeasonId, 1));
        db.EntityFiles.AddRange(
            NewSourceFile(seriesId, $"{basePath}/Mixed Show"),
            NewSourceFile(monitoredSeasonId, $"{basePath}/Mixed Show/Season 01"),
            NewSourceFile(monitoredEpisodeId, $"{basePath}/Mixed Show/Season 01/Mixed Show S01E01.mkv"),
            NewSourceFile(unmonitoredSeasonId, $"{basePath}/Mixed Show/Season 02"),
            NewSourceFile(unmonitoredEpisodeId, $"{basePath}/Mixed Show/Season 02/Mixed Show S02E01.mkv"));
        var seriesIdentity = new ExternalIdentity("tmdb", "101");
        var monitoredSeasonIdentity = new ExternalIdentity("tmdbseason", "101:1");
        var unmonitoredSeasonIdentity = new ExternalIdentity("tmdbseason", "101:2");
        db.EntityExternalIds.AddRange(
            NewExternalId(seriesId, seriesIdentity),
            NewExternalId(monitoredSeasonId, monitoredSeasonIdentity),
            NewExternalId(unmonitoredSeasonId, unmonitoredSeasonIdentity));
        db.Monitors.AddRange(
            new MonitorRow {
                Id = Guid.NewGuid(), Kind = EntityKind.VideoSeries, EntityId = seriesId,
                Status = MonitorStatus.Active, Title = "Mixed Show",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            },
            new MonitorRow {
                Id = Guid.NewGuid(), Kind = EntityKind.VideoSeason, EntityId = monitoredSeasonId,
                AcquisitionId = RecordingAcquisitions.OlderAcquisitionId,
                Status = MonitorStatus.Active, Title = "Season 1",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            },
            new MonitorRow {
                Id = Guid.NewGuid(), Kind = EntityKind.VideoSeason, EntityId = unmonitoredSeasonId,
                Status = MonitorStatus.Paused, Title = "Season 2",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();
        return new MixedSeriesTree(
            seriesId,
            monitoredSeasonId,
            monitoredEpisodeId,
            unmonitoredSeasonId,
            unmonitoredEpisodeId,
            seriesIdentity,
            monitoredSeasonIdentity,
            unmonitoredSeasonIdentity);
    }

    private static EntityRow NewEntity(
        Guid id,
        string kind,
        string title,
        Guid? parentId = null,
        int? sortOrder = null) => new() {
        Id = id, KindCode = kind, Title = title, ParentEntityId = parentId, SortOrder = sortOrder,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };

    private static EntityExternalIdRow NewExternalId(Guid entityId, ExternalIdentity identity) => new() {
        Id = Guid.NewGuid(), EntityId = entityId, Provider = identity.Namespace, Value = identity.Value,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };

    private static EntityFileRow NewSourceFile(Guid entityId, string path) => new() {
        Id = Guid.NewGuid(), EntityId = entityId, Role = EntityFileRole.Source, Path = path,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeRoots(params FileLibraryRoot[] roots) : IFilesPersistence {
        public Task<IReadOnlyList<FileLibraryRoot>> ListRootsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileLibraryRoot>>(roots);
        public Task<FileLibraryRoot?> GetRootAsync(Guid rootId, CancellationToken cancellationToken) =>
            Task.FromResult(roots.FirstOrDefault(root => root.Id == rootId));
        public Task<IReadOnlyList<FileLinkedEntity>> ListLinkedEntitiesAsync(string absolutePath, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileLinkedEntity>>([]);
        public Task<IReadOnlySet<string>> ListHiddenPathsAsync(string scopeDirectory, IReadOnlyList<string> absolutePaths, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<IReadOnlySet<string>> ListExcludedRelativePathsAsync(Guid rootId, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task UpsertExclusionAsync(Guid rootId, string relativePath, FileEntryKind kind, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveExclusionAsync(Guid rootId, string relativePath, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ApplyPathPrefixRewriteAsync(string sourcePath, string targetPath, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingStorage : IManagedFileStorage {
        public List<string> AttemptedPaths { get; } = [];
        public List<string> DeletedPaths { get; } = [];
        public HashSet<string> FailPaths { get; } = [];
        public HashSet<string> NotFoundPaths { get; } = [];
        public Task DeleteAsync(ResolvedFilePath path, CancellationToken cancellationToken) {
            AttemptedPaths.Add(path.AbsolutePath);
            if (NotFoundPaths.Contains(path.AbsolutePath)) {
                throw new FileOperationException(ApiProblemCodes.NotFound, "Path was not found.");
            }
            if (FailPaths.Contains(path.AbsolutePath)) {
                throw new IOException($"Simulated storage failure for '{path.AbsolutePath}'.");
            }
            DeletedPaths.Add(path.AbsolutePath);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FileEntry>> ListChildrenAsync(ResolvedFilePath directory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileDetail> GetDetailAsync(ResolvedFilePath path, IReadOnlyList<FileLinkedEntity> linkedEntities, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FileContentInfo> GetContentInfoAsync(ResolvedFilePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(ResolvedFilePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task WriteFileAsync(ResolvedFilePath path, Stream content, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MoveAsync(ResolvedFilePath source, ResolvedFilePath target, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingSuppressions(params ExternalIdentity[] initiallySuppressed) : IWantedSuppressionStore {
        public List<string> Suppressed { get; } = [];
        public HashSet<ExternalIdentity> SuppressedIdentities { get; } = initiallySuppressed.ToHashSet();
        public Task SuppressAsync(IReadOnlyList<ExternalIdentity> identities, EntityKind kind, string title, CancellationToken cancellationToken) {
            Suppressed.AddRange(identities.Select(identity => $"{identity.Namespace}:{identity.Value}"));
            SuppressedIdentities.UnionWith(identities);
            return Task.CompletedTask;
        }

        public Task<IReadOnlySet<ExternalIdentity>> FilterSuppressedAsync(
            IReadOnlyList<ExternalIdentity> identities,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<ExternalIdentity>>(identities.Where(SuppressedIdentities.Contains).ToHashSet());
        public Task ClearAsync(IReadOnlyList<ExternalIdentity> identities, CancellationToken cancellationToken) {
            SuppressedIdentities.ExceptWith(identities);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRecovery(
        PrismediaDbContext? db = null,
        Func<Task>? onMaintain = null) : IMonitoredEntityRecovery {
        public List<Guid> Requested { get; } = [];
        public bool SawWantedFileless { get; private set; }

        public Task<bool> MaintainAsync(Guid entityId, CancellationToken cancellationToken) =>
            RequestIfMonitoredAndFilelessAsync(entityId, cancellationToken);

        public async Task<bool> RequestIfMonitoredAndFilelessAsync(
            Guid entityId,
            CancellationToken cancellationToken) {
            if (onMaintain is not null) {
                await onMaintain();
            }
            Requested.Add(entityId);
            if (db is not null) {
                SawWantedFileless = await db.Entities.AsNoTracking()
                    .AnyAsync(entity => entity.Id == entityId && entity.IsWanted, cancellationToken)
                    && !await db.EntityFiles.AsNoTracking()
                        .AnyAsync(file => file.EntityId == entityId && file.Role == EntityFileRole.Source, cancellationToken);
            }

            return true;
        }
    }

    public enum ActiveMonitorTarget {
        EntityId,
        AcquisitionEntityId
    }

    private sealed record MixedSeriesTree(
        Guid SeriesId,
        Guid MonitoredSeasonId,
        Guid MonitoredEpisodeId,
        Guid UnmonitoredSeasonId,
        Guid UnmonitoredEpisodeId,
        ExternalIdentity SeriesIdentity,
        ExternalIdentity MonitoredSeasonIdentity,
        ExternalIdentity UnmonitoredSeasonIdentity);

    private sealed class RecordingAcquisitions(
        IReadOnlyList<Guid> entityIdsWithAcquisition,
        PrismediaDbContext? db = null) : IAcquisitionRequestService {
        public static readonly Guid AcquisitionId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        public static readonly Guid OlderAcquisitionId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        public static readonly Guid ActiveAcquisitionId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
        public Dictionary<Guid, IReadOnlyList<Guid>> AcquisitionIdsByEntity { get; } =
            entityIdsWithAcquisition.ToDictionary(
                entityId => entityId,
                _ => (IReadOnlyList<Guid>)[AcquisitionId]);
        public List<Guid> Deleted { get; } = [];
        public List<Guid> Reacquired { get; } = [];
        public List<Guid> ConfirmedTransferRemovals { get; } = [];
        public List<(Guid Id, AcquisitionTeardownIntent Intent)> ClaimedTeardowns { get; } = [];
        public List<(Guid Id, AcquisitionTeardownIntent Intent)> CompletedTeardowns { get; } = [];
        public Dictionary<Guid, AcquisitionTeardownIntent> TeardownClaims { get; } = [];
        public HashSet<Guid> IneligibleReacquireIds { get; } = [];
        public HashSet<Guid> IneligibleRemovalIds { get; } = [];
        public HashSet<Guid> TransferRemovalFailureIds { get; } = [];
        public Func<Task>? ReacquireEligibilityObserved { get; set; }
        public Func<Guid, Task<Guid?>>? ReacquireOverride { get; set; }
        public List<Guid> ReacquireEligibilityIds { get; } = [];
        public bool ReacquireSawWantedFileless { get; private set; }
        public Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(AcquisitionIdsByEntity.GetValueOrDefault(entityId) ?? []);

        public async Task<AcquisitionReacquireEligibility> GetReacquireEligibilityAsync(
            Guid id,
            CancellationToken cancellationToken) {
            ReacquireEligibilityIds.Add(id);
            if (ReacquireEligibilityObserved is not null) {
                await ReacquireEligibilityObserved();
            }
            return IneligibleReacquireIds.Contains(id)
                ? new AcquisitionReacquireEligibility(
                    false,
                    "This acquisition cannot be changed while it is downloading.")
                : new AcquisitionReacquireEligibility(true);
        }

        public Task<AcquisitionRemovalEligibility> GetRemovalEligibilityAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(IneligibleRemovalIds.Contains(id)
                ? new AcquisitionRemovalEligibility(false, "This acquisition cannot be removed safely right now.")
                : new AcquisitionRemovalEligibility(true));

        public Task ConfirmTransferRemovedAsync(Guid id, CancellationToken cancellationToken) {
            ConfirmedTransferRemovals.Add(id);
            return TransferRemovalFailureIds.Contains(id)
                ? Task.FromException(new AcquisitionConfigurationException(
                    ApiProblemCodes.AcquisitionInvalid,
                    "The recorded download client is unavailable."))
                : Task.CompletedTask;
        }

        public Task<bool> ClaimTeardownAsync(
            Guid id,
            AcquisitionTeardownIntent intent,
            CancellationToken cancellationToken) {
            if (TeardownClaims.TryGetValue(id, out var existing) && existing != intent) {
                throw new AcquisitionConfigurationException(
                    ApiProblemCodes.AcquisitionInvalid,
                    "The acquisition already has a different cleanup claim.");
            }
            TeardownClaims[id] = intent;
            ClaimedTeardowns.Add((id, intent));
            return Task.FromResult(true);
        }

        public Task<bool> CompleteTeardownAsync(
            Guid id,
            AcquisitionTeardownIntent intent,
            CancellationToken cancellationToken) {
            Assert.Equal(intent, TeardownClaims[id]);
            CompletedTeardowns.Add((id, intent));
            Deleted.Add(id);
            TeardownClaims.Remove(id);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken, bool preserveWantedLoop = false) {
            Deleted.Add(id);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteForUnmonitorAsync(Guid id, CancellationToken cancellationToken) =>
            DeleteAsync(id, cancellationToken);

        public async Task<Guid?> ReacquireAsync(Guid id, CancellationToken cancellationToken) {
            if (IneligibleReacquireIds.Contains(id)) {
                throw new InvalidOperationException("This acquisition cannot be changed while it is downloading.");
            }

            if (ReacquireOverride is not null) {
                var replacement = await ReacquireOverride(id);
                Reacquired.Add(id);
                return replacement;
            }

            Reacquired.Add(id);
            if (db is not null) {
                var entityId = entityIdsWithAcquisition.Single();
                ReacquireSawWantedFileless = await db.Entities.AsNoTracking()
                    .AnyAsync(entity => entity.Id == entityId && entity.IsWanted, cancellationToken)
                    && !await db.EntityFiles.AsNoTracking()
                        .AnyAsync(file => file.EntityId == entityId && file.Role == EntityFileRole.Source, cancellationToken);
            }

            return Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
        }

        public Task<AcquisitionSummary> CreateAndSearchAsync(AcquisitionCreateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> AnyOpenForEntityAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class ThrowOnceAfterRetargetMonitorStore(IMonitorStore inner) : IMonitorStore {
        private bool _throwAfterRetarget = true;

        public Task<MonitorView> StartAsync(Guid acquisitionId, EntityKind kind, string title, string? author, CancellationToken cancellationToken) =>
            inner.StartAsync(acquisitionId, kind, title, author, cancellationToken);

        public Task<MonitorView> StartForEntityAsync(Guid entityId, EntityKind kind, string title, AcquisitionTargeting? targeting, MonitorPreset? preset, CancellationToken cancellationToken) =>
            inner.StartForEntityAsync(entityId, kind, title, targeting, preset, cancellationToken);

        public Task<MonitorView?> GetByEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
            inner.GetByEntityAsync(entityId, cancellationToken);

        public Task<AcquisitionTargeting?> GetTargetingByEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
            inner.GetTargetingByEntityAsync(entityId, cancellationToken);

        public Task<MonitorPreset?> GetPresetByEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
            inner.GetPresetByEntityAsync(entityId, cancellationToken);

        public Task<bool> DeleteAsync(Guid monitorId, CancellationToken cancellationToken) =>
            inner.DeleteAsync(monitorId, cancellationToken);

        public Task<bool> RetargetAsync(Guid fromAcquisitionId, Guid toAcquisitionId, CancellationToken cancellationToken) =>
            inner.RetargetAsync(fromAcquisitionId, toAcquisitionId, cancellationToken);

        public async Task<bool> RetargetAfterFileDeletionAsync(
            Guid fromAcquisitionId,
            Guid toAcquisitionId,
            CancellationToken cancellationToken) {
            var retargeted = await inner.RetargetAfterFileDeletionAsync(
                fromAcquisitionId,
                toAcquisitionId,
                cancellationToken);
            if (_throwAfterRetarget) {
                _throwAfterRetarget = false;
                throw new IOException("Simulated process crash after the monitor retarget committed.");
            }

            return retargeted;
        }

        public Task<bool> SetStatusAsync(Guid monitorId, MonitorStatus status, CancellationToken cancellationToken) =>
            inner.SetStatusAsync(monitorId, status, cancellationToken);

        public Task<IReadOnlyList<MonitorView>> ListAsync(CancellationToken cancellationToken) =>
            inner.ListAsync(cancellationToken);

        public Task<WantedPage> ListMissingAsync(int page, int pageSize, EntityKind? kind, CancellationToken cancellationToken) =>
            inner.ListMissingAsync(page, pageSize, kind, cancellationToken);

        public Task<WantedPage> ListCutoffUnmetAsync(int page, int pageSize, EntityKind? kind, CancellationToken cancellationToken) =>
            inner.ListCutoffUnmetAsync(page, pageSize, kind, cancellationToken);

        public Task<MonitorView?> GetByAcquisitionAsync(Guid acquisitionId, CancellationToken cancellationToken) =>
            inner.GetByAcquisitionAsync(acquisitionId, cancellationToken);

        public Task<bool> HasActiveMonitorsAsync(CancellationToken cancellationToken) =>
            inner.HasActiveMonitorsAsync(cancellationToken);

        public Task<IReadOnlyList<DueMonitor>> ListDueMonitorsAsync(int defaultIntervalMinutes, CancellationToken cancellationToken) =>
            inner.ListDueMonitorsAsync(defaultIntervalMinutes, cancellationToken);

        public Task MarkSearchedAsync(Guid monitorId, CancellationToken cancellationToken) =>
            inner.MarkSearchedAsync(monitorId, cancellationToken);

        public Task<Guid?> CreateUpgradeChildAsync(Guid monitorId, CancellationToken cancellationToken) =>
            inner.CreateUpgradeChildAsync(monitorId, cancellationToken);

        public Task ResolveUpgradeChildAsync(Guid childId, bool succeeded, CancellationToken cancellationToken) =>
            inner.ResolveUpgradeChildAsync(childId, succeeded, cancellationToken);
    }

    private sealed class ThrowingBlocklistStore : IAcquisitionBlocklistStore {
        public Task<IReadOnlySet<string>> GetIdentitiesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(BlocklistAddRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AcquisitionBlocklistEntry>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class EmptyImportedFilesReader : IImportedFilesReader {
        public IReadOnlyList<DownloadItemFile> List(string path) => [];
    }

    private sealed class NullJobQueue(bool hasPending = true, bool throwOnEnqueue = false) : IJobQueueService {
        public List<EnqueueJobRequest> Enqueued { get; } = [];
        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobRunSnapshot>>([]);
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            if (throwOnEnqueue) {
                throw new InvalidOperationException("Simulated scan enqueue failure after deletion commit.");
            }
            Enqueued.Add(request);
            return Task.FromResult(new JobRunSnapshot(
                Guid.NewGuid(), request.Type, JobRunStatus.Queued, 0, null,
                request.PayloadJson ?? "{}", null, request.TargetEntityId, request.TargetLabel,
                DateTimeOffset.UtcNow, null, null));
        }
        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) => Task.FromResult(hasPending);
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken, JobRunLane? lane = null) => Task.FromResult<JobRunSnapshot?>(null);
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobQueueCount>>([]);
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
