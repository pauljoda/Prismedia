using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EntityLifecycleRecoveryStoreTests {
    [Fact]
    public async Task ListsClaimedOrOrphanedLifecycleWorkAndCompletesOnlySafeOrphans() {
        await using var db = new PrismediaDbContext(
            new DbContextOptionsBuilder<PrismediaDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var now = DateTimeOffset.UtcNow;
        var deletingEntityId = Guid.NewGuid();
        var orphanedDeletingMonitorId = Guid.NewGuid();
        var stoppingMonitorId = Guid.NewGuid();
        var ownedDeletingMonitorId = Guid.NewGuid();
        var orphanedStoppingAcquisitionId = Guid.NewGuid();
        var reacquireAcquisitionId = Guid.NewGuid();
        var ownedStoppingAcquisitionId = Guid.NewGuid();
        var ownedEntityId = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = deletingEntityId,
            KindCode = EntityKind.Movie.ToCode(),
            Title = "Claimed movie",
            LifecycleClaimKind = EntityLifecycleClaimKind.DeletingFiles,
            LifecycleClaimId = Guid.NewGuid(),
            LifecycleClaimedAt = now.AddHours(-2),
            CreatedAt = now.AddDays(-1),
            UpdatedAt = now.AddHours(-2)
        });
        db.Entities.Add(new EntityRow {
            Id = ownedEntityId,
            KindCode = EntityKind.AudioTrack.ToCode(),
            Title = "Owned track",
            CreatedAt = now.AddDays(-1),
            UpdatedAt = now
        });
        db.Acquisitions.AddRange(
            NewAcquisition(orphanedStoppingAcquisitionId, Guid.NewGuid(), AcquisitionTeardownIntent.Remove, now.AddMinutes(-3)),
            NewAcquisition(reacquireAcquisitionId, Guid.NewGuid(), AcquisitionTeardownIntent.Reacquire, now.AddMinutes(-2)),
            NewAcquisition(ownedStoppingAcquisitionId, ownedEntityId, AcquisitionTeardownIntent.Remove, now.AddMinutes(-1)));
        db.Monitors.AddRange(
            NewMonitor(orphanedDeletingMonitorId, MonitorStatus.DeletingFiles, Guid.NewGuid(), now.AddHours(-1)),
            NewMonitor(stoppingMonitorId, MonitorStatus.Stopping, Guid.NewGuid(), now),
            NewMonitor(ownedDeletingMonitorId, MonitorStatus.DeletingFiles, deletingEntityId, now));
        await db.SaveChangesAsync();
        var store = new EfEntityFileDeletionRecoveryProjection(db);

        var batch = await store.ListAsync(10, CancellationToken.None);

        Assert.Equal([deletingEntityId], batch.DeletingEntityIds);
        Assert.Equal([orphanedDeletingMonitorId], batch.OrphanedDeletingMonitorIds);
        Assert.Equal([stoppingMonitorId], batch.StoppingMonitorIds);
        Assert.Equal([orphanedStoppingAcquisitionId], batch.OrphanedStoppingAcquisitionIds);
        Assert.True(await store.CompleteOrphanedDeletionAsync(
            orphanedDeletingMonitorId,
            CancellationToken.None));
        Assert.False(await store.CompleteOrphanedDeletionAsync(
            ownedDeletingMonitorId,
            CancellationToken.None));
        Assert.Null(await db.Monitors.FindAsync(orphanedDeletingMonitorId));
        Assert.NotNull(await db.Monitors.FindAsync(ownedDeletingMonitorId));
    }

    private static AcquisitionRow NewAcquisition(
        Guid id,
        Guid entityId,
        AcquisitionTeardownIntent intent,
        DateTimeOffset updatedAt) =>
        new() {
            Id = id,
            Kind = EntityKind.AudioTrack,
            EntityId = entityId,
            Status = AcquisitionStatus.Stopping,
            TeardownIntent = intent,
            TeardownOriginalStatus = AcquisitionStatus.Searching,
            Title = "Track",
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };

    private static MonitorRow NewMonitor(
        Guid id,
        MonitorStatus status,
        Guid entityId,
        DateTimeOffset updatedAt) =>
        new() {
            Id = id,
            Kind = EntityKind.Movie,
            EntityId = entityId,
            Status = status,
            Title = "Monitor",
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };
}
