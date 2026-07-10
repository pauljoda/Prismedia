using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Locks generalized subtree scope, root-only suppression, and source-preserving pruning.</summary>
public sealed class EfEntityUnmonitorPersistenceTests {
    [Fact]
    public async Task ContainerOffScopesEveryDescendantButPrunesFilelessAcquisitionOnlyBranches() {
        await using var db = CreateContext();
        var artist = AddEntity(db, EntityKind.MusicArtist, "Artist", wanted: true);
        var album = AddEntity(db, EntityKind.AudioLibrary, "Owned album", wanted: false, artist);
        var track = AddEntity(db, EntityKind.AudioTrack, "Wanted track", wanted: true, album);
        var futureAlbum = AddEntity(db, EntityKind.AudioLibrary, "Future album", wanted: true, artist);
        var driftedError = AddEntity(db, EntityKind.AudioTrack, "Errored without wanted flag", wanted: false, album);
        AddSource(db, album);
        AddIdentity(db, artist, "musicbrainz", "artist-1");
        AddIdentity(db, track, "musicbrainz", "track-1");

        var imported = AddAcquisition(db, album, AcquisitionStatus.Imported);
        var failed = AddAcquisition(db, track, AcquisitionStatus.Failed);
        var pending = AddAcquisition(db, futureAlbum, AcquisitionStatus.Pending);
        var driftedFailed = AddAcquisition(db, driftedError, AcquisitionStatus.Failed);
        var rootMonitor = AddEntityMonitor(db, artist, EntityKind.MusicArtist);
        var albumMonitor = AddAcquisitionMonitor(db, imported, EntityKind.AudioLibrary);
        var trackMonitor = AddAcquisitionMonitor(db, failed, EntityKind.AudioTrack);
        await db.SaveChangesAsync();
        var persistence = new EfEntityUnmonitorPersistence(db, new EfEntityHierarchyReader(db));

        var scope = await persistence.ResolveAsync(rootMonitor, CancellationToken.None);

        Assert.NotNull(scope);
        Assert.True(scope!.EntityIds.ToHashSet().SetEquals([artist, album, track, futureAlbum, driftedError]));
        Assert.Equal(new[] { imported, failed, pending, driftedFailed }.Order(), scope.AcquisitionIds.Order());
        Assert.Equal(new[] { rootMonitor, albumMonitor, trackMonitor }.Order(), scope.MonitorIds.Order());
        Assert.Equal(artist, scope.RootSuppression?.EntityId);
        Assert.Equal([new ExternalIdentity("musicbrainz", "artist-1")], scope.RootSuppression?.ExternalIdentities);

        Assert.True(await persistence.ClaimAsync(scope, CancellationToken.None));
        var claim = Assert.Single(await db.Monitors.ToArrayAsync());
        Assert.Equal(rootMonitor, claim.Id);
        Assert.Equal(artist, claim.EntityId);
        Assert.Equal(MonitorStatus.Stopping, claim.Status);

        db.Acquisitions.RemoveRange(await db.Acquisitions.Where(row => scope.AcquisitionIds.Contains(row.Id)).ToArrayAsync());
        await db.SaveChangesAsync();
        await persistence.CompleteAsync(scope, CancellationToken.None);

        Assert.Empty(await db.Monitors.ToArrayAsync());
        Assert.NotNull(await db.Entities.FindAsync(artist));
        Assert.False((await db.Entities.FindAsync(artist))!.IsWanted);
        Assert.NotNull(await db.Entities.FindAsync(album));
        Assert.NotNull(await db.EntityFiles.SingleOrDefaultAsync(row => row.EntityId == album));
        Assert.Null(await db.Entities.FindAsync(track));
        Assert.Null(await db.Entities.FindAsync(futureAlbum));
        Assert.Null(await db.Entities.FindAsync(driftedError));
    }

    [Fact]
    public async Task ChildOffLeavesTheAncestorMonitorAndSuppressesOnlyThatChildRoot() {
        await using var db = CreateContext();
        var series = AddEntity(db, EntityKind.VideoSeries, "Series", wanted: false);
        var season = AddEntity(db, EntityKind.VideoSeason, "Season 1", wanted: false, series);
        var episode = AddEntity(db, EntityKind.Video, "Wanted episode", wanted: true, season);
        AddSource(db, season);
        AddIdentity(db, series, "tmdb", "series-1");
        AddIdentity(db, season, "tmdbseason", "series-1:1");
        AddIdentity(db, episode, "tmdbepisode", "series-1:1:1");
        var ancestorMonitor = AddEntityMonitor(db, series, EntityKind.VideoSeries);
        var seasonAcquisition = AddAcquisition(db, season, AcquisitionStatus.Imported);
        var seasonMonitor = AddAcquisitionMonitor(db, seasonAcquisition, EntityKind.VideoSeason);
        await db.SaveChangesAsync();
        var persistence = new EfEntityUnmonitorPersistence(db, new EfEntityHierarchyReader(db));

        var scope = await persistence.ResolveAsync(seasonMonitor, CancellationToken.None);

        Assert.NotNull(scope);
        Assert.Equal(season, scope!.RootEntityId);
        Assert.Equal([season, episode], scope.EntityIds);
        Assert.DoesNotContain(ancestorMonitor, scope.MonitorIds);
        Assert.Equal(season, scope.RootSuppression?.EntityId);
        Assert.Equal([new ExternalIdentity("tmdbseason", "series-1:1")], scope.RootSuppression?.ExternalIdentities);
    }

    [Fact]
    public async Task ClaimPublishesTheChildSuppressionBeforeReturning() {
        await using var db = CreateContext();
        var artist = AddEntity(db, EntityKind.MusicArtist, "Artist", wanted: false);
        var album = AddEntity(db, EntityKind.AudioLibrary, "Removed album", wanted: true, artist);
        AddIdentity(db, album, "musicbrainz", "release-1");
        AddEntityMonitor(db, artist, EntityKind.MusicArtist);
        var albumMonitor = AddEntityMonitor(db, album, EntityKind.AudioLibrary);
        await db.SaveChangesAsync();
        var persistence = new EfEntityUnmonitorPersistence(db, new EfEntityHierarchyReader(db));
        var scope = (await persistence.ResolveAsync(albumMonitor, CancellationToken.None))!;

        Assert.True(await persistence.ClaimAsync(scope, CancellationToken.None));

        var suppression = await db.WantedSuppressions.AsNoTracking().SingleAsync();
        Assert.Equal("musicbrainz", suppression.Provider);
        Assert.Equal("release-1", suppression.ItemId);
        Assert.Equal(EntityKind.AudioLibrary, suppression.Kind);
    }

    [Fact]
    public async Task MonitorlessWantedEntityGetsARetryableSyntheticStoppingAnchor() {
        await using var db = CreateContext();
        var book = AddEntity(db, EntityKind.Book, "Removed book", wanted: true);
        AddIdentity(db, book, "openlibrary", "OL1W");
        await db.SaveChangesAsync();
        var persistence = new EfEntityUnmonitorPersistence(db, new EfEntityHierarchyReader(db));

        var scope = await persistence.ResolveForEntityAsync(book, CancellationToken.None);

        Assert.NotNull(scope);
        Assert.True(scope!.SyntheticMonitorAnchor);
        Assert.Contains(scope.MonitorId, scope.MonitorIds);
        Assert.True(await persistence.ClaimAsync(scope, CancellationToken.None));
        var anchor = await db.Monitors.AsNoTracking().SingleAsync();
        Assert.Equal(scope.MonitorId, anchor.Id);
        Assert.Equal(book, anchor.EntityId);
        Assert.Equal(MonitorStatus.Stopping, anchor.Status);
        Assert.Single(await db.WantedSuppressions.AsNoTracking().ToArrayAsync());

        // A remote teardown failure deliberately leaves the claim in place. The next attempt resolves the
        // same durable anchor instead of generating a second Active monitor or losing retry authority.
        var retry = await persistence.ResolveForEntityAsync(book, CancellationToken.None);
        Assert.NotNull(retry);
        Assert.False(retry!.SyntheticMonitorAnchor);
        Assert.Equal(scope.MonitorId, retry.MonitorId);

        await persistence.CompleteAsync(retry, CancellationToken.None);

        Assert.Null(await db.Entities.FindAsync(book));
        Assert.Empty(await db.Monitors.ToArrayAsync());
    }

    [Fact]
    public async Task SyntheticClaimConflictsWithExplicitIntentCreatedAfterResolution() {
        await using var db = CreateContext();
        var book = AddEntity(db, EntityKind.Book, "Wanted book", wanted: true);
        await db.SaveChangesAsync();
        var persistence = new EfEntityUnmonitorPersistence(db, new EfEntityHierarchyReader(db));
        var scope = (await persistence.ResolveForEntityAsync(book, CancellationToken.None))!;

        var explicitMonitor = AddEntityMonitor(db, book, EntityKind.Book);
        await db.SaveChangesAsync();

        Assert.False(await persistence.ClaimAsync(scope, CancellationToken.None));
        var remaining = await db.Monitors.AsNoTracking().SingleAsync();
        Assert.Equal(explicitMonitor, remaining.Id);
        Assert.Equal(MonitorStatus.Active, remaining.Status);
        Assert.Empty(await db.WantedSuppressions.ToArrayAsync());
    }

    [Fact]
    public async Task ClaimRejectsAnAcquisitionWhoseLifecycleChangedAfterPreflight() {
        await using var db = CreateContext();
        var book = AddEntity(db, EntityKind.Book, "Wanted book", wanted: true);
        AddIdentity(db, book, "openlibrary", "OL1W");
        var acquisitionId = AddAcquisition(db, book, AcquisitionStatus.Downloaded);
        var monitorId = AddAcquisitionMonitor(db, acquisitionId, EntityKind.Book);
        await db.SaveChangesAsync();
        var persistence = new EfEntityUnmonitorPersistence(db, new EfEntityHierarchyReader(db));
        var scope = (await persistence.ResolveAsync(monitorId, CancellationToken.None))!;
        Assert.Equal(AcquisitionStatus.Downloaded, scope.AcquisitionStatuses?[acquisitionId]);

        var acquisition = await db.Acquisitions.SingleAsync(row => row.Id == acquisitionId);
        acquisition.Status = AcquisitionStatus.Importing;
        await db.SaveChangesAsync();

        Assert.False(await persistence.ClaimAsync(scope, CancellationToken.None));
        Assert.Equal(MonitorStatus.Active, (await db.Monitors.FindAsync(monitorId))!.Status);
        Assert.Empty(await db.WantedSuppressions.ToArrayAsync());
    }

    [Fact]
    public async Task CompletionNeverSweepsAChildCreatedAfterTheImmutableScopeWasClaimed() {
        await using var db = CreateContext();
        var artist = AddEntity(db, EntityKind.MusicArtist, "Artist", wanted: true);
        AddIdentity(db, artist, "musicbrainz", "artist-1");
        var monitorId = AddEntityMonitor(db, artist, EntityKind.MusicArtist);
        await db.SaveChangesAsync();
        var persistence = new EfEntityUnmonitorPersistence(db, new EfEntityHierarchyReader(db));
        var scope = (await persistence.ResolveAsync(monitorId, CancellationToken.None))!;
        Assert.True(await persistence.ClaimAsync(scope, CancellationToken.None));

        var newAlbum = AddEntity(db, EntityKind.AudioLibrary, "New explicit album", wanted: true, artist);
        var newAcquisition = AddAcquisition(db, newAlbum, AcquisitionStatus.Pending);
        var newMonitor = AddAcquisitionMonitor(db, newAcquisition, EntityKind.AudioLibrary);
        await db.SaveChangesAsync();

        await persistence.CompleteAsync(scope, CancellationToken.None);

        Assert.NotNull(await db.Entities.FindAsync(artist));
        Assert.NotNull(await db.Entities.FindAsync(newAlbum));
        Assert.True((await db.Entities.FindAsync(artist))!.IsWanted);
        Assert.True((await db.Entities.FindAsync(newAlbum))!.IsWanted);
        Assert.NotNull(await db.Acquisitions.FindAsync(newAcquisition));
        Assert.NotNull(await db.Monitors.FindAsync(newMonitor));
    }

    [Fact]
    public async Task CompletionPreservesWantedClosureTargetedByANewEntityOnlyMonitorAfterClaim() {
        await using var db = CreateContext();
        var artist = AddEntity(db, EntityKind.MusicArtist, "Artist", wanted: true);
        var album = AddEntity(db, EntityKind.AudioLibrary, "Album", wanted: true, artist);
        var monitorId = AddEntityMonitor(db, artist, EntityKind.MusicArtist);
        await db.SaveChangesAsync();
        var persistence = new EfEntityUnmonitorPersistence(db, new EfEntityHierarchyReader(db));
        var scope = (await persistence.ResolveAsync(monitorId, CancellationToken.None))!;
        Assert.True(await persistence.ClaimAsync(scope, CancellationToken.None));

        var newMonitor = AddEntityMonitor(db, album, EntityKind.AudioLibrary);
        await db.SaveChangesAsync();

        await persistence.CompleteAsync(scope, CancellationToken.None);

        Assert.NotNull(await db.Entities.FindAsync(artist));
        Assert.NotNull(await db.Entities.FindAsync(album));
        Assert.True((await db.Entities.FindAsync(artist))!.IsWanted);
        Assert.True((await db.Entities.FindAsync(album))!.IsWanted);
        Assert.NotNull(await db.Monitors.FindAsync(newMonitor));
    }

    [Fact]
    public async Task ClaimAbsorbsAProviderPhantomCreatedAfterPreflight() {
        await using var db = CreateContext();
        var artist = AddEntity(db, EntityKind.MusicArtist, "Artist", wanted: true);
        AddSource(db, artist);
        var monitorId = AddEntityMonitor(db, artist, EntityKind.MusicArtist);
        await db.SaveChangesAsync();
        var persistence = new EfEntityUnmonitorPersistence(db, new EfEntityHierarchyReader(db));
        var scope = (await persistence.ResolveAsync(monitorId, CancellationToken.None))!;

        var providerPhantom = AddEntity(db, EntityKind.AudioLibrary, "Concurrent album", wanted: true, artist);
        await db.SaveChangesAsync();

        Assert.True(await persistence.ClaimAsync(scope, CancellationToken.None));
        await persistence.CompleteAsync(scope, CancellationToken.None);

        Assert.NotNull(await db.Entities.FindAsync(artist));
        Assert.Null(await db.Entities.FindAsync(providerPhantom));
        Assert.Empty(await db.Monitors.ToArrayAsync());
    }

    [Fact]
    public async Task CompletionPrunesAProviderPhantomCreatedAfterTheChildWasClaimedOff() {
        await using var db = CreateContext();
        var series = AddEntity(db, EntityKind.VideoSeries, "Series", wanted: false);
        var season = AddEntity(db, EntityKind.VideoSeason, "Season 1", wanted: false, series);
        AddSource(db, season);
        var ancestorMonitor = AddEntityMonitor(db, series, EntityKind.VideoSeries);
        var seasonMonitor = AddEntityMonitor(db, season, EntityKind.VideoSeason);
        await db.SaveChangesAsync();
        var persistence = new EfEntityUnmonitorPersistence(db, new EfEntityHierarchyReader(db));
        var scope = (await persistence.ResolveAsync(seasonMonitor, CancellationToken.None))!;
        Assert.True(await persistence.ClaimAsync(scope, CancellationToken.None));

        var lateEpisode = AddEntity(db, EntityKind.Video, "Late provider episode", wanted: true, season);
        await db.SaveChangesAsync();

        await persistence.CompleteAsync(scope, CancellationToken.None);

        Assert.NotNull(await db.Entities.FindAsync(series));
        Assert.NotNull(await db.Entities.FindAsync(season));
        Assert.Null(await db.Entities.FindAsync(lateEpisode));
        Assert.NotNull(await db.Monitors.FindAsync(ancestorMonitor));
        Assert.Null(await db.Monitors.FindAsync(seasonMonitor));
    }

    [Fact]
    public async Task ClaimRejectsANewUpgradeDescendantThatHasNoEntityLink() {
        await using var db = CreateContext();
        var book = AddEntity(db, EntityKind.Book, "Book", wanted: true);
        var acquisition = AddAcquisition(db, book, AcquisitionStatus.Imported);
        var monitorId = AddEntityMonitor(db, book, EntityKind.Book);
        await db.SaveChangesAsync();
        var persistence = new EfEntityUnmonitorPersistence(db, new EfEntityHierarchyReader(db));
        var scope = (await persistence.ResolveAsync(monitorId, CancellationToken.None))!;

        db.Acquisitions.Add(new AcquisitionRow {
            Id = Guid.NewGuid(),
            Kind = EntityKind.Book,
            EntityId = null,
            UpgradeOfAcquisitionId = acquisition,
            Status = AcquisitionStatus.Searching,
            Title = "Concurrent upgrade",
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.False(await persistence.ClaimAsync(scope, CancellationToken.None));
        Assert.Equal(MonitorStatus.Active, (await db.Monitors.SingleAsync()).Status);
    }

    [Fact]
    public async Task ExplicitRequestLeaseFirstMakesTheLaterUnmonitorClaimConflictWithoutOrphaningWork() {
        await using var db = CreateContext();
        var book = AddEntity(db, EntityKind.Book, "Book", wanted: true);
        var monitorId = AddEntityMonitor(db, book, EntityKind.Book);
        await db.SaveChangesAsync();
        var hierarchy = new EfEntityHierarchyReader(db);
        var persistence = new EfEntityUnmonitorPersistence(db, hierarchy);
        var scope = (await persistence.ResolveAsync(monitorId, CancellationToken.None))!;
        var monitors = new EfMonitorStore(db, hierarchy);
        Guid? acquisitionId = null;

        Assert.True(await monitors.ExecuteIfEntityLifecycleMutableAsync(
            book,
            async leaseCancellationToken => {
                acquisitionId = AddAcquisition(db, book, AcquisitionStatus.Pending);
                await db.SaveChangesAsync(leaseCancellationToken);
                await monitors.StartAsync(
                    acquisitionId.Value,
                    EntityKind.Book,
                    "Book",
                    author: null,
                    leaseCancellationToken);
            },
            CancellationToken.None));

        Assert.False(await persistence.ClaimAsync(scope, CancellationToken.None));
        Assert.NotNull(await db.Acquisitions.FindAsync(acquisitionId!.Value));
        var monitor = await db.Monitors.AsNoTracking().SingleAsync();
        Assert.Equal(monitorId, monitor.Id);
        Assert.Equal(MonitorStatus.Active, monitor.Status);
        Assert.Equal(acquisitionId, monitor.AcquisitionId);
    }

    [Fact]
    public async Task UnmonitorClaimDoesNotOverrideAFileDeletionClaimInItsSubtree() {
        await using var db = CreateContext();
        var artist = AddEntity(db, EntityKind.MusicArtist, "Artist", wanted: true);
        var album = AddEntity(db, EntityKind.AudioLibrary, "Album", wanted: true, artist);
        var rootMonitor = AddEntityMonitor(db, artist, EntityKind.MusicArtist);
        var childMonitor = AddEntityMonitor(db, album, EntityKind.AudioLibrary);
        await db.SaveChangesAsync();
        (await db.Monitors.SingleAsync(row => row.Id == childMonitor)).Status = MonitorStatus.DeletingFiles;
        await db.SaveChangesAsync();
        var persistence = new EfEntityUnmonitorPersistence(db, new EfEntityHierarchyReader(db));
        var scope = (await persistence.ResolveAsync(rootMonitor, CancellationToken.None))!;

        Assert.False(await persistence.ClaimAsync(scope, CancellationToken.None));

        var monitors = await db.Monitors.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync();
        Assert.Equal(2, monitors.Length);
        Assert.Contains(monitors, row => row.Id == rootMonitor && row.Status == MonitorStatus.Active);
        Assert.Contains(monitors, row => row.Id == childMonitor && row.Status == MonitorStatus.DeletingFiles);
    }

    [Fact]
    public async Task CompletionCleansAssetsOnlyForActuallyPrunedEntities() {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"prismedia-unmonitor-assets-{Guid.NewGuid():N}");
        try {
            await using var db = CreateContext();
            var artist = AddEntity(db, EntityKind.MusicArtist, "Artist", wanted: true);
            var ownedAlbum = AddEntity(db, EntityKind.AudioLibrary, "Owned album", wanted: false, artist);
            var prunedTrack = AddEntity(db, EntityKind.AudioTrack, "Wanted track", wanted: true, ownedAlbum);
            AddSource(db, ownedAlbum);
            var monitorId = AddEntityMonitor(db, artist, EntityKind.MusicArtist);

            var assets = new AssetPathService(tempRoot);
            var retainedGrid = assets.GridThumbnailPath(ownedAlbum);
            var prunedGrid = assets.GridThumbnailPath(prunedTrack);
            var retainedArtworkUrl = $"/assets/plugins/artwork/{ownedAlbum}/poster.jpg";
            var prunedArtworkUrl = $"/assets/plugins/artwork/{prunedTrack}/poster.jpg";
            WriteAsset(retainedGrid);
            WriteAsset(prunedGrid);
            WriteAsset(assets.ResolveAssetDiskPath(retainedArtworkUrl)!);
            WriteAsset(assets.ResolveAssetDiskPath(prunedArtworkUrl)!);
            db.EntityFiles.AddRange(
                NewAssetFile(ownedAlbum, retainedArtworkUrl),
                NewAssetFile(prunedTrack, prunedArtworkUrl));
            await db.SaveChangesAsync();

            var cleanup = new EntityAssetCleanupService(
                assets,
                NullLogger<EntityAssetCleanupService>.Instance);
            var persistence = new EfEntityUnmonitorPersistence(
                db,
                new EfEntityHierarchyReader(db),
                cleanup);
            var scope = (await persistence.ResolveAsync(monitorId, CancellationToken.None))!;
            Assert.True(await persistence.ClaimAsync(scope, CancellationToken.None));

            await persistence.CompleteAsync(scope, CancellationToken.None);

            Assert.NotNull(await db.Entities.FindAsync(ownedAlbum));
            Assert.Null(await db.Entities.FindAsync(prunedTrack));
            Assert.True(File.Exists(retainedGrid));
            Assert.True(File.Exists(assets.ResolveAssetDiskPath(retainedArtworkUrl)!));
            Assert.False(File.Exists(prunedGrid));
            Assert.False(File.Exists(assets.ResolveAssetDiskPath(prunedArtworkUrl)!));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static EntityFileRow NewAssetFile(Guid entityId, string path) => new() {
        Id = Guid.NewGuid(),
        EntityId = entityId,
        Role = EntityFileRole.Poster,
        Path = path,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static void WriteAsset(string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "asset");
    }

    private static Guid AddEntity(
        PrismediaDbContext db,
        EntityKind kind,
        string title,
        bool wanted,
        Guid? parent = null) {
        var id = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind.ToCode(),
            Title = title,
            IsWanted = wanted,
            ParentEntityId = parent,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private static void AddSource(PrismediaDbContext db, Guid entityId) =>
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Role = EntityFileRole.Source,
            Path = $"/media/{entityId}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

    private static void AddIdentity(PrismediaDbContext db, Guid entityId, string provider, string value) =>
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Provider = provider,
            Value = value,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

    private static Guid AddAcquisition(
        PrismediaDbContext db,
        Guid entityId,
        AcquisitionStatus status) {
        var id = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = id,
            Kind = EntityKind.Book,
            EntityId = entityId,
            Status = status,
            Title = id.ToString(),
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private static Guid AddEntityMonitor(PrismediaDbContext db, Guid entityId, EntityKind kind) {
        var id = Guid.NewGuid();
        db.Monitors.Add(new MonitorRow {
            Id = id,
            EntityId = entityId,
            Kind = kind,
            Status = MonitorStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private static Guid AddAcquisitionMonitor(PrismediaDbContext db, Guid acquisitionId, EntityKind kind) {
        var id = Guid.NewGuid();
        db.Monitors.Add(new MonitorRow {
            Id = id,
            AcquisitionId = acquisitionId,
            Kind = kind,
            Status = MonitorStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
