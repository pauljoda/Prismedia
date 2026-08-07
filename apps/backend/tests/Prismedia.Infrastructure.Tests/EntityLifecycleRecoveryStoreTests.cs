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
        Assert.True(await store.CompleteOrphanedDeletionAsync(
            orphanedDeletingMonitorId,
            CancellationToken.None));
        Assert.False(await store.CompleteOrphanedDeletionAsync(
            ownedDeletingMonitorId,
            CancellationToken.None));
        Assert.Null(await db.Monitors.FindAsync(orphanedDeletingMonitorId));
        Assert.NotNull(await db.Monitors.FindAsync(ownedDeletingMonitorId));
    }

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
