using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Files;
using Prismedia.Domain.Entities;
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
            db, new FakeRoots(root), storage, suppressions, acquisitions, new NullJobQueue(),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
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

    [Fact]
    public async Task KeepsFilesWhenDeleteFilesIsFalseAndSkipsPathsOutsideRoots() {
        await using var db = CreateContext();
        var root = new FileLibraryRoot(Guid.NewGuid(), "/media/tv", "TV", true, true, false, false, false, false);
        var (seriesId, _, _) = await SeedSeriesTreeAsync(db, "/somewhere/else");

        var storage = new RecordingStorage();
        var service = new MediaEntityDeletionService(
            db, new FakeRoots(root), storage, new RecordingSuppressions(), new RecordingAcquisitions([]),
            new NullJobQueue(),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            NullLogger<MediaEntityDeletionService>.Instance);

        // deleteFiles false → rows gone, disk untouched.
        var result = await service.DeleteAsync(seriesId, deleteFiles: false, CancellationToken.None);
        Assert.True(result.Deleted);
        Assert.Empty(storage.DeletedPaths);
        Assert.Empty(await db.Entities.ToArrayAsync());
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
            Status = MonitorStatus.Fulfilled,
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
            new NullJobQueue(),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            NullLogger<MediaEntityDeletionService>.Instance);

        // Deleting the SEASON while the SERIES is actively monitored: disk state goes, library state
        // reverts to wanted so the monitoring loop re-acquires it. Monitoring itself is untouched.
        var result = await service.DeleteAsync(seasonId, deleteFiles: true, CancellationToken.None);

        Assert.True(result.Deleted);
        Assert.True(result.Reverted);
        Assert.Equal(["/media/tv/Clifford the Big Red Dog/Season 01"], storage.DeletedPaths);
        // Season and episode rows survive as wanted placeholders with their source bindings cleared…
        var season = await db.Entities.SingleAsync(row => row.Id == seasonId);
        var episode = await db.Entities.SingleAsync(row => row.Id == episodeId);
        Assert.True(season.IsWanted);
        Assert.True(episode.IsWanted);
        Assert.Empty(await db.EntityFiles.Where(file => file.EntityId == seasonId || file.EntityId == episodeId).ToArrayAsync());
        // …the series and both its container monitor and the season's acquisition monitor stay. The
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
                jobs,
                new Prismedia.Infrastructure.Media.Processing.AssetPathService(cacheRoot),
                NullLogger<MediaEntityDeletionService>.Instance);

            var result = await service.DeleteAsync(seasonId, deleteFiles: true, CancellationToken.None);

            Assert.False(result.Deleted);
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
    public async Task RefusesUnknownAndNonMediaKinds() {
        await using var db = CreateContext();
        var tagId = Guid.NewGuid();
        db.Entities.Add(NewEntity(tagId, EntityKindRegistry.Tag.Code, "Some Tag"));
        await db.SaveChangesAsync();

        var service = new MediaEntityDeletionService(
            db, new FakeRoots(), new RecordingStorage(), new RecordingSuppressions(),
            new RecordingAcquisitions([]), new NullJobQueue(),
            new Prismedia.Infrastructure.Media.Processing.AssetPathService(System.IO.Path.GetTempPath()),
            NullLogger<MediaEntityDeletionService>.Instance);

        Assert.False((await service.DeleteAsync(Guid.NewGuid(), true, CancellationToken.None)).Deleted);
        var tag = await service.DeleteAsync(tagId, true, CancellationToken.None);
        Assert.False(tag.Deleted);
        Assert.NotNull(await db.Entities.FirstOrDefaultAsync(row => row.Id == tagId));
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

    private static EntityRow NewEntity(Guid id, string kind, string title, Guid? parentId = null) => new() {
        Id = id, KindCode = kind, Title = title, ParentEntityId = parentId,
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
        public List<string> DeletedPaths { get; } = [];
        public Task DeleteAsync(ResolvedFilePath path, CancellationToken cancellationToken) {
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

    private sealed class RecordingSuppressions : IWantedSuppressionStore {
        public List<string> Suppressed { get; } = [];
        public Task SuppressAsync(IReadOnlyList<ExternalIdentity> identities, EntityKind kind, string title, CancellationToken cancellationToken) {
            Suppressed.AddRange(identities.Select(identity => $"{identity.Namespace}:{identity.Value}"));
            return Task.CompletedTask;
        }

        public Task<IReadOnlySet<ExternalIdentity>> FilterSuppressedAsync(
            IReadOnlyList<ExternalIdentity> identities,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<ExternalIdentity>>(new HashSet<ExternalIdentity>());
        public Task ClearAsync(IReadOnlyList<ExternalIdentity> identities, CancellationToken cancellationToken) => Task.CompletedTask;
    }

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
        public HashSet<Guid> IneligibleReacquireIds { get; } = [];
        public bool ReacquireSawWantedFileless { get; private set; }
        public Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(AcquisitionIdsByEntity.GetValueOrDefault(entityId) ?? []);

        public Task<AcquisitionReacquireEligibility> GetReacquireEligibilityAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(IneligibleReacquireIds.Contains(id)
                ? new AcquisitionReacquireEligibility(
                    false,
                    "This acquisition cannot be changed while it is downloading.")
                : new AcquisitionReacquireEligibility(true));

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken, bool preserveWantedLoop = false) {
            Deleted.Add(id);
            return Task.FromResult(true);
        }

        public async Task<Guid?> ReacquireAsync(Guid id, CancellationToken cancellationToken) {
            if (IneligibleReacquireIds.Contains(id)) {
                throw new InvalidOperationException("This acquisition cannot be changed while it is downloading.");
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
        public Task<bool> AnyForEntityAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class NullJobQueue(bool hasPending = true) : IJobQueueService {
        public List<EnqueueJobRequest> Enqueued { get; } = [];
        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobRunSnapshot>>([]);
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
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
