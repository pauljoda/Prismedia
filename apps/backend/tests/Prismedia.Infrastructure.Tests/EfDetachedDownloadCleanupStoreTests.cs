using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Pins the atomic ownership handoff that lets a manual replacement acquire a new transfer pointer while
/// the old exact client item remains durable cleanup work.
/// </summary>
public sealed class EfDetachedDownloadCleanupStoreTests {
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task UpgradeHandoffIsIdempotentAndSurvivesDeletionOfTheTemporaryOwner() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (owner, transferId) = await SeedUpgradeAsync(db);
        var store = new EfDetachedDownloadCleanupStore(db);

        Assert.True(await store.PreserveUpgradeAsync(owner, default));
        Assert.True(await store.PreserveUpgradeAsync(owner, default));
        Assert.Empty(await db.DownloadTransfers.ToArrayAsync());
        await db.Acquisitions.Where(row => row.Id == owner).ExecuteDeleteAsync();
        db.ChangeTracker.Clear();

        var cleanup = Assert.Single(await db.DetachedDownloadCleanups.ToArrayAsync());
        Assert.Equal(transferId, cleanup.Id);
        Assert.Null(cleanup.SourceAcquisitionId);
        Assert.Equal("/downloads/upgrade", cleanup.ContentPath);
        Assert.Equal("/library/Film.mkv", cleanup.ImportedSourcePath);
        Assert.True(await store.PreserveUpgradeAsync(owner, default));
    }

    [Theory]
    [InlineData(AcquisitionStatus.Downloading)]
    [InlineData(AcquisitionStatus.ManualImportRequired)]
    [InlineData(AcquisitionStatus.Stopping)]
    public async Task UpgradeHandoffPreservesAnUnreadyOrClaimedLifecycle(AcquisitionStatus status) {
        await using var db = CreateContext();
        var (owner, _) = await SeedUpgradeAsync(db);
        (await db.Acquisitions.FindAsync(owner))!.Status = status;
        await db.SaveChangesAsync();

        Assert.False(await new EfDetachedDownloadCleanupStore(db).PreserveUpgradeAsync(owner, default));

        Assert.Single(await db.DownloadTransfers.ToArrayAsync());
        Assert.Empty(await db.DetachedDownloadCleanups.ToArrayAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpgradeHandoffPreservesIncompleteTransfersAndUnknownClients(bool incomplete) {
        await using var db = CreateContext();
        var (owner, transferId) = await SeedUpgradeAsync(db);
        var transfer = (await db.DownloadTransfers.FindAsync(transferId))!;
        if (incomplete) transfer.Progress = 0.5;
        else transfer.DownloadClientConfigId = null;
        await db.SaveChangesAsync();

        Assert.False(await new EfDetachedDownloadCleanupStore(db).PreserveUpgradeAsync(owner, default));

        Assert.Single(await db.DownloadTransfers.ToArrayAsync());
        Assert.Empty(await db.DetachedDownloadCleanups.ToArrayAsync());
    }

    private static async Task<(Guid Owner, Guid Transfer)> SeedUpgradeAsync(PrismediaDbContext db) {
        var parent = new AcquisitionRow { Id = Guid.NewGuid(), Status = AcquisitionStatus.Imported,
            Kind = EntityKind.Movie, Title = "Film", FinalSourcePath = "/library/Film.mkv" };
        var child = new AcquisitionRow { Id = Guid.NewGuid(), Status = AcquisitionStatus.Importing,
            Kind = EntityKind.Movie, Title = "Film", UpgradeOfAcquisitionId = parent.Id };
        var client = new DownloadClientConfigRow { Id = Guid.NewGuid(), Kind = DownloadClientKind.Sabnzbd,
            DisplayName = "Downloads", BaseUrl = "http://client", Category = "validation" };
        var transfer = new DownloadTransferRow { Id = Guid.NewGuid(), AcquisitionId = child.Id,
            DownloadClientConfigId = client.Id, ClientItemId = "upgrade-item", ContentPath = "/downloads/upgrade", Progress = 1 };
        db.Acquisitions.AddRange(parent, child);
        db.DownloadClientConfigs.Add(client);
        db.DownloadTransfers.Add(transfer);
        await db.SaveChangesAsync();
        return (child.Id, transfer.Id);
    }

    [Fact]
    public async Task DetachMovesTheExactTransferIntoIndependentCleanup() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Status = AcquisitionStatus.Queued,
            Title = "Book upgrade",
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.DownloadClientConfigs.Add(new DownloadClientConfigRow {
            Id = clientId,
            Kind = DownloadClientKind.QBittorrent,
            DisplayName = "qBittorrent",
            BaseUrl = "http://qbit",
            Category = "prismedia",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            DownloadClientConfigId = clientId,
            ClientItemId = "old-hash",
            Progress = 0.4,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var store = new EfDetachedDownloadCleanupStore(db);

        Assert.True(await store.DetachAsync(acquisitionId, clientId, "old-hash", CancellationToken.None));

        Assert.Empty(await db.DownloadTransfers.AsNoTracking().ToArrayAsync());
        var cleanup = Assert.Single(await store.ListAsync(CancellationToken.None));
        Assert.Equal(acquisitionId, cleanup.AcquisitionId);
        Assert.Equal(clientId, cleanup.DownloadClientConfigId);
        Assert.Equal("old-hash", cleanup.ClientItemId);
        var acquisition = await db.Acquisitions.SingleAsync(row => row.Id == acquisitionId);
        acquisition.Status = AcquisitionStatus.Failed;
        await db.SaveChangesAsync();
        Assert.True(await AcquisitionTestFactory.Store(db).HasActiveTransfersAsync(CancellationToken.None));

        await store.CompleteAsync(cleanup.Id, CancellationToken.None);

        Assert.Empty(await store.ListAsync(CancellationToken.None));
        Assert.False(await AcquisitionTestFactory.Store(db).HasActiveTransfersAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DetachRefusesAChangedTransferPointer() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Status = AcquisitionStatus.Queued,
            Title = "Book upgrade",
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            DownloadClientConfigId = clientId,
            ClientItemId = "new-owner",
            Progress = 0,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var store = new EfDetachedDownloadCleanupStore(db);

        Assert.False(await store.DetachAsync(acquisitionId, clientId, "old-owner", CancellationToken.None));

        Assert.Equal("new-owner", Assert.Single(await db.DownloadTransfers.AsNoTracking().ToArrayAsync()).ClientItemId);
        Assert.Empty(await store.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DetachRefusesAnUnclaimedAcquisitionLifecycle() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Status = AcquisitionStatus.Failed,
            Title = "Book upgrade",
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.DownloadClientConfigs.Add(new DownloadClientConfigRow {
            Id = clientId,
            Kind = DownloadClientKind.QBittorrent,
            DisplayName = "qBittorrent",
            BaseUrl = "http://qbit",
            Category = "prismedia",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            DownloadClientConfigId = clientId,
            ClientItemId = "old-hash",
            Progress = 0.4,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var store = new EfDetachedDownloadCleanupStore(db);

        Assert.False(await store.DetachAsync(acquisitionId, clientId, "old-hash", CancellationToken.None));

        Assert.Equal("old-hash", Assert.Single(await db.DownloadTransfers.AsNoTracking().ToArrayAsync()).ClientItemId);
        Assert.Empty(await store.ListAsync(CancellationToken.None));
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
