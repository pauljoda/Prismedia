using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfMonitorStoreTests {
    [Fact]
    public async Task StartCreatesAnActiveMonitorForTheAcquisition() {
        await using var db = CreateContext();
        var acquisitionId = SeedAcquisition(db, AcquisitionStatus.Failed);
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);

        var view = await store.StartAsync(acquisitionId, EntityKind.Book, "The Hobbit", "Tolkien", CancellationToken.None);

        Assert.Equal(MonitorStatus.Active, view.Status);
        Assert.Equal("The Hobbit", view.Title);
        Assert.Equal(acquisitionId, view.AcquisitionId);
        Assert.Equal(AcquisitionStatus.Failed, view.AcquisitionStatus);
    }

    [Fact]
    public async Task StartIsIdempotentAndReactivatesAnExistingMonitor() {
        await using var db = CreateContext();
        var acquisitionId = SeedAcquisition(db, AcquisitionStatus.Failed);
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);

        var first = await store.StartAsync(acquisitionId, EntityKind.Book, "T", null, CancellationToken.None);
        await store.SetStatusAsync(first.Id, MonitorStatus.Paused, CancellationToken.None);
        var second = await store.StartAsync(acquisitionId, EntityKind.Book, "T", null, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(MonitorStatus.Active, second.Status); // re-activated
        Assert.Single(await store.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task NeverSearchedActiveMonitorIsDue() {
        await using var db = CreateContext();
        var store = await SeedMonitorAsync(db, AcquisitionStatus.Failed);

        var due = await store.ListDueMonitorsAsync(360, CancellationToken.None);

        Assert.Single(due);
    }

    [Fact]
    public async Task RecentlySearchedMonitorIsNotDueUntilTheIntervalElapses() {
        await using var db = CreateContext();
        var store = await SeedMonitorAsync(db, AcquisitionStatus.Failed);
        var monitorId = (await store.ListAsync(CancellationToken.None))[0].Id;
        await store.MarkSearchedAsync(monitorId, CancellationToken.None);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
    }

    [Fact]
    public async Task InFlightAcquisitionIsNotReSearchedAndStaysActive() {
        // The blocker fix: a downloading item must NOT be re-searched (that would reset its status and,
        // with auto-pick, delete the live torrent). It stays Active and absent from the due set.
        await using var db = CreateContext();
        var store = await SeedMonitorAsync(db, AcquisitionStatus.Downloading);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Active, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task AwaitingSelectionWithNoAcceptedCandidateIsDue() {
        // Search ran but found nothing acceptable — still missing, so keep looking.
        await using var db = CreateContext();
        var store = await SeedMonitorAsync(db, AcquisitionStatus.AwaitingSelection);

        Assert.Single(await store.ListDueMonitorsAsync(360, CancellationToken.None));
    }

    [Fact]
    public async Task AwaitingSelectionWithAnAcceptedCandidateIsNotDue() {
        // A release was found and is awaiting the user's pick (or auto-grab) — do not churn the candidate list.
        await using var db = CreateContext();
        var store = await SeedMonitorAsync(db, AcquisitionStatus.AwaitingSelection);
        var acquisitionId = (await store.ListAsync(CancellationToken.None))[0].AcquisitionId!.Value;
        db.ReleaseCandidates.Add(new ReleaseCandidateRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, IndexerName = "i", Title = "t",
            Accepted = true, Score = 1, Protocol = DownloadProtocol.Torrent, RejectionsJson = "[]", CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Active, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task SearchedBeyondTheIntervalIsDueAgain() {
        await using var db = CreateContext();
        var store = await SeedMonitorAsync(db, AcquisitionStatus.Failed);
        var monitor = await db.Monitors.FirstAsync();
        monitor.LastSearchedAt = DateTimeOffset.UtcNow.AddMinutes(-400); // older than the 360-minute interval
        await db.SaveChangesAsync();

        Assert.Single(await store.ListDueMonitorsAsync(360, CancellationToken.None));
    }

    [Fact]
    public async Task PausedMonitorIsNeverReturnedAndStaysPaused() {
        await using var db = CreateContext();
        var store = await SeedMonitorAsync(db, AcquisitionStatus.Failed);
        var monitorId = (await store.ListAsync(CancellationToken.None))[0].Id;
        await store.SetStatusAsync(monitorId, MonitorStatus.Paused, CancellationToken.None);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Paused, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task MonitorWhoseAcquisitionImportedIsFulfilledAndNotDue() {
        await using var db = CreateContext();
        var store = await SeedMonitorAsync(db, AcquisitionStatus.Imported);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task MonitorWhoseAcquisitionCancelledIsPaused() {
        await using var db = CreateContext();
        var store = await SeedMonitorAsync(db, AcquisitionStatus.Cancelled);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Paused, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task OrphanedMonitorIsPaused() {
        // Simulate the acquisition being hard-deleted: the FK is set null.
        await using var db = CreateContext();
        var store = await SeedMonitorAsync(db, AcquisitionStatus.Failed);
        var row = await db.Monitors.FirstAsync();
        row.AcquisitionId = null;
        await db.SaveChangesAsync();

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Paused, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task DeleteRemovesTheMonitorButLeavesTheAcquisition() {
        await using var db = CreateContext();
        var store = await SeedMonitorAsync(db, AcquisitionStatus.Failed);
        var monitorId = (await store.ListAsync(CancellationToken.None))[0].Id;
        var acquisitionId = (await store.ListAsync(CancellationToken.None))[0].AcquisitionId;

        Assert.True(await store.DeleteAsync(monitorId, CancellationToken.None));
        Assert.Empty(await store.ListAsync(CancellationToken.None));
        Assert.True(await db.Acquisitions.AnyAsync(a => a.Id == acquisitionId)); // acquisition untouched
    }

    [Fact]
    public async Task StartForEntityDefaultsToTheAllPresetAndRecordsAnExplicitOne() {
        await using var db = CreateContext();
        var store = new EfMonitorStore(db);
        var defaultEntity = Guid.NewGuid();
        var presetEntity = Guid.NewGuid();

        // No preset passed: the container monitor keeps the All default (the pre-preset "mirror" behavior).
        var withoutPreset = await store.StartForEntityAsync(defaultEntity, EntityKind.BookAuthor, "Author", targeting: null, preset: null, CancellationToken.None);
        // An explicit request records its chosen preset on the row.
        var withPreset = await store.StartForEntityAsync(presetEntity, EntityKind.VideoSeries, "Series", targeting: null, preset: MonitorPreset.FirstSeason, CancellationToken.None);

        Assert.Equal(MonitorPreset.All, withoutPreset.Preset);
        Assert.Equal(MonitorPreset.FirstSeason, withPreset.Preset);
        Assert.Equal(MonitorPreset.All, await store.GetPresetByEntityAsync(defaultEntity, CancellationToken.None));
        Assert.Equal(MonitorPreset.FirstSeason, await store.GetPresetByEntityAsync(presetEntity, CancellationToken.None));
    }

    [Fact]
    public async Task StartForEntityWithNullPresetNeverClobbersAStoredPreset() {
        // A discovery sync re-touches the container with preset: null, which must keep the recorded preset.
        await using var db = CreateContext();
        var store = new EfMonitorStore(db);
        var entityId = Guid.NewGuid();

        await store.StartForEntityAsync(entityId, EntityKind.VideoSeries, "Series", targeting: null, preset: MonitorPreset.LatestSeason, CancellationToken.None);
        var synced = await store.StartForEntityAsync(entityId, EntityKind.VideoSeries, "Series", targeting: null, preset: null, CancellationToken.None);

        Assert.Equal(MonitorPreset.LatestSeason, synced.Preset);
        Assert.Equal(MonitorPreset.LatestSeason, await store.GetPresetByEntityAsync(entityId, CancellationToken.None));
    }

    [Fact]
    public async Task GetPresetByEntityReturnsNullForAnUnmonitoredEntity() {
        await using var db = CreateContext();
        var store = new EfMonitorStore(db);

        Assert.Null(await store.GetPresetByEntityAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private static async Task<EfMonitorStore> SeedMonitorAsync(PrismediaDbContext db, AcquisitionStatus acquisitionStatus) {
        var acquisitionId = SeedAcquisition(db, acquisitionStatus);
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);
        await store.StartAsync(acquisitionId, EntityKind.Book, "Some Book", "Author", CancellationToken.None);
        return store;
    }

    private static Guid SeedAcquisition(PrismediaDbContext db, AcquisitionStatus status) {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = id, Status = status, Title = "Some Book", ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now
        });
        return id;
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
