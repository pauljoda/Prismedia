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
/// never a category error — and the connection test validates the category explicitly.
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

    private static QBittorrentDownloadClient NewClient(StubHandler handler) =>
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
}
