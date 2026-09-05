using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed partial class AcquisitionDownloadRemovalTests {
    [Theory]
    [InlineData(AcquisitionStatus.Queued)]
    [InlineData(AcquisitionStatus.Downloading)]
    [InlineData(AcquisitionStatus.Downloaded)]
    [InlineData(AcquisitionStatus.Importing)]
    [InlineData(AcquisitionStatus.ManualImportRequired)]
    public async Task FactoryRemovalProtectsOtherActiveOwners(AcquisitionStatus otherStatus) {
        await using var db = Context();
        var (owner, clientId, transfer) = await SeedAsync(db);
        await AddOwnerAsync(db, clientId, otherStatus);
        var client = new Client();
        var factory = new DownloadClientFactory([client], new EfAcquisitionDownloadRemoval(db));

        await Assert.ThrowsAsync<IOException>(() => factory.Get(client.Kind).RemoveOwnedAsync(
            Connection(clientId), owner, "same-item", true, default, transfer));

        Assert.False(client.Removed);
        Assert.Equal(2, await db.DownloadTransfers.CountAsync());
    }

    [Theory]
    [InlineData(1, 5, false)]
    [InlineData(2, 5, true)]
    [InlineData(1, 3600, true)]
    public async Task SharedImportedOwnerRetainsItsOwnSeedingGoal(double ratio, long seconds, bool removes) {
        await using var db = Context();
        var (owner, clientId, transfer) = await SeedAsync(db);
        var other = await AddOwnerAsync(db, clientId, AcquisitionStatus.Imported);
        other.SeedingSince = DateTimeOffset.UtcNow;
        other.SeedGoalRatio = 2;
        other.SeedGoalTimeMinutes = 60;
        await db.SaveChangesAsync();
        var client = new Client { Properties = new(1, 0, 0, 0, 0, 0, null, ratio, seconds) };
        var remove = () => new DownloadClientFactory([client], new EfAcquisitionDownloadRemoval(db)).Get(client.Kind)
            .RemoveOwnedAsync(Connection(clientId), owner, "same-item", true, default, transfer);

        if (removes) await remove();
        else await Assert.ThrowsAsync<IOException>(remove);
        Assert.Equal(removes, client.Removed);
    }

    [Fact]
    public async Task ObsoleteTransferSnapshotsAndWrongClientsCannotRemoveANewerItem() {
        await using var db = Context();
        var (owner, clientId, _) = await SeedAsync(db);
        var client = new Client();
        var wrapped = new DownloadClientFactory([client], new EfAcquisitionDownloadRemoval(db)).Get(client.Kind);

        await Assert.ThrowsAsync<IOException>(() => wrapped.RemoveOwnedAsync(Connection(clientId), owner, "same-item", true, default, Guid.NewGuid()));
        await Assert.ThrowsAsync<IOException>(() => wrapped.RemoveOwnedAsync(Connection(Guid.NewGuid()), owner, "same-item", true, default));
        Assert.False(client.Removed);
    }

    [Fact]
    public async Task DetachedReceiptRemainsAuthoritativeAfterItsOriginalAcquisitionIsGone() {
        await using var db = Context();
        var clientId = Guid.NewGuid();
        db.DetachedDownloadCleanups.Add(new DetachedDownloadCleanupRow {
            Id = Guid.NewGuid(), DownloadClientConfigId = clientId, ClientItemId = "same-item"
        });
        await db.SaveChangesAsync();
        var client = new Client();

        await new DownloadClientFactory([client], new EfAcquisitionDownloadRemoval(db)).Get(client.Kind)
            .RemoveOwnedAsync(Connection(clientId), null, "same-item", true, default);

        Assert.True(client.Removed);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task RemovalHoldsTheClientLockUntilTheRemoteDeletionCompletes() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Guid owner, clientId, transferId, nextOwner;
        await using (var setup = database.CreateContext()) {
            (owner, clientId, transferId) = await SeedAsync(setup);
            nextOwner = (await AddOwnerAsync(setup, clientId, AcquisitionStatus.Queued, "different-item")).AcquisitionId;
        }
        await using var removing = database.CreateContext();
        await using var adding = database.CreateContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client { BeforeRemove = async () => { entered.SetResult(); await release.Task; } };
        var removal = new EfAcquisitionDownloadRemoval(removing).RemoveAsync(client, Connection(clientId), owner,
            "same-item", true, default, transferId);
        try {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var add = new EfAcquisitionTransferAddCoordinator(adding).AcquireAsync(nextOwner, clientId, "different-category", default);
            Assert.NotSame(add, await Task.WhenAny(add, Task.Delay(200)));
            release.TrySetResult();
            await removal.WaitAsync(TimeSpan.FromSeconds(10));
            await using var lease = await add.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(lease);
            await lease.CommitAsync(default);
        } finally { release.TrySetResult(); }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task RemovalRechecksAnOwnerThatFinishedAddingWhileCleanupWaited() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Guid owner, clientId, transferId, nextOwner;
        await using (var setup = database.CreateContext()) {
            (owner, clientId, transferId) = await SeedAsync(setup);
            nextOwner = (await AddOwnerAsync(setup, clientId, AcquisitionStatus.Queued, "different-item")).AcquisitionId;
        }
        await using var adding = database.CreateContext();
        await using var removing = database.CreateContext();
        var client = new Client();
        var lease = await new EfAcquisitionTransferAddCoordinator(adding).AcquireAsync(nextOwner, clientId, "different-category", default);
        Assert.NotNull(lease);
        try {
            var removal = new EfAcquisitionDownloadRemoval(removing).RemoveAsync(client, Connection(clientId), owner,
                "same-item", true, default, transferId);
            Assert.NotSame(removal, await Task.WhenAny(removal, Task.Delay(200)));
            (await adding.DownloadTransfers.SingleAsync(row => row.AcquisitionId == nextOwner)).ClientItemId = "same-item";
            await adding.SaveChangesAsync();
            await lease.CommitAsync(default);
            await lease.DisposeAsync();
            await Assert.ThrowsAsync<IOException>(() => removal.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.False(client.Removed);
        } finally { await lease.DisposeAsync(); }
    }

    private static PrismediaDbContext Context() => new(new DbContextOptionsBuilder<PrismediaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static DownloadClientConnection Connection(Guid clientId) => new(clientId, DownloadClientKind.Sabnzbd, "http://client", null, null, "validation");
    private static async Task<(Guid Owner, Guid Client, Guid Transfer)> SeedAsync(PrismediaDbContext db) {
        var client = Guid.NewGuid();
        db.DownloadClientConfigs.Add(new DownloadClientConfigRow { Id = client, Kind = DownloadClientKind.Sabnzbd,
            DisplayName = "Downloads", BaseUrl = "http://client", Category = "validation" });
        var transfer = await AddOwnerAsync(db, client, AcquisitionStatus.Imported);
        return (transfer.AcquisitionId, client, transfer.Id);
    }
    private static async Task<DownloadTransferRow> AddOwnerAsync(PrismediaDbContext db, Guid client, AcquisitionStatus status, string itemId = "same-item") {
        var owner = new AcquisitionRow { Id = Guid.NewGuid(), Kind = EntityKind.Movie, Title = "Film", Status = status };
        var transfer = new DownloadTransferRow { Id = Guid.NewGuid(), AcquisitionId = owner.Id,
            DownloadClientConfigId = client, ClientItemId = itemId, Progress = 1 };
        db.Acquisitions.Add(owner); db.DownloadTransfers.Add(transfer); await db.SaveChangesAsync();
        return transfer;
    }

    private sealed class Client : IDownloadClient {
        public DownloadClientKind Kind => DownloadClientKind.Sabnzbd;
        public bool DeletesCompletedPayload { get; init; } = true;
        public IReadOnlyList<string> CompletedDirectories { get; init; } = [];
        public Task<IReadOnlyList<string>> GetCompletedDirectoriesAsync(DownloadClientConnection connection, CancellationToken token) => Task.FromResult(CompletedDirectories);
        public DownloadItemStatus? Item { get; init; }
        public bool Removed { get; private set; }
        public Func<Task>? BeforeRemove { get; init; }
        public DownloadItemProperties? Properties { get; init; }
        public async Task RemoveAsync(DownloadClientConnection connection, string itemId, bool deleteData, CancellationToken token) {
            if (BeforeRemove is not null) await BeforeRemove();
            Removed = true;
        }
        public Task<DownloadItemProperties?> GetPropertiesAsync(DownloadClientConnection connection, string itemId, CancellationToken token) => Task.FromResult(Properties);
        public Task<string> AddAsync(DownloadClientConnection connection, DownloadAddRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<string> AddTorrentFileAsync(DownloadClientConnection connection, string name, byte[] data, CancellationToken token) => throw new NotSupportedException();
        public Task<DownloadItemStatus?> GetItemAsync(DownloadClientConnection connection, string itemId, CancellationToken token) => Task.FromResult(Item);
        public Task<IReadOnlyList<DownloadItemStatus>> ListItemsAsync(DownloadClientConnection connection, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadItemFile>> GetFilesAsync(DownloadClientConnection connection, string itemId, CancellationToken token) => throw new NotSupportedException();
        public Task<byte[]> GetPieceStatesAsync(DownloadClientConnection connection, string itemId, CancellationToken token) => throw new NotSupportedException();
        public Task<DownloadClientConnectionTest> TestAsync(DownloadClientConnection connection, CancellationToken token) => throw new NotSupportedException();
    }
}
