using System.Net;
using System.Text.Json;
using System.Web;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Exercises aggregate paging without re-querying providers whose result sets are exhausted.</summary>
public sealed class ProwlarrIndexerClientTests {
    [Fact]
    public async Task ProtocolScopeSkipsUnsupportedIndexersAndReusesDiscoveryAcrossQueryVariants() {
        var handler = new ProtocolHandler([
            new { id = 10, protocol = DownloadProtocol.Usenet.ToCode(), enable = true },
            new { id = 20, protocol = DownloadProtocol.Torrent.ToCode(), enable = true },
            new { id = 30, protocol = "future-protocol", enable = true },
            new { id = 40, protocol = DownloadProtocol.Usenet.ToCode(), enable = false }
        ]);
        var client = new ProwlarrIndexerClient(new HttpClient(handler));
        var connection = new IndexerConnection(Guid.NewGuid(), IndexerKind.Prowlarr, "http://indexer.test", null, []);
        var query = new IndexerQuery("Series 500", [], EntityKind.VideoEpisode) { Protocols = [DownloadProtocol.Usenet] };

        await client.SearchAsync(connection, query, CancellationToken.None);
        await client.SearchAsync(connection, query with { Text = "Series finale" }, CancellationToken.None);

        Assert.Single(handler.Requests, uri => uri.AbsolutePath.EndsWith(ProwlarrProtocol.IndexersEndpoint, StringComparison.Ordinal));
        var searches = handler.Requests.Where(uri => uri.AbsolutePath.EndsWith(ProwlarrProtocol.SearchEndpoint, StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, searches.Length);
        Assert.All(searches, uri => Assert.Equal("10,30", HttpUtility.ParseQueryString(uri.Query)[ProwlarrProtocol.IndexerIdsParam]));
    }

    [Fact]
    public async Task KnownEmptyProtocolScopeDoesNotBecomeAnUnrestrictedAggregateSearch() {
        var handler = new ProtocolHandler([new { id = 20, protocol = DownloadProtocol.Torrent.ToCode(), enable = true }]);
        var client = new ProwlarrIndexerClient(new HttpClient(handler));

        var releases = await client.SearchAsync(
            new IndexerConnection(Guid.NewGuid(), IndexerKind.Prowlarr, "http://indexer.test", null, []),
            new IndexerQuery("Series", [], EntityKind.VideoSeries) { Protocols = [DownloadProtocol.Usenet] }, CancellationToken.None);

        Assert.Empty(releases);
        Assert.Single(handler.Requests);
        Assert.EndsWith(ProwlarrProtocol.IndexersEndpoint, handler.Requests[0].AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnavailableProtocolDiscoveryFallsBackToTheAggregateWithoutHidingResults() {
        var handler = new ProtocolHandler([], failDiscovery: true);
        var client = new ProwlarrIndexerClient(new HttpClient(handler));

        var releases = await client.SearchAsync(
            new IndexerConnection(Guid.NewGuid(), IndexerKind.Prowlarr, "http://indexer.test", null, []),
            new IndexerQuery("Series", [], EntityKind.VideoSeries) { Protocols = [DownloadProtocol.Usenet] }, CancellationToken.None);

        Assert.Equal(2, releases.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(ProwlarrProtocol.IndexerIdsParam, handler.Requests[1].Query);
    }

    [Fact]
    public async Task ScopedPaginationCannotReintroduceUnsupportedProviders() {
        var handler = new PagesHandler(call => call switch {
            0 => [new { id = 10, protocol = DownloadProtocol.Usenet.ToCode(), enable = true },
                  new { id = 20, protocol = DownloadProtocol.Torrent.ToCode(), enable = true }],
            1 => Page(10, 0, 100).Concat(Page(20, 0, 100)).ToArray(),
            2 => Page(10, 100, 2),
            _ => throw new InvalidOperationException("The exhausted provider must stop pagination.")
        });
        var releases = await new ProwlarrIndexerClient(new HttpClient(handler)).SearchAsync(
            new IndexerConnection(Guid.NewGuid(), IndexerKind.Prowlarr, "http://indexer.test", null, []),
            new IndexerQuery("Series", [], EntityKind.VideoSeries) { Protocols = [DownloadProtocol.Usenet] }, CancellationToken.None);

        Assert.Equal(102, releases.Count);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests.Skip(1), uri => Assert.Equal("10", HttpUtility.ParseQueryString(uri.Query)[ProwlarrProtocol.IndexerIdsParam]));
        Assert.Equal("100", HttpUtility.ParseQueryString(handler.Requests[2].Query)[ProwlarrProtocol.OffsetParam]);
    }

    [Fact]
    public async Task CachedCatalogIsFilteredForEachCallersProtocols() {
        var handler = new ProtocolHandler([
            new { id = 10, protocol = DownloadProtocol.Usenet.ToCode(), enable = true },
            new { id = 20, protocol = DownloadProtocol.Torrent.ToCode(), enable = true }
        ]);
        var client = new ProwlarrIndexerClient(new HttpClient(handler));
        var connection = new IndexerConnection(Guid.NewGuid(), IndexerKind.Prowlarr, "http://indexer.test", null, []);
        var query = new IndexerQuery("Series", [], EntityKind.VideoSeries) { Protocols = [DownloadProtocol.Usenet] };

        await client.SearchAsync(connection, query, CancellationToken.None);
        await client.SearchAsync(connection, query with { Protocols = [DownloadProtocol.Torrent] }, CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("10", HttpUtility.ParseQueryString(handler.Requests[1].Query)[ProwlarrProtocol.IndexerIdsParam]);
        Assert.Equal("20", HttpUtility.ParseQueryString(handler.Requests[2].Query)[ProwlarrProtocol.IndexerIdsParam]);
    }

    [Fact]
    public async Task MalformedCatalogDoesNotSilentlyRemoveUnknownProviders() {
        var handler = new ProtocolHandler([
            new { id = 20, protocol = DownloadProtocol.Torrent.ToCode(), enable = true },
            new { protocol = DownloadProtocol.Usenet.ToCode(), enable = true }
        ]);
        var releases = await new ProwlarrIndexerClient(new HttpClient(handler)).SearchAsync(
            new IndexerConnection(Guid.NewGuid(), IndexerKind.Prowlarr, "http://indexer.test", null, []),
            new IndexerQuery("Series", [], EntityKind.VideoSeries) { Protocols = [DownloadProtocol.Usenet] }, CancellationToken.None);

        Assert.Equal(2, releases.Count);
        Assert.DoesNotContain(ProwlarrProtocol.IndexerIdsParam, handler.Requests[1].Query);
    }

    [Fact]
    public async Task CancellationDuringDiscoveryDoesNotFallBackToAnotherRequest() {
        using var cancellation = new CancellationTokenSource();
        var handler = new PagesHandler(_ => {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            return [];
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProwlarrIndexerClient(new HttpClient(handler)).SearchAsync(
            new IndexerConnection(Guid.NewGuid(), IndexerKind.Prowlarr, "http://indexer.test", null, []),
            new IndexerQuery("Series", [], EntityKind.VideoSeries) { Protocols = [DownloadProtocol.Usenet] }, cancellation.Token));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FullProviderPagesContinueWithoutRepeatingExhaustedProviders() {
        var handler = new PagesHandler(call => call switch {
            0 => Page(35, 0, 100).Concat(Page(10, 0, 2)).ToArray(),
            1 => Page(35, 100, 100),
            2 => Page(35, 200, 17),
            _ => throw new InvalidOperationException("An exhausted page must stop pagination.")
        });

        var releases = await SearchAsync(handler);

        Assert.Equal(219, releases.Count);
        Assert.Equal(3, handler.Requests.Count);
        Assert.DoesNotContain(ProwlarrProtocol.IndexerIdsParam, handler.Requests[0].Query);
        var second = HttpUtility.ParseQueryString(handler.Requests[1].Query);
        Assert.Equal("35", second[ProwlarrProtocol.IndexerIdsParam]);
        Assert.Equal("100", second[ProwlarrProtocol.OffsetParam]);
        Assert.Equal("200", HttpUtility.ParseQueryString(handler.Requests[2].Query)[ProwlarrProtocol.OffsetParam]);
    }

    [Fact]
    public async Task ProviderIgnoringOffsetsStopsWhenItRepeatsItsPage() {
        var handler = new PagesHandler(_ => Page(35, 0, 100));

        var releases = await SearchAsync(handler);

        Assert.Equal(100, releases.Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task PaginationHasABoundedPerProviderResultBudget() {
        var handler = new PagesHandler(call => Page(35, call * 100, 100));

        var releases = await SearchAsync(handler);

        Assert.Equal(1000, releases.Count);
        Assert.Equal(10, handler.Requests.Count);
    }

    [Fact]
    public async Task ShortFirstPageDoesNotMakeAnotherRequest() {
        var handler = new PagesHandler(_ => Page(35, 0, 3));

        Assert.Equal(3, (await SearchAsync(handler)).Count);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ContinuationFailurePreservesAlreadyRetrievedCandidates() {
        var handler = new PagesHandler(call => call == 0
            ? Page(35, 0, 100)
            : throw new HttpRequestException("The provider could not return its next page."));

        Assert.Equal(100, (await SearchAsync(handler)).Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task FirstPageFailureStillReportsAFailedSearch() {
        var handler = new PagesHandler(_ => throw new HttpRequestException("The provider is unreachable."));

        await Assert.ThrowsAsync<HttpRequestException>(() => SearchAsync(handler));
    }

    [Fact]
    public async Task CancellationDuringPaginationIsNotSwallowedAsAPartialSuccess() {
        using var cancellation = new CancellationTokenSource();
        var handler = new PagesHandler(call => {
            if (call == 0) {
                return Page(35, 0, 100);
            }
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            return [];
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SearchAsync(handler, cancellation.Token));
    }

    private static Task<IReadOnlyList<IndexerRelease>> SearchAsync(PagesHandler handler, CancellationToken cancellationToken = default) =>
        new ProwlarrIndexerClient(new HttpClient(handler)).SearchAsync(
            new IndexerConnection(Guid.NewGuid(), IndexerKind.Prowlarr, "http://indexer.test", null, []),
            new IndexerQuery("A television season", [], EntityKind.VideoSeason), cancellationToken);

    private static object[] Page(int indexerId, int offset, int count) =>
        Enumerable.Range(offset, count)
            .Select(number => (object)new {
                guid = $"release-{indexerId}-{number}", indexerId,
                title = $"A television season {number}", size = 1024,
                protocol = DownloadProtocol.Usenet.ToCode()
            }).ToArray();

    private sealed class ProtocolHandler(object[] catalog, bool failDiscovery = false) : HttpMessageHandler {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add(request.RequestUri!);
            var discovery = request.RequestUri!.AbsolutePath.EndsWith(ProwlarrProtocol.IndexersEndpoint, StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(discovery && failDiscovery ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) {
                Content = new StringContent(JsonSerializer.Serialize(discovery ? catalog : Page(10, 0, 2)))
            });
        }
    }

    private sealed class PagesHandler(Func<int, object[]> page) : HttpMessageHandler {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(JsonSerializer.Serialize(page(Requests.Count - 1)))
            });
        }
    }
}
