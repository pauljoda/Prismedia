using System.Net;
using System.Text;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Covers the qBittorrent adapter's add-correlation behavior against a stubbed WebUI: a fresh no-hash
/// add resolves by category diff, a duplicate add (accepted but nothing created) resolves to the
/// already-present torrent by normalized name, an uncorrelatable duplicate throws a duplicate error —
/// never a category error — concurrent adds cannot steal one another's hash, and the connection test
/// validates the category explicitly.
/// </summary>
public sealed class QBittorrentDownloadClientTests {
    private static readonly DownloadClientConnection Connection =
        new(Guid.NewGuid(), DownloadClientKind.QBittorrent, "http://qbit.test", null, null, "prismedia", null);

    [Fact]
    public async Task NoHashAddResolvesTheNewTorrentByCategoryDiff() {
        var handler = new StubHandler();
        handler.CategoryListings.Enqueue("""[{"hash":"aaa","name":"Existing"}]""");
        handler.CategoryListings.Enqueue("""[{"hash":"aaa","name":"Existing"},{"hash":"bbb","name":"Fresh Release"}]""");
        var client = NewClient(handler);

        var hash = await client.AddAsync(
            Connection, new DownloadAddRequest("http://indexer/dl", InfoHash: null, "prismedia", "Fresh Release"), CancellationToken.None);

        Assert.Equal("bbb", hash);
    }

    [Fact]
    public async Task DuplicateAddResolvesToTheExistingTorrentByNormalizedName() {
        // The Bear incident: qBittorrent accepts the add of a torrent it already has and creates
        // nothing. The adapter must correlate by name instead of blaming the category.
        var handler = new StubHandler(repeatLastListing: true);
        handler.CategoryListings.Enqueue(
            """[{"hash":"1742a902","name":"WALT DISNEYS-BEAR IN THE BIG BLUE HOUSE-SHAPES-SOUNDS$COLOURS- @KIDZCORNER DVDRIP[ENG]"}]""");
        var client = NewClient(handler);

        var hash = await client.AddAsync(
            Connection,
            new DownloadAddRequest(
                "http://indexer/dl", InfoHash: null, "prismedia",
                "WALT DISNEYS BEAR IN THE BIG BLUE HOUSE SHAPES SOUNDS COLOURS KIDZCORNER DVDRIP[ENG]"),
            CancellationToken.None);

        Assert.Equal("1742a902", hash);
    }

    [Fact]
    public async Task UncorrelatableDuplicateThrowsADuplicateErrorNotACategoryError() {
        var handler = new StubHandler(repeatLastListing: true);
        handler.CategoryListings.Enqueue("""[{"hash":"aaa","name":"Something Unrelated"}]""");
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<DownloadClientAddUnresolvedException>(() => client.AddAsync(
            Connection, new DownloadAddRequest("http://indexer/dl", InfoHash: null, "prismedia", "Wanted Release"), CancellationToken.None));

        Assert.Contains("already present", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("category", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConcurrentNoHashAddsAreSerializedAcrossScopedClientInstances() {
        var handler = new ConcurrentAddHandler();
        var first = NewClient(handler);
        var second = NewClient(handler);

        var hashes = await Task.WhenAll(
            first.AddAsync(
                Connection,
                new DownloadAddRequest("http://indexer/first", InfoHash: null, "prismedia", "First Release"),
                CancellationToken.None),
            second.AddAsync(
                Connection,
                new DownloadAddRequest("http://indexer/second", InfoHash: null, "prismedia", "Second Release"),
                CancellationToken.None));

        Assert.Equal(2, hashes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(1, handler.MaxConcurrentAdds);
    }

    [Fact]
    public async Task CategorylessConnectionTestChecksConnectivityOnly() {
        // A pre-save test carries no category; posting createCategory with an empty name would be
        // rejected by qBittorrent and misread as a connection/auth failure.
        var categoryless = Connection with { Category = string.Empty };

        var result = await NewClient(new StubHandler()).TestAsync(categoryless, CancellationToken.None);

        Assert.True(result.Connected);
    }

    [Fact]
    public async Task ConnectionTestValidatesTheCategoryExplicitly() {
        var handler = new StubHandler { Categories = """{"prismedia":{"name":"prismedia","savePath":""}}""" };
        var ready = await NewClient(handler).TestAsync(Connection, CancellationToken.None);
        Assert.True(ready.Connected);
        Assert.Contains("prismedia", ready.Message);

        var missing = new StubHandler { Categories = """{"other":{"name":"other","savePath":""}}""" };
        var broken = await NewClient(missing).TestAsync(Connection, CancellationToken.None);
        Assert.False(broken.Connected);
        Assert.Contains("category", broken.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListingCarriesTelemetryWithoutPerTorrentPropertiesRequests() {
        var handler = new StubHandler();
        handler.CategoryListings.Enqueue(
            """[{"hash":"abc","name":"Frozen","progress":0.5,"state":"downloading","save_path":"/save","content_path":"/save/Frozen","total_size":1234,"dlspeed":456,"upspeed":78,"eta":90,"num_seeds":4,"num_leechs":5,"ratio":1.25,"seeding_time":67}]""");

        var item = Assert.Single(await NewClient(handler).ListItemsAsync(Connection, CancellationToken.None));

        Assert.Equal(1234, item.Properties?.TotalSizeBytes);
        Assert.Equal(456, item.Properties?.DownloadSpeedBytesPerSecond);
        Assert.Equal(78, item.Properties?.UploadSpeedBytesPerSecond);
        Assert.Equal(90, item.Properties?.EtaSeconds);
        Assert.Equal(4, item.Properties?.Seeds);
        Assert.Equal(5, item.Properties?.Peers);
        Assert.Equal(1.25, item.Properties?.Ratio);
        Assert.Equal(67, item.Properties?.SeedingTimeSeconds);
    }

    private static QBittorrentDownloadClient NewClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler)) { AddPollDelay = TimeSpan.FromMilliseconds(1) };

    /// <summary>
    /// Minimal WebUI stub: category listings are served from a queue (optionally repeating the last one
    /// forever, as a real client does for a duplicate add), all POSTs succeed, and the categories
    /// endpoint returns a fixed object.
    /// </summary>
    private sealed class StubHandler(bool repeatLastListing = false) : HttpMessageHandler {
        public Queue<string> CategoryListings { get; } = new();
        public string Categories { get; set; } = "{}";
        private string _lastListing = "[]";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/torrents/info", StringComparison.Ordinal)) {
                if (CategoryListings.Count > 0) {
                    _lastListing = CategoryListings.Dequeue();
                } else if (!repeatLastListing) {
                    _lastListing = "[]";
                }

                return Json(_lastListing);
            }

            if (path.EndsWith("/torrents/categories", StringComparison.Ordinal)) {
                return Json(Categories);
            }

            // createCategory, add, version, and any other call simply succeed.
            return Json(path.EndsWith("/app/version", StringComparison.Ordinal) ? "\"v5.0.0\"" : string.Empty);
        }

        private static Task<HttpResponseMessage> Json(string body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private sealed class ConcurrentAddHandler : HttpMessageHandler {
        private readonly object _sync = new();
        private readonly List<(string Hash, string Name)> _torrents = [];
        private int _activeAdds;
        private int _addCount;
        private int _maxConcurrentAdds;

        public int MaxConcurrentAdds => Volatile.Read(ref _maxConcurrentAdds);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/torrents/info", StringComparison.Ordinal)) {
                string listing;
                lock (_sync) {
                    listing = "[" + string.Join(',', _torrents.Select(torrent =>
                        $"{{\"hash\":\"{torrent.Hash}\",\"name\":\"{torrent.Name}\"}}")) + "]";
                }

                return await Json(listing);
            }

            if (path.EndsWith("/torrents/add", StringComparison.Ordinal)) {
                var active = Interlocked.Increment(ref _activeAdds);
                UpdateMaximum(ref _maxConcurrentAdds, active);
                try {
                    await Task.Delay(20, cancellationToken);
                    var ordinal = Interlocked.Increment(ref _addCount);
                    lock (_sync) {
                        _torrents.Add(ordinal == 1 ? ("aaa", "First Release") : ("bbb", "Second Release"));
                    }
                } finally {
                    Interlocked.Decrement(ref _activeAdds);
                }
            }

            return await Json(path.EndsWith("/app/version", StringComparison.Ordinal) ? "\"v5.0.0\"" : string.Empty);
        }

        private static void UpdateMaximum(ref int maximum, int candidate) {
            int observed;
            do {
                observed = Volatile.Read(ref maximum);
                if (candidate <= observed) {
                    return;
                }
            } while (Interlocked.CompareExchange(ref maximum, candidate, observed) != observed);
        }

        private static Task<HttpResponseMessage> Json(string body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
