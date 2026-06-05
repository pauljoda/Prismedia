using Prismedia.Infrastructure.Videos;

namespace Prismedia.Infrastructure.Tests;

public sealed class TranscodeCacheServiceTests : IDisposable {
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), $"pm-cache-{Guid.NewGuid():N}");

    public TranscodeCacheServiceTests() => Directory.CreateDirectory(_cacheRoot);

    public void Dispose() {
        try {
            Directory.Delete(_cacheRoot, recursive: true);
        } catch {
            // Best-effort cleanup of the temp cache.
        }
    }

    private TranscodeCacheService Service() => new(new HlsAssetServiceOptions(_cacheRoot));

    private (Guid Id, string Dir) WriteCachedItem(string root, int sizeBytes, DateTime lastWriteUtc) {
        var id = Guid.NewGuid();
        var dir = Path.Combine(_cacheRoot, root, id.ToString());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "seg_00000.ts");
        File.WriteAllBytes(file, new byte[sizeBytes]);
        File.SetLastWriteTimeUtc(file, lastWriteUtc);
        Directory.SetLastWriteTimeUtc(dir, lastWriteUtc);
        return (id, dir);
    }

    [Fact]
    public void ComputeSizeBytesSumsEveryCacheRoot() {
        WriteCachedItem("hlsv", 1000, DateTime.UtcNow);
        WriteCachedItem("hls", 500, DateTime.UtcNow);
        WriteCachedItem("hls2", 250, DateTime.UtcNow);

        Assert.Equal(1750, Service().ComputeSizeBytes());
    }

    [Fact]
    public void PruneEvictsLeastRecentlyPlayedUntilUnderLimit() {
        var now = DateTime.UtcNow;
        var (_, oldDir) = WriteCachedItem("hlsv", 1000, now.AddHours(-2));
        var (_, newDir) = WriteCachedItem("hlsv", 1000, now);

        var evicted = Service().PruneToLimit(maxBytes: 1000, liveItemIds: new HashSet<Guid>());

        Assert.Equal(1, evicted);
        Assert.False(Directory.Exists(oldDir)); // older item evicted first
        Assert.True(Directory.Exists(newDir));
    }

    [Fact]
    public void PruneNeverEvictsAnActivelyPlayingItem() {
        var now = DateTime.UtcNow;
        var (oldId, oldDir) = WriteCachedItem("hlsv", 1000, now.AddHours(-2));
        var (_, newDir) = WriteCachedItem("hlsv", 1000, now);

        // The oldest item is live, so the next-oldest is evicted instead — never the playing one.
        var evicted = Service().PruneToLimit(maxBytes: 1000, liveItemIds: new HashSet<Guid> { oldId });

        Assert.Equal(1, evicted);
        Assert.True(Directory.Exists(oldDir));
        Assert.False(Directory.Exists(newDir));
    }

    [Fact]
    public void PruneAggregatesAnItemSizeAcrossAllCacheRoots() {
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        // One logical item split across two roots; its combined size (1500) exceeds the limit.
        WriteCachedItemWithId(id, "hlsv", 1000, now);
        WriteCachedItemWithId(id, "hls", 500, now);

        var evicted = Service().PruneToLimit(maxBytes: 1000, liveItemIds: new HashSet<Guid>());

        Assert.Equal(1, evicted);
        Assert.Equal(0, Service().ComputeSizeBytes());
    }

    [Fact]
    public void PruneWithUnlimitedLeavesEverything() {
        WriteCachedItem("hlsv", 1000, DateTime.UtcNow);
        Assert.Equal(0, Service().PruneToLimit(maxBytes: 0, liveItemIds: new HashSet<Guid>()));
    }

    [Fact]
    public void ClearRemovesAllCachedOutputAndReportsBytesFreed() {
        WriteCachedItem("hlsv", 1000, DateTime.UtcNow);
        WriteCachedItem("hls", 500, DateTime.UtcNow);
        var service = Service();

        var freed = service.Clear();

        Assert.Equal(1500, freed);
        Assert.Equal(0, service.ComputeSizeBytes());
    }

    private void WriteCachedItemWithId(Guid id, string root, int sizeBytes, DateTime lastWriteUtc) {
        var dir = Path.Combine(_cacheRoot, root, id.ToString());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "seg_00000.ts");
        File.WriteAllBytes(file, new byte[sizeBytes]);
        File.SetLastWriteTimeUtc(file, lastWriteUtc);
        Directory.SetLastWriteTimeUtc(dir, lastWriteUtc);
    }
}
