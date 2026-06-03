using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Infrastructure.Jobs;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfScanSnapshotStoreTests {
    private static readonly Guid RootId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string Kind = "scan-library";

    [Fact]
    public async Task LoadReturnsEmptyWhenNoSnapshotStored() {
        await using var db = CreateContext();
        var store = new EfScanSnapshotStore(db);

        var loaded = await store.LoadAsync(RootId, Kind, CancellationToken.None);

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task ApplyThenLoadRoundTripsSignatures() {
        await using var db = CreateContext();
        var store = new EfScanSnapshotStore(db);
        var delta = new ScanDelta(
            Added: [new FileSignature("/a.mkv", 10, 1), new FileSignature("/b.mkv", 20, 2)],
            Removed: [],
            Changed: [],
            UnchangedCount: 0);

        await store.ApplyAsync(RootId, Kind, delta, CancellationToken.None);
        var loaded = (await store.LoadAsync(RootId, Kind, CancellationToken.None))
            .OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(2, loaded.Length);
        Assert.Equal(new FileSignature("/a.mkv", 10, 1), loaded[0]);
        Assert.Equal(new FileSignature("/b.mkv", 20, 2), loaded[1]);
    }

    [Fact]
    public async Task ApplyInsertsUpdatesAndDeletesAgainstAnExistingSnapshot() {
        await using var db = CreateContext();
        var store = new EfScanSnapshotStore(db);
        await store.ApplyAsync(RootId, Kind, new ScanDelta(
            Added: [new FileSignature("/keep.mkv", 10, 1), new FileSignature("/change.mkv", 20, 2), new FileSignature("/gone.mkv", 30, 3)],
            Removed: [], Changed: [], UnchangedCount: 0), CancellationToken.None);

        await store.ApplyAsync(RootId, Kind, new ScanDelta(
            Added: [new FileSignature("/fresh.mkv", 40, 4)],
            Removed: [new FileSignature("/gone.mkv", 30, 3)],
            Changed: [new FileSignature("/change.mkv", 99, 9)],
            UnchangedCount: 1), CancellationToken.None);

        var loaded = (await store.LoadAsync(RootId, Kind, CancellationToken.None))
            .ToDictionary(s => s.Path, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(3, loaded.Count);
        Assert.False(loaded.ContainsKey("/gone.mkv"));
        Assert.Equal(new FileSignature("/fresh.mkv", 40, 4), loaded["/fresh.mkv"]);
        Assert.Equal(new FileSignature("/change.mkv", 99, 9), loaded["/change.mkv"]);
        Assert.Equal(new FileSignature("/keep.mkv", 10, 1), loaded["/keep.mkv"]);
    }

    [Fact]
    public async Task SnapshotsAreIsolatedByRootAndScanKind() {
        await using var db = CreateContext();
        var store = new EfScanSnapshotStore(db);
        var otherRoot = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        await store.ApplyAsync(RootId, "scan-library", new ScanDelta([new FileSignature("/video.mkv", 1, 1)], [], [], 0), CancellationToken.None);
        await store.ApplyAsync(RootId, "scan-gallery", new ScanDelta([new FileSignature("/image.png", 2, 2)], [], [], 0), CancellationToken.None);
        await store.ApplyAsync(otherRoot, "scan-library", new ScanDelta([new FileSignature("/other.mkv", 3, 3)], [], [], 0), CancellationToken.None);

        Assert.Equal("/video.mkv", Assert.Single(await store.LoadAsync(RootId, "scan-library", CancellationToken.None)).Path);
        Assert.Equal("/image.png", Assert.Single(await store.LoadAsync(RootId, "scan-gallery", CancellationToken.None)).Path);
        Assert.Equal("/other.mkv", Assert.Single(await store.LoadAsync(otherRoot, "scan-library", CancellationToken.None)).Path);
    }

    [Fact]
    public async Task ApplyWithNoChangesWritesNothing() {
        await using var db = CreateContext();
        var store = new EfScanSnapshotStore(db);

        await store.ApplyAsync(RootId, Kind, ScanDelta.Empty, CancellationToken.None);

        Assert.Empty(await store.LoadAsync(RootId, Kind, CancellationToken.None));
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"scan-snapshot-{Guid.NewGuid():N}")
            .Options);
}
