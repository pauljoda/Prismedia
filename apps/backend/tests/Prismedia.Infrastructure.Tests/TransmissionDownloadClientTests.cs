using System.Net;
using System.Text;
using System.Text.Json;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Pins the Transmission adapter's RPC behavior against canned responses: the 409 session-id
/// handshake, hash extraction from torrent-added/duplicate, status projection (stalled, error,
/// completion, content path), category-label scoping, and the piece-bitfield decode.
/// </summary>
public sealed class TransmissionDownloadClientTests {
    private static readonly DownloadClientConnection Connection =
        new(Guid.NewGuid(), DownloadClientKind.Transmission, "http://transmission:9091", "user", "pass", "prismedia");

    [Fact]
    public async Task AddHandshakesTheSessionIdAndReturnsTheHash() {
        var handler = new RpcHandler(body =>
            """{"result": "success", "arguments": {"torrent-added": {"id": 5, "hashString": "ABCDEF1234", "name": "My.Release"}}}""");
        var client = new TransmissionDownloadClient(new HttpClient(handler));

        var id = await client.AddAsync(Connection, new DownloadAddRequest("magnet:?xt=urn:btih:abc", null, "prismedia"), CancellationToken.None);

        Assert.Equal("abcdef1234", id);
        // First call answered 409 with the session id; the retry must carry it.
        Assert.Equal(2, handler.Requests.Count);
        Assert.True(handler.Requests[1].Headers.Contains("X-Transmission-Session-Id"));
    }

    [Fact]
    public async Task ADuplicateAddStillResolvesTheHash() {
        var handler = new RpcHandler(_ =>
            """{"result": "success", "arguments": {"torrent-duplicate": {"id": 5, "hashString": "FEEDBEEF", "name": "My.Release"}}}""");
        var client = new TransmissionDownloadClient(new HttpClient(handler));

        var id = await client.AddAsync(Connection, new DownloadAddRequest("magnet:?xt=urn:btih:abc", null, "prismedia"), CancellationToken.None);

        Assert.Equal("feedbeef", id);
    }

    [Fact]
    public async Task StatusMapsProgressErrorAndContentPath() {
        var handler = new RpcHandler(_ =>
            """{"result": "success", "arguments": {"torrents": [{"hashString": "AA11", "name": "Show.S01", "percentDone": 0.5, "status": 4, "isStalled": false, "isFinished": false, "error": 0, "errorString": "", "downloadDir": "/downloads", "labels": ["prismedia"]}]}}""");
        var client = new TransmissionDownloadClient(new HttpClient(handler));

        var status = await client.GetItemAsync(Connection, "aa11", CancellationToken.None);

        Assert.NotNull(status);
        Assert.Equal(0.5, status.Progress);
        Assert.False(status.IsComplete);
        Assert.False(status.IsStalled);
        Assert.Equal(Path.Combine("/downloads", "Show.S01"), status.ContentPath);
    }

    [Fact]
    public async Task AStalledOrErroredTorrentReadsStalledUnlessComplete() {
        var handler = new RpcHandler(_ =>
            """{"result": "success", "arguments": {"torrents": [{"hashString": "AA11", "name": "Dead", "percentDone": 0.1, "status": 4, "isStalled": true, "error": 3, "errorString": "No data found", "downloadDir": "/d", "labels": ["prismedia"]}]}}""");
        var client = new TransmissionDownloadClient(new HttpClient(handler));

        var status = await client.GetItemAsync(Connection, "aa11", CancellationToken.None);

        Assert.NotNull(status);
        Assert.True(status.IsStalled);
        Assert.Equal("No data found", status.State);
    }

    [Fact]
    public async Task ListItemsScopesToTheCategoryLabel() {
        var handler = new RpcHandler(_ =>
            """{"result": "success", "arguments": {"torrents": [{"hashString": "MINE", "name": "A", "percentDone": 1.0, "labels": ["prismedia"]}, {"hashString": "OTHER", "name": "B", "percentDone": 1.0, "labels": ["radarr"]}, {"hashString": "NONE", "name": "C", "percentDone": 1.0, "labels": []}]}}""");
        var client = new TransmissionDownloadClient(new HttpClient(handler));

        var items = await client.ListItemsAsync(Connection, CancellationToken.None);

        Assert.Equal(["mine"], items.Select(item => item.ClientItemId).ToArray());
    }

    [Fact]
    public async Task PieceBitfieldDecodesToHaveStates() {
        // 0b10100000 → pieces 0 and 2 done of 4.
        var bitfield = Convert.ToBase64String([0b1010_0000]);
        var handler = new RpcHandler(_ =>
            """{"result": "success", "arguments": {"torrents": [{"pieces": "BITFIELD", "pieceCount": 4}]}}""".Replace("BITFIELD", bitfield));
        var client = new TransmissionDownloadClient(new HttpClient(handler));

        var states = await client.GetPieceStatesAsync(Connection, "aa11", CancellationToken.None);

        Assert.Equal(new byte[] { 2, 0, 2, 0 }, states);
    }

    [Fact]
    public async Task AnRpcFailureSurfacesTheResultText() {
        var handler = new RpcHandler(_ => """{"result": "unrecognized torrent", "arguments": {}}""");
        var client = new TransmissionDownloadClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.RemoveAsync(Connection, "aa11", deleteData: true, CancellationToken.None));
        Assert.Contains("unrecognized torrent", ex.Message);
    }

    /// <summary>Answers the first request with the 409 session handshake, then canned RPC JSON; records requests.</summary>
    private sealed class RpcHandler(Func<string, string> bodyFor) : HttpMessageHandler {
        public List<HttpRequestMessage> Requests { get; } = [];
        private bool _handshook;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add(request);
            if (!_handshook) {
                _handshook = true;
                var conflict = new HttpResponseMessage(HttpStatusCode.Conflict);
                conflict.Headers.Add("X-Transmission-Session-Id", "session-123");
                return conflict;
            }

            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(bodyFor(body), Encoding.UTF8, "application/json")
            };
        }
    }
}
