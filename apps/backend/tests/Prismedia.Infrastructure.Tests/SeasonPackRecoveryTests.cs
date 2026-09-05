using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Pins bounded pack retries and serialization with missing episode searches.</summary>
public sealed class SeasonPackRecoveryTests {
    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompetingPackAndEpisodeClaimsHaveOneWinnerInPostgres(bool packFirst) {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Guid monitorId, childMonitorId, episodeId;
        await using (var setup = database.CreateContext()) {
            var (_, monitor, _, episodes) = await SeedAsync(setup);
            monitorId = monitor.Id;
            episodeId = episodes[0].Id;
            childMonitorId = await setup.Monitors.Where(row => row.AcquisitionId == episodeId)
                .Select(row => row.Id).SingleAsync();
        }
        await using var firstDb = database.CreateContext();
        await using var secondDb = database.CreateContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new EfMonitorStore(firstDb, lifecycleLease: new GatedLease(
            new EfEntityLifecycleMutationLease(firstDb, new EfEntityHierarchyReader(firstDb)), entered, release));
        var second = new EfMonitorStore(secondDb);
        Task<Guid?> pack;
        Task<bool> episode;
        if (packFirst) {
            pack = first.CreateSeasonPackRetryAsync(monitorId, default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            episode = second.TryStartEpisodeSearchAsync(childMonitorId, episodeId, AcquisitionTestFactory.Store(secondDb), default);
        } else {
            episode = first.TryStartEpisodeSearchAsync(childMonitorId, episodeId, AcquisitionTestFactory.Store(firstDb), default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            pack = second.CreateSeasonPackRetryAsync(monitorId, default);
        }
        var competing = packFirst ? (Task)episode : pack;
        try {
            Assert.NotSame(competing, await Task.WhenAny(competing, Task.Delay(200)));
        } finally {
            release.TrySetResult();
        }
        var packId = await pack.WaitAsync(TimeSpan.FromSeconds(10));
        var episodeStarted = await episode.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(packFirst, packId is not null);
        Assert.Equal(!packFirst, episodeStarted);
        await using var verification = database.CreateContext();
        if (packFirst) {
            Assert.Empty(await new EfMonitorStore(verification).ListImmediateForMonitorAsync(childMonitorId, default));
        }
        Assert.Equal(packFirst ? AcquisitionStatus.Failed : AcquisitionStatus.Searching,
            (await verification.Acquisitions.SingleAsync(row => row.Id == episodeId)).Status);
    }

    [Theory]
    [InlineData(AcquisitionStatus.Imported)]
    [InlineData(AcquisitionStatus.Failed)]
    [InlineData(AcquisitionStatus.AwaitingSelection)]
    public async Task ExhaustedEpisodesStartOneFreshPackWithoutRewritingPriorImport(AcquisitionStatus status) {
        await using var db = Context();
        var (store, monitor, parent, episodes) = await SeedAsync(db);
        parent.Status = status;
        parent.FinalSourcePath = "/library/retained";
        await db.SaveChangesAsync();

        var retryId = await store.CreateSeasonPackRetryAsync(monitor.Id, default);

        Assert.NotNull(retryId);
        var retry = await db.Acquisitions.SingleAsync(row => row.Id == retryId);
        Assert.Equal(AcquisitionStatus.Searching, retry.Status);
        Assert.Equal(parent.EntityId, retry.EntityId);
        Assert.Equal(parent.ProfileId, retry.ProfileId);
        Assert.Equal(parent.TargetLibraryRootId, retry.TargetLibraryRootId);
        Assert.Null(retry.UpgradeOfAcquisitionId);
        Assert.Null(retry.FinalSourcePath);
        Assert.Equal(status, parent.Status);
        Assert.Equal("/library/retained", parent.FinalSourcePath);
        Assert.Equal(retryId, monitor.AcquisitionId);
        Assert.All(episodes, episode => Assert.Equal(AcquisitionStatus.Failed, episode.Status));
        Assert.Null(await store.CreateSeasonPackRetryAsync(monitor.Id, default));
    }

    [Fact]
    public async Task DetachedSeasonIntentUsesItsLastAttemptAndCurrentTargeting() {
        await using var db = Context();
        var (store, monitor, _, _) = await SeedAsync(db);
        monitor.AcquisitionId = null;
        monitor.ProfileId = Guid.NewGuid();
        monitor.TargetLibraryRootId = Guid.NewGuid();
        await db.SaveChangesAsync();

        var id = await store.CreateSeasonPackRetryAsync(monitor.Id, default);

        var retry = await db.Acquisitions.SingleAsync(row => row.Id == id);
        Assert.Equal(monitor.ProfileId, retry.ProfileId);
        Assert.Equal(monitor.TargetLibraryRootId, retry.TargetLibraryRootId);
    }

    [Theory]
    [InlineData(AcquisitionStatus.Pending)]
    [InlineData(AcquisitionStatus.Searching)]
    [InlineData(AcquisitionStatus.Queued)]
    [InlineData(AcquisitionStatus.Downloading)]
    [InlineData(AcquisitionStatus.Downloaded)]
    [InlineData(AcquisitionStatus.Importing)]
    [InlineData(AcquisitionStatus.ManualImportRequired)]
    [InlineData(AcquisitionStatus.ManualSearchRequired)]
    [InlineData(AcquisitionStatus.Cancelled)]
    [InlineData(AcquisitionStatus.WaitingForRelease)]
    public async Task UnexhaustedEpisodePreventsPackRetry(AcquisitionStatus status) {
        await using var db = Context();
        var (store, monitor, _, episodes) = await SeedAsync(db);
        episodes[0].Status = status;
        await db.SaveChangesAsync();

        Assert.Null(await store.CreateSeasonPackRetryAsync(monitor.Id, default));
    }

    [Fact]
    public async Task BarrenEpisodeAllowsRetryButAnAcceptedReviewDoesNot() {
        await using var db = Context();
        var (store, monitor, _, episodes) = await SeedAsync(db);
        episodes[0].Status = AcquisitionStatus.AwaitingSelection;
        db.ReleaseCandidates.Add(new ReleaseCandidateRow {
            Id = Guid.NewGuid(), AcquisitionId = episodes[0].Id, Accepted = true,
            Title = "Series S01E01", IndexerName = "Indexer", Protocol = DownloadProtocol.Usenet
        });
        await db.SaveChangesAsync();
        Assert.Null(await store.CreateSeasonPackRetryAsync(monitor.Id, default));

        db.ReleaseCandidates.RemoveRange(db.ReleaseCandidates);
        await db.SaveChangesAsync();
        Assert.NotNull(await store.CreateSeasonPackRetryAsync(monitor.Id, default));
    }

    [Fact]
    public async Task RecentPackAndEpisodesThatNeverSearchedSinceItDoNotRetry() {
        await using var db = Context();
        var (store, monitor, parent, episodes) = await SeedAsync(db);
        parent.CreatedAt = DateTimeOffset.UtcNow.AddHours(-1);
        await db.SaveChangesAsync();
        Assert.Null(await store.CreateSeasonPackRetryAsync(monitor.Id, default));

        parent.CreatedAt = DateTimeOffset.UtcNow.AddHours(-7);
        episodes[0].UpdatedAt = parent.CreatedAt.AddMinutes(-1);
        await db.SaveChangesAsync();
        Assert.Null(await store.CreateSeasonPackRetryAsync(monitor.Id, default));
    }

    [Theory]
    [InlineData(MonitorStatus.Paused)]
    [InlineData(MonitorStatus.Stopping)]
    [InlineData(MonitorStatus.DeletingFiles)]
    public async Task InactiveChildIntentPreventsRetry(MonitorStatus status) {
        await using var db = Context();
        var (store, monitor, _, episodes) = await SeedAsync(db);
        (await db.Monitors.SingleAsync(row => row.AcquisitionId == episodes[0].Id)).Status = status;
        await db.SaveChangesAsync();
        Assert.Null(await store.CreateSeasonPackRetryAsync(monitor.Id, default));
    }

    [Fact]
    public async Task HeldPackUnfinishedReconcileAndCompletedCoverageNeverRetry() {
        await using var db = Context();
        var (store, monitor, parent, _) = await SeedAsync(db);
        parent.Status = AcquisitionStatus.ManualImportRequired;
        await db.SaveChangesAsync();
        Assert.Null(await store.CreateSeasonPackRetryAsync(monitor.Id, default));

        parent.Status = AcquisitionStatus.Imported;
        db.AcquisitionImportHints.Add(new AcquisitionImportHintRow {
            Id = Guid.NewGuid(), AcquisitionId = parent.Id, EntityId = parent.EntityId, Consumed = false
        });
        await db.SaveChangesAsync();
        Assert.Null(await store.CreateSeasonPackRetryAsync(monitor.Id, default));

        db.AcquisitionImportHints.RemoveRange(db.AcquisitionImportHints);
        foreach (var episode in await db.Entities.Where(row => row.ParentEntityId == parent.EntityId).ToArrayAsync()) {
            episode.IsWanted = false;
        }
        await db.SaveChangesAsync();
        Assert.Null(await store.CreateSeasonPackRetryAsync(monitor.Id, default));
    }

    [Fact]
    public async Task AWorkingPackSuppressesEpisodeDuesAndAStaleEpisodeSearchClaim() {
        await using var db = Context();
        var (store, monitor, _, episodes) = await SeedAsync(db);
        var childMonitor = await db.Monitors.SingleAsync(row => row.AcquisitionId == episodes[0].Id);
        Assert.NotNull(await store.CreateSeasonPackRetryAsync(monitor.Id, default));

        Assert.Empty(await store.ListImmediateForMonitorAsync(childMonitor.Id, default));
        Assert.False(await store.TryStartEpisodeSearchAsync(
            childMonitor.Id, episodes[0].Id, AcquisitionTestFactory.Store(db), default));
        Assert.Equal(AcquisitionStatus.Failed, episodes[0].Status);
    }

    [Fact]
    public async Task AnEpisodeSearchClaimWinsBeforePackRecovery() {
        await using var db = Context();
        var (store, monitor, _, episodes) = await SeedAsync(db);
        var childMonitor = await db.Monitors.SingleAsync(row => row.AcquisitionId == episodes[0].Id);

        Assert.True(await store.TryStartEpisodeSearchAsync(
            childMonitor.Id, episodes[0].Id, AcquisitionTestFactory.Store(db), default));
        Assert.Null(await store.CreateSeasonPackRetryAsync(monitor.Id, default));
    }

    private static async Task<(EfMonitorStore Store, MonitorRow Monitor, AcquisitionRow Parent, AcquisitionRow[] Episodes)> SeedAsync(PrismediaDbContext db) {
        var now = DateTimeOffset.UtcNow;
        var season = new EntityRow { Id = Guid.NewGuid(), KindCode = EntityKind.VideoSeason.ToCode(), Title = "Season 1" };
        db.Entities.Add(season);
        var parent = new AcquisitionRow {
            Id = Guid.NewGuid(), EntityId = season.Id, Kind = EntityKind.VideoSeason,
            Title = "Series S01", Series = "Series", SeasonNumber = 1, Status = AcquisitionStatus.Imported,
            ProfileId = Guid.NewGuid(), TargetLibraryRootId = Guid.NewGuid(),
            CreatedAt = now.AddHours(-7), UpdatedAt = now.AddHours(-6)
        };
        db.LibraryRoots.Add(new LibraryRootRow { Id = parent.TargetLibraryRootId!.Value, Path = "/media/tv", Label = "TV" });
        db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow {
            Id = parent.ProfileId!.Value, Kind = EntityKind.VideoSeries, DisplayName = "TV",
            TargetLibraryRootId = parent.TargetLibraryRootId.Value
        });
        db.Acquisitions.Add(parent);
        var monitor = new MonitorRow {
            Id = Guid.NewGuid(), EntityId = season.Id, AcquisitionId = parent.Id,
            Kind = EntityKind.VideoSeason, Title = parent.Title, Status = MonitorStatus.Active
        };
        db.Monitors.Add(monitor);
        var episodes = Enumerable.Range(1, 2).Select(number => {
            var entity = new EntityRow {
                Id = Guid.NewGuid(), ParentEntityId = season.Id, KindCode = EntityKind.VideoEpisode.ToCode(),
                Title = $"Episode {number}", SortOrder = number, IsWanted = true
            };
            db.Entities.Add(entity);
            var acquisition = new AcquisitionRow {
                Id = Guid.NewGuid(), EntityId = entity.Id, Kind = EntityKind.VideoEpisode,
                Status = AcquisitionStatus.Failed, Title = entity.Title,
                CreatedAt = now.AddHours(-5), UpdatedAt = now.AddHours(-1)
            };
            db.Monitors.Add(new MonitorRow {
                Id = Guid.NewGuid(), EntityId = entity.Id, AcquisitionId = acquisition.Id,
                Kind = EntityKind.VideoEpisode, Title = entity.Title, Status = MonitorStatus.Active
            });
            return acquisition;
        }).ToArray();
        db.Acquisitions.AddRange(episodes);
        await db.SaveChangesAsync();
        return (new EfMonitorStore(db), monitor, parent, episodes);
    }

    private static PrismediaDbContext Context() => new(new DbContextOptionsBuilder<PrismediaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class GatedLease(IEntityLifecycleMutationLease inner, TaskCompletionSource entered, TaskCompletionSource release)
        : IEntityLifecycleMutationLease {
        public Task<bool> ExecuteAsync(Guid entityId, Func<CancellationToken, Task> mutation, CancellationToken cancellationToken) =>
            inner.ExecuteAsync(entityId, async token => {
                entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                await mutation(token);
            }, cancellationToken);
    }
}
