using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfMonitorStoreTests {
    [Fact]
    public async Task ParentDiscoveryLeaseRefusesMutationWhileADescendantIsBeingUnmonitored() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        db.Entities.AddRange(
            new EntityRow {
                Id = parentId,
                KindCode = EntityKind.MusicArtist.ToCode(),
                Title = "Artist",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = childId,
                ParentEntityId = parentId,
                KindCode = EntityKind.AudioLibrary.ToCode(),
                Title = "Album",
                CreatedAt = now,
                UpdatedAt = now
            });
        db.Monitors.AddRange(
            new MonitorRow {
                Id = Guid.NewGuid(),
                EntityId = parentId,
                Kind = EntityKind.MusicArtist,
                Status = MonitorStatus.Active,
                Title = "Artist",
                CreatedAt = now,
                UpdatedAt = now
            },
            new MonitorRow {
                Id = Guid.NewGuid(),
                EntityId = childId,
                Kind = EntityKind.AudioLibrary,
                Status = MonitorStatus.Stopping,
                Title = "Album",
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);
        var mutated = false;

        var leased = await store.ExecuteIfActiveEntityMutationAsync(
            parentId,
            _ => {
                mutated = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.False(leased);
        Assert.False(mutated);
    }

    [Fact]
    public async Task ExplicitChildIntentLeaseRefusesMutationWhileAnAncestorIsBeingUnmonitored() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        db.Entities.AddRange(
            new EntityRow {
                Id = parentId,
                KindCode = EntityKind.MusicArtist.ToCode(),
                Title = "Artist",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = childId,
                ParentEntityId = parentId,
                KindCode = EntityKind.AudioLibrary.ToCode(),
                Title = "Album",
                CreatedAt = now,
                UpdatedAt = now
            });
        db.Monitors.Add(new MonitorRow {
            Id = Guid.NewGuid(),
            EntityId = parentId,
            Kind = EntityKind.MusicArtist,
            Status = MonitorStatus.Stopping,
            Title = "Artist",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);
        var mutated = false;

        var leased = await store.ExecuteIfEntityLifecycleMutableAsync(
            childId,
            _ => {
                mutated = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.False(leased);
        Assert.False(mutated);
    }

    [Fact]
    public async Task ExplicitIntentLeaseRefusesMutationWhenCleanupAlreadyDeletedTheTargetEntity() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = EntityKind.Book.ToCode(),
            Title = "Wanted book",
            IsWanted = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        // Models cleanup winning after request proposal/pick resolution but before lifecycle entry.
        db.Entities.Remove((await db.Entities.SingleAsync(row => row.Id == entityId)));
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);
        var mutated = false;

        var leased = await store.ExecuteIfEntityLifecycleMutableAsync(
            entityId,
            _ => {
                mutated = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.False(leased);
        Assert.False(mutated);
    }

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
    public async Task StartReusesTheStableEntityMonitorAndAttachesTheAcquisition() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        var acquisitionId = SeedAcquisition(db, AcquisitionStatus.Failed, entityId);
        var stable = new MonitorRow {
            Id = Guid.NewGuid(), EntityId = entityId, Kind = EntityKind.Book,
            Status = MonitorStatus.Active, Title = "Book", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Monitors.Add(stable);
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);

        var monitor = await store.StartAsync(acquisitionId, EntityKind.Book, "Book", null, CancellationToken.None);

        Assert.Equal(stable.Id, monitor.Id);
        Assert.Equal(entityId, monitor.EntityId);
        Assert.Equal(acquisitionId, monitor.AcquisitionId);
        Assert.Single(await db.Monitors.ToArrayAsync());
    }

    [Fact]
    public async Task RetargetRefusesAStoppingClaimWithoutAttachingTheReplacement() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        var originalId = SeedAcquisition(db, AcquisitionStatus.Cancelled, entityId);
        var replacementId = SeedAcquisition(db, AcquisitionStatus.Pending, entityId);
        var monitorId = Guid.NewGuid();
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            EntityId = entityId,
            AcquisitionId = originalId,
            Kind = EntityKind.Book,
            Status = MonitorStatus.Stopping,
            Title = "Book",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);

        Assert.False(await store.RetargetAsync(originalId, replacementId, CancellationToken.None));

        var monitor = await db.Monitors.AsNoTracking().SingleAsync();
        Assert.Equal(MonitorStatus.Stopping, monitor.Status);
        Assert.Equal(originalId, monitor.AcquisitionId);
        Assert.Equal(entityId, monitor.EntityId);
    }

    [Fact]
    public async Task FileDeletionRetargetConsumesItsClaimAndRestoresActiveMonitoring() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        var originalId = SeedAcquisition(db, AcquisitionStatus.Stopping, entityId);
        var replacementId = SeedAcquisition(db, AcquisitionStatus.Pending, entityId);
        var monitorId = Guid.NewGuid();
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            EntityId = entityId,
            AcquisitionId = originalId,
            Kind = EntityKind.Book,
            Status = MonitorStatus.DeletingFiles,
            Title = "Book",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);

        Assert.False(await store.RetargetAsync(originalId, replacementId, CancellationToken.None));
        Assert.True(await store.RetargetAfterFileDeletionAsync(originalId, replacementId, CancellationToken.None));

        var monitor = await db.Monitors.AsNoTracking().SingleAsync();
        Assert.Equal(MonitorStatus.Active, monitor.Status);
        Assert.Equal(replacementId, monitor.AcquisitionId);
        Assert.Equal(entityId, monitor.EntityId);
    }

    [Theory]
    [InlineData(MonitorStatus.DeletingFiles)]
    [InlineData(MonitorStatus.Stopping)]
    public async Task DestructiveMonitorClaimsCannotBeResumedPausedOrDeleted(MonitorStatus claimedStatus) {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        var monitorId = Guid.NewGuid();
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            EntityId = entityId,
            Kind = EntityKind.Book,
            Status = claimedStatus,
            Title = "Book",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);

        await Assert.ThrowsAsync<Prismedia.Application.Acquisition.AcquisitionConfigurationException>(() =>
            store.StartForEntityAsync(
                entityId,
                EntityKind.Book,
                "Book",
                targeting: null,
                preset: null,
                CancellationToken.None));
        Assert.False(await store.SetStatusAsync(monitorId, MonitorStatus.Active, CancellationToken.None));
        Assert.False(await store.DeleteAsync(monitorId, CancellationToken.None));

        var monitor = await db.Monitors.AsNoTracking().SingleAsync();
        Assert.Equal(claimedStatus, monitor.Status);
    }

    [Fact]
    public async Task StaleUpgradeDueCannotCreateWorkAfterMonitorIsClaimedStopping() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        var acquisitionId = SeedAcquisition(db, AcquisitionStatus.Imported, entityId);
        var monitorId = Guid.NewGuid();
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            EntityId = entityId,
            AcquisitionId = acquisitionId,
            Kind = EntityKind.Book,
            Status = MonitorStatus.Stopping,
            Title = "Book",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);

        Assert.Null(await store.CreateUpgradeChildAsync(monitorId, CancellationToken.None));
        Assert.Single(await db.Acquisitions.ToArrayAsync());
        Assert.Null((await db.Monitors.AsNoTracking().SingleAsync()).UpgradeChildAcquisitionId);
    }

    [Fact]
    public async Task LegacyAcquisitionMonitorIsFoundAndBackfilledByEntityToggle() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        var acquisitionId = SeedAcquisition(db, AcquisitionStatus.Imported, entityId);
        var legacy = new MonitorRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, EntityId = null, Kind = EntityKind.Book,
            Status = MonitorStatus.Fulfilled, Title = "Book", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Monitors.Add(legacy);
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);

        Assert.Equal(legacy.Id, (await store.GetByEntityAsync(entityId, CancellationToken.None))?.Id);
        var restarted = await store.StartForEntityAsync(entityId, EntityKind.Book, "Book", null, null, CancellationToken.None);

        Assert.Equal(legacy.Id, restarted.Id);
        Assert.Equal(entityId, restarted.EntityId);
        Assert.Equal(acquisitionId, restarted.AcquisitionId);
        Assert.Equal(MonitorStatus.Active, restarted.Status);
        Assert.Single(await db.Monitors.ToArrayAsync());
    }

    [Fact]
    public async Task ImportedStableEntityMonitorDetachesAcquisitionAndStaysActive() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        var acquisitionId = SeedAcquisition(db, AcquisitionStatus.Imported, entityId);
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);
        await store.StartAsync(acquisitionId, EntityKind.Book, "Book", null, CancellationToken.None);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));

        var monitor = Assert.Single(await db.Monitors.AsNoTracking().ToArrayAsync());
        Assert.Equal(entityId, monitor.EntityId);
        Assert.Null(monitor.AcquisitionId);
        Assert.Equal(MonitorStatus.Active, monitor.Status);
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
    public async Task MonitorWhoseAcquisitionCancelledStaysActiveAndComesDue() {
        // Cancel stops the download, not the want: the monitor keeps chasing the item on its normal
        // cadence instead of pausing (monitoring is managed separately from download actions).
        await using var db = CreateContext();
        var store = await SeedMonitorAsync(db, AcquisitionStatus.Cancelled);

        Assert.Single(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Active, (await store.ListAsync(CancellationToken.None))[0].Status);
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
        var withPreset = await store.StartForEntityAsync(presetEntity, EntityKind.VideoSeries, "Series", targeting: null, preset: MonitorPreset.Missing, CancellationToken.None);

        Assert.Equal(MonitorPreset.All, withoutPreset.Preset);
        Assert.Equal(MonitorPreset.Missing, withPreset.Preset);
        Assert.Equal(MonitorPreset.All, await store.GetPresetByEntityAsync(defaultEntity, CancellationToken.None));
        Assert.Equal(MonitorPreset.Missing, await store.GetPresetByEntityAsync(presetEntity, CancellationToken.None));
    }

    [Fact]
    public async Task StartForEntityWithNullPresetNeverClobbersAStoredPreset() {
        // A discovery sync re-touches the container with preset: null, which must keep the recorded preset.
        await using var db = CreateContext();
        var store = new EfMonitorStore(db);
        var entityId = Guid.NewGuid();

        await store.StartForEntityAsync(entityId, EntityKind.VideoSeries, "Series", targeting: null, preset: MonitorPreset.None, CancellationToken.None);
        var synced = await store.StartForEntityAsync(entityId, EntityKind.VideoSeries, "Series", targeting: null, preset: null, CancellationToken.None);

        Assert.Equal(MonitorPreset.None, synced.Preset);
        Assert.Equal(MonitorPreset.None, await store.GetPresetByEntityAsync(entityId, CancellationToken.None));
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

    private static Guid SeedAcquisition(PrismediaDbContext db, AcquisitionStatus status, Guid? entityId = null) {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = id, EntityId = entityId, Status = status, Title = "Some Book", ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now
        });
        return id;
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
