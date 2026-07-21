using System.Net;
using System.Text;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

public sealed class SlskdDownloadClientTests {
    private static readonly DownloadClientConnection Connection = new(
        Guid.NewGuid(), DownloadClientKind.Slskd, "http://slskd:5030", null, null,
        "prismedia", "secret", "/downloads");

    [Fact]
    public async Task AddEnqueuesTheOpaqueLocatorAsADeterministicBatch() {
        var locator = SoulseekLocator.Encode(new SoulseekReleaseLocator(
            Guid.Parse("11111111-1111-1111-1111-111111111111"), "peer",
            [new SoulseekFileLocator("Music\\Album\\01.flac", 100)]));
        var handler = new Handler(_ => """{"batch":{"id":"33333333-3333-3333-3333-333333333333","transfers":[]},"failures":[]}""");
        var client = new SlskdDownloadClient(new HttpClient(handler));

        var id = await client.AddAsync(Connection, new DownloadAddRequest(locator, null, "prismedia", "Artist Album"), CancellationToken.None);

        Assert.True(Guid.TryParse(id, out _));
        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/api/v0/transfers/downloads/batches", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("\"username\":\"peer\"", handler.Bodies.Single(), StringComparison.Ordinal);
        Assert.Contains("\"destination\":\"prismedia/", handler.Bodies.Single(), StringComparison.Ordinal);
        Assert.Equal("secret", request.Headers.GetValues(SoulseekProtocol.ApiKeyHeader).Single());
    }

    [Fact]
    public async Task CompletedBatchAggregatesProgressAndReportsItsDownloadDirectory() {
        var handler = new Handler(_ => """
          {"id":"33333333-3333-3333-3333-333333333333","username":"peer","options":{"destination":"prismedia/33333333-3333-3333-3333-333333333333","externalId":"Artist Album"},"transfers":[
            {"id":"44444444-4444-4444-4444-444444444444","filename":"01.flac","size":100,"bytesTransferred":100,"state":"Completed, Succeeded"},
            {"id":"55555555-5555-5555-5555-555555555555","filename":"02.flac","size":300,"bytesTransferred":300,"state":"Completed, Succeeded"}
          ]}
          """);
        var client = new SlskdDownloadClient(new HttpClient(handler));

        var item = await client.GetItemAsync(Connection, "33333333-3333-3333-3333-333333333333", CancellationToken.None);

        Assert.NotNull(item);
        Assert.True(item.IsComplete);
        Assert.Equal(1, item.Progress);
        Assert.Equal("/downloads/prismedia/33333333-3333-3333-3333-333333333333", item.ContentPath);
    }

    [Fact]
    public async Task AddRecoversAnIdempotentBatchConflict() {
        var locator = SoulseekLocator.Encode(new SoulseekReleaseLocator(
            Guid.Parse("11111111-1111-1111-1111-111111111111"), "peer",
            [new SoulseekFileLocator("Music\\Album\\01.flac", 100)]));
        var handler = new StatusHandler([
            (HttpStatusCode.Conflict, "duplicate"),
            (HttpStatusCode.OK, """{"id":"33333333-3333-3333-3333-333333333333","username":"peer","transfers":[{"id":"44444444-4444-4444-4444-444444444444","filename":"Music\\Album\\01.flac","size":100,"bytesTransferred":0,"state":"Queued"}]}""")
        ]);
        var client = new SlskdDownloadClient(new HttpClient(handler));

        var id = await client.AddAsync(Connection, new DownloadAddRequest(locator, null, "prismedia", "Artist Album"), CancellationToken.None);

        Assert.True(Guid.TryParse(id, out _));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
    }

    [Fact]
    public async Task AddUsesAFreshBatchAfterAStaleBatchConflict() {
        var locator = SoulseekLocator.Encode(new SoulseekReleaseLocator(
            Guid.Parse("11111111-1111-1111-1111-111111111111"), "peer",
            [new SoulseekFileLocator("Music\\Album\\01.flac", 100)]));
        var handler = new StatusHandler([
            (HttpStatusCode.Conflict, "duplicate"),
            (HttpStatusCode.OK, """{"id":"33333333-3333-3333-3333-333333333333","username":"peer","transfers":[]}"""),
            (HttpStatusCode.Created, """{"batch":{"id":"55555555-5555-5555-5555-555555555555","username":"peer","transfers":[]},"failures":[]}""")
        ]);
        var client = new SlskdDownloadClient(new HttpClient(handler));

        var id = await client.AddAsync(Connection, new DownloadAddRequest(locator, null, "prismedia", "Artist Album"), CancellationToken.None);

        Assert.Equal("55555555-5555-5555-5555-555555555555", id);
        Assert.Equal([HttpMethod.Post, HttpMethod.Get, HttpMethod.Post], handler.Requests.Select(request => request.Method));
    }

    private sealed class Handler(Func<HttpRequestMessage, string> response) : HttpMessageHandler {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.Created) {
                Content = new StringContent(response(request), Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class StatusHandler(IReadOnlyList<(HttpStatusCode Status, string Body)> responses) : HttpMessageHandler {
        private int index;
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add(request);
            var response = responses[index++];
            return Task.FromResult(new HttpResponseMessage(response.Status) {
                Content = new StringContent(response.Body, Encoding.UTF8, "application/json")
            });
        }
    }
}
