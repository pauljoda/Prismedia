using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Pins post-transfer watch bookkeeping: a transfer created with a seed goal waits for that goal, while
/// a goal-less transfer enters cleanup-pending so the monitor removes it. The watch carries captured
/// goals, clearing ends it, and watches alone keep the monitor scheduled.
/// </summary>
public sealed class EfAcquisitionStoreSeedingTests {
    [Fact]
    public async Task ATransferWithAGoalEntersAndLeavesTheSeedingWatch() {
        await using var db = CreateContext();
        var store = AcquisitionTestFactory.Store(db);
        var acquisition = await store.CreateAsync(new AcquisitionMetadata("Book", null, null, null, null, null, EntityKind.Book), CancellationToken.None);
        await store.CreateTransferAsync(acquisition.Id, Guid.NewGuid(), "hash1", "prismedia", CancellationToken.None, new TransferSeedGoal(1.5, 4320));

        Assert.True(await store.MarkTransferSeedingAsync(acquisition.Id, DateTimeOffset.UtcNow, CancellationToken.None));

        var watch = Assert.Single(await store.ListSeedingTransfersAsync(CancellationToken.None));
        Assert.Equal("hash1", watch.ClientItemId);
        Assert.Equal(1.5, watch.GoalRatio);
        Assert.Equal(4320, watch.GoalTimeMinutes);
        // Seeding watches keep the monitor scheduled even though the acquisition itself is done.
        Assert.True(await store.HasActiveTransfersAsync(CancellationToken.None));

        await store.ClearTransferSeedingAsync(watch.TransferId, CancellationToken.None);
        Assert.Empty(await store.ListSeedingTransfersAsync(CancellationToken.None));
        Assert.False(await store.HasActiveTransfersAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AGoalLessTransferEntersImmediateCleanupWatch() {
        await using var db = CreateContext();
        var store = AcquisitionTestFactory.Store(db);
        var acquisition = await store.CreateAsync(new AcquisitionMetadata("Book", null, null, null, null, null, EntityKind.Book), CancellationToken.None);
        await store.CreateTransferAsync(acquisition.Id, Guid.NewGuid(), "hash1", "prismedia", CancellationToken.None);

        Assert.True(await store.MarkTransferSeedingAsync(acquisition.Id, DateTimeOffset.UtcNow, CancellationToken.None));
        var watch = Assert.Single(await store.ListSeedingTransfersAsync(CancellationToken.None));
        Assert.Null(watch.GoalRatio);
        Assert.Null(watch.GoalTimeMinutes);
        Assert.True(await store.HasActiveTransfersAsync(CancellationToken.None));
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
