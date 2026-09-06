using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfMonitorStoreTargetingRecoveryTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnselectedRequestWithLostTargetingRecoversItsDirectMonitorChoices(bool postgres) {
        await using var database = postgres ? await PostgresTestDatabase.CreateAsync() : null;
        await using var db = database?.CreateContext() ?? MemoryContext();
        var (monitor, acquisition) = await SeedAsync(db);
        var store = new EfMonitorStore(db);

        var due = Assert.Single(await store.ListDueMonitorsAsync(360, default));

        await db.Entry(acquisition).ReloadAsync();
        Assert.Equal(monitor.ProfileId, acquisition.ProfileId);
        Assert.Equal(monitor.TargetLibraryRootId, acquisition.TargetLibraryRootId);
        Assert.Equal(acquisition.Id, due.AcquisitionId);
        Assert.Equal(monitor.ProfileId, due.ProfileId);
        Assert.Null(monitor.LastSearchedAt);
        Assert.Single(await db.Acquisitions.ToArrayAsync());
        monitor.LastSearchedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        Assert.Empty(await store.ListDueMonitorsAsync(360, default));
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("selected")]
    [InlineData("checkpoint")]
    [InlineData("installed")]
    [InlineData("manual review")]
    [InlineData("transfer")]
    [InlineData("explicit profile")]
    [InlineData("explicit root")]
    [InlineData("searching")]
    [InlineData("importing")]
    [InlineData("owned")]
    [InlineData("stopping")]
    [InlineData("deleted profile")]
    [InlineData("wrong profile kind")]
    [InlineData("deleted root")]
    public async Task RecoveryPreservesExistingAuthorityAndWork(string scenario) {
        await using var db = MemoryContext();
        var (monitor, acquisition) = await SeedAsync(db);
        if (scenario == "deleted profile") monitor.ProfileId = Guid.NewGuid();
        if (scenario == "wrong profile kind") (await db.BookAcquisitionProfiles.SingleAsync()).Kind = EntityKind.Book;
        if (scenario == "deleted root") monitor.TargetLibraryRootId = Guid.NewGuid();
        if (scenario == "paused") monitor.Status = MonitorStatus.Paused;
        if (scenario == "selected") acquisition.SelectedReleaseJson = "{}";
        if (scenario == "checkpoint") acquisition.ImportCheckpointJson = "{}";
        if (scenario == "installed") acquisition.FinalSourcePath = "/library/owned.mkv";
        if (scenario == "manual review") acquisition.ImportManualReview = true;
        if (scenario == "explicit profile") acquisition.ProfileId = Guid.NewGuid();
        if (scenario == "explicit root") acquisition.TargetLibraryRootId = Guid.NewGuid();
        if (scenario == "searching") acquisition.Status = AcquisitionStatus.Searching;
        if (scenario == "importing") acquisition.Status = AcquisitionStatus.Importing;
        if (scenario == "stopping") monitor.Status = MonitorStatus.Stopping;
        if (scenario == "transfer") db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisition.Id, ClientItemId = "current-download"
        });
        if (scenario == "owned") db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(), EntityId = monitor.EntityId!.Value, Role = EntityFileRole.Source, Path = "/library/owned.mkv"
        });
        await db.SaveChangesAsync();
        var profile = acquisition.ProfileId;
        var root = acquisition.TargetLibraryRootId;
        var searched = monitor.LastSearchedAt;

        await new EfMonitorStore(db).ListDueMonitorsAsync(360, default);

        Assert.Equal(profile, acquisition.ProfileId);
        Assert.Equal(root, acquisition.TargetLibraryRootId);
        Assert.Equal(searched, monitor.LastSearchedAt);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RechecksAuthorityAfterTheCandidateObservation(bool postgres, bool pause) {
        await using var database = postgres ? await PostgresTestDatabase.CreateAsync() : null;
        await using var db = database?.CreateContext() ?? MemoryContext();
        var (monitor, acquisition) = await SeedAsync(db);
        var changed = false;
        var lease = new ObservedMutationLease(new EfEntityLifecycleMutationLease(db, new EfEntityHierarchyReader(db)), async token => {
            changed = true;
            if (database is not null) {
                await using var other = database.CreateContext();
                if (pause) await other.Monitors.Where(row => row.Id == monitor.Id).ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.Status, MonitorStatus.Paused), token);
                else await other.Acquisitions.Where(row => row.Id == acquisition.Id).ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.SelectedReleaseJson, "{}"), token);
            } else {
                if (pause) monitor.Status = MonitorStatus.Paused;
                else acquisition.SelectedReleaseJson = "{}";
                await db.SaveChangesAsync(token);
            }
        });

        await new EfMonitorStore(db, lifecycleLease: lease).ListDueMonitorsAsync(360, default);

        Assert.True(changed);
        await db.Entry(acquisition).ReloadAsync();
        Assert.Null(acquisition.ProfileId);
        Assert.Null(acquisition.TargetLibraryRootId);
        Assert.NotNull(monitor.LastSearchedAt);
    }

    private sealed class ObservedMutationLease(IEntityLifecycleMutationLease inner, Func<CancellationToken, Task> before)
        : IEntityLifecycleMutationLease {
        public async Task<bool> ExecuteAsync(Guid entityId, Func<CancellationToken, Task> mutation, CancellationToken cancellationToken) {
            await before(cancellationToken);
            return await inner.ExecuteAsync(entityId, mutation, cancellationToken);
        }
    }

    private static async Task<(MonitorRow Monitor, AcquisitionRow Acquisition)> SeedAsync(PrismediaDbContext db) {
        var entity = new EntityRow { Id = Guid.NewGuid(), KindCode = EntityKind.Movie.ToCode(), Title = "Wanted movie", IsWanted = true };
        var root = new LibraryRootRow { Id = Guid.NewGuid(), Path = "/library", Label = "Library", ScanVideos = true };
        var profile = new BookAcquisitionProfileRow { Id = Guid.NewGuid(), Kind = EntityKind.Movie,
            DisplayName = "Explicit quality", TargetLibraryRootId = root.Id, AutoPick = true };
        db.Entities.Add(entity);
        db.LibraryRoots.Add(root);
        db.BookAcquisitionProfiles.Add(profile);
        await db.SaveChangesAsync();
        var acquisition = new AcquisitionRow { Id = Guid.NewGuid(), EntityId = entity.Id, Kind = EntityKind.Movie,
            Title = entity.Title, Status = AcquisitionStatus.AwaitingSelection, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Acquisitions.Add(acquisition);
        await db.SaveChangesAsync();
        var monitor = new MonitorRow { Id = Guid.NewGuid(), EntityId = entity.Id, AcquisitionId = acquisition.Id,
            Kind = EntityKind.Movie, Title = entity.Title, Status = MonitorStatus.Active, ProfileId = profile.Id,
            TargetLibraryRootId = root.Id, LastSearchedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Monitors.Add(monitor);
        await db.SaveChangesAsync();
        return (monitor, acquisition);
    }

    private static PrismediaDbContext MemoryContext() => new(new DbContextOptionsBuilder<PrismediaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
