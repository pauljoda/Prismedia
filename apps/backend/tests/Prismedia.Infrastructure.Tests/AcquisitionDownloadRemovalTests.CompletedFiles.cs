using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Persistence.Migrations;

namespace Prismedia.Infrastructure.Tests;

public sealed partial class AcquisitionDownloadRemovalTests {
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task RecoveryMigrationReopensOnlyCompletedImportedUsenetTransfers() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var migrations = db.Database.GetMigrations().ToArray();
        var recovery = migrations.Single(name => name.EndsWith("_" + nameof(RequeueCompletedUsenetPayloadCleanup), StringComparison.Ordinal));
        await database.MigrateAsync(migrations[Array.IndexOf(migrations, recovery) - 1]);
        var (owner, client, transfer) = await SeedAsync(db);
        (await db.DownloadTransfers.FindAsync(transfer))!.ContentPath = "/downloads/completed-item";
        var active = await AddOwnerAsync(db, client, AcquisitionStatus.Downloading, "active-item");
        active.ContentPath = "/downloads/active-item";
        var partial = await AddOwnerAsync(db, client, AcquisitionStatus.Imported, "partial-item");
        partial.ContentPath = "/downloads/partial-item";
        partial.Progress = 0.5;
        var (_, torrentClient, torrentTransfer) = await SeedAsync(db);
        (await db.DownloadClientConfigs.FindAsync(torrentClient))!.Kind = DownloadClientKind.QBittorrent;
        (await db.DownloadTransfers.FindAsync(torrentTransfer))!.ContentPath = "/downloads/torrent-item";
        await db.SaveChangesAsync();

        await database.MigrateAsync(recovery);
        db.ChangeTracker.Clear();

        var reopened = Assert.Single(await db.DownloadTransfers.Where(row => row.SeedingSince != null).ToArrayAsync());
        Assert.Equal(transfer, reopened.Id);
        Assert.Equal(owner, reopened.AcquisitionId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Category", "PostgreSQL")]
    public async Task CompletedCleanupUsesMappedPathsAndHealsAlreadyRemovedHistory(bool hasHistory) {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (owner, clientId, transfer) = await SeedAsync(db);
        using var files = await PayloadFiles.CreateAsync(db, owner, transfer);
        db.RemotePathMappings.Add(new RemotePathMappingRow { Id = Guid.NewGuid(), DownloadClientConfigId = clientId,
            RemotePath = "/remote", LocalPath = Path.GetDirectoryName(files.Payload)! });
        await db.SaveChangesAsync();
        var client = new Client { DeletesCompletedPayload = false, CompletedDirectories = ["/remote"],
            Item = hasHistory ? new("same-item", null, 1, null, true, "/remote/item", "/remote/item") : null,
            BeforeRemove = () => {
                Assert.False(Directory.Exists(files.Payload));
                Assert.Equal("validated", File.ReadAllText(files.Owned));
                return Task.CompletedTask;
            } };

        await Cleaner(db).RemoveAsync(client, Connection(clientId), owner, "same-item", true, default, transfer);

        Assert.True(client.Removed);
        Assert.False(Directory.Exists(files.Payload));
        Assert.Equal("validated", File.ReadAllText(files.Owned));
    }

    [Fact]
    public async Task CompletedCleanupKeepsHistoryWhenTheLibraryCopyIsMissing() {
        await using var db = Context();
        var (owner, clientId, transfer) = await SeedAsync(db);
        using var files = await PayloadFiles.CreateAsync(db, owner, transfer);
        File.Delete(files.Owned);
        var client = new Client { DeletesCompletedPayload = false, CompletedDirectories = [Path.GetDirectoryName(files.Payload)!] };

        await Assert.ThrowsAsync<IOException>(() => Cleaner(db).RemoveAsync(client, Connection(clientId), owner,
            "same-item", true, default, transfer));

        Assert.False(client.Removed);
        Assert.True(Directory.Exists(files.Payload));
    }

    [Fact]
    public async Task CompletedCleanupProtectsAnOverlappingPayloadWithADifferentClientItemId() {
        await using var db = Context();
        var (owner, clientId, transfer) = await SeedAsync(db);
        using var files = await PayloadFiles.CreateAsync(db, owner, transfer);
        var other = await AddOwnerAsync(db, clientId, AcquisitionStatus.Downloading, "different-item");
        other.ContentPath = Path.Combine(files.Payload, "film.mkv");
        await db.SaveChangesAsync();
        var client = new Client { DeletesCompletedPayload = false, CompletedDirectories = [Path.GetDirectoryName(files.Payload)!] };

        await Assert.ThrowsAsync<IOException>(() => Cleaner(db).RemoveAsync(client, Connection(clientId), owner,
            "same-item", true, default, transfer));

        Assert.False(client.Removed);
        Assert.True(Directory.Exists(files.Payload));
    }

    [Fact]
    public async Task DetachedReceiptCanCleanCompletedFilesAfterTheRequestAndHistoryAreGone() {
        await using var db = Context();
        var (owner, clientId, transfer) = await SeedAsync(db);
        using var files = await PayloadFiles.CreateAsync(db, owner, transfer);
        db.DownloadTransfers.Remove((await db.DownloadTransfers.FindAsync(transfer))!);
        db.DetachedDownloadCleanups.Add(new DetachedDownloadCleanupRow { Id = Guid.NewGuid(), DownloadClientConfigId = clientId,
            ClientItemId = "same-item", ContentPath = files.Payload, ImportedSourcePath = files.Owned });
        await db.SaveChangesAsync();
        var client = new Client { DeletesCompletedPayload = false, CompletedDirectories = [Path.GetDirectoryName(files.Payload)!] };

        await Cleaner(db).RemoveAsync(client, Connection(clientId), null, "same-item", true, default);

        Assert.True(client.Removed);
        Assert.False(Directory.Exists(files.Payload));
        Assert.Equal("validated", File.ReadAllText(files.Owned));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task CorruptedPayloadPointersCannotDeleteACompletedRootOrAnUnrelatedDirectory(bool pointsAtRoot, bool filesystemRoot) {
        await using var db = Context();
        var (owner, clientId, transfer) = await SeedAsync(db);
        using var files = await PayloadFiles.CreateAsync(db, owner, transfer);
        var client = new Client { DeletesCompletedPayload = false,
            CompletedDirectories = [filesystemRoot ? Path.GetPathRoot(files.Payload)! : pointsAtRoot ? files.Payload : Path.Combine(Path.GetDirectoryName(files.Payload)!, "different-root")] };

        await Assert.ThrowsAsync<IOException>(() => Cleaner(db).RemoveAsync(client, Connection(clientId), owner,
            "same-item", true, default, transfer));

        Assert.False(client.Removed);
        Assert.True(Directory.Exists(files.Payload));
        Assert.Equal("validated", File.ReadAllText(files.Owned));
    }

    private static EfAcquisitionDownloadRemoval Cleaner(PrismediaDbContext db) => new(db,
        new EfCompletedDownloadPayloadCleanup(db, new RemotePathMapper(new EfRemotePathMappingStore(db))));

    private sealed class PayloadFiles : IDisposable {
        private readonly string root = Path.Combine(Path.GetTempPath(), "completed-payload-" + Guid.NewGuid().ToString("N"));
        public string Payload => Path.Combine(root, "downloads/item");
        public string Owned => Path.Combine(root, "library/film.mkv");
        public static async Task<PayloadFiles> CreateAsync(PrismediaDbContext db, Guid owner, Guid transfer) {
            var files = new PayloadFiles();
            Directory.CreateDirectory(files.Payload);
            Directory.CreateDirectory(Path.GetDirectoryName(files.Owned)!);
            File.WriteAllText(Path.Combine(files.Payload, "film.mkv"), "validated");
            File.WriteAllText(files.Owned, "validated");
            (await db.Acquisitions.FindAsync(owner))!.FinalSourcePath = files.Owned;
            (await db.DownloadTransfers.FindAsync(transfer))!.ContentPath = files.Payload;
            db.LibraryRoots.Add(new LibraryRootRow { Id = Guid.NewGuid(), Path = Path.GetDirectoryName(files.Owned)!, Label = "Library" });
            await db.SaveChangesAsync();
            return files;
        }
        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
