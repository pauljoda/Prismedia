using System.Net;
using System.Text;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Pins the direct Torznab/Newznab clients against canned XML: torznab attribute extraction
/// (seeders / swarm-peers / infohash / magnet), size resolution across the three homes it can live
/// in, caps-driven category clamping with a liberal fallback, the in-band error envelope, and the
/// protocol each family stamps on its releases.
/// </summary>
public sealed class NewznabIndexerClientsTests {
    private const string TorznabFeed =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:torznab="http://torznab.com/schemas/2015/feed">
          <channel>
            <item>
              <title>My.Book.2024.Retail.EPUB-GRP</title>
              <guid>https://tracker.example/details/42</guid>
              <comments>https://tracker.example/details/42#comments</comments>
              <link>https://tracker.example/download/42.torrent</link>
              <pubDate>Thu, 02 Jul 2026 20:15:00 +0000</pubDate>
              <size>52428800</size>
              <torznab:attr name="seeders" value="12" />
              <torznab:attr name="peers" value="15" />
              <torznab:attr name="infohash" value="ABCDEF0123456789" />
              <torznab:attr name="magneturl" value="magnet:?xt=urn:btih:abcdef0123456789" />
            </item>
          </channel>
        </rss>
        """;

    private const string NewznabFeed =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:newznab="http://www.newznab.com/DTD/2010/feeds/attributes/">
          <channel>
            <item>
              <title>My.Show.S01E01.1080p.WEB.H264-GRP</title>
              <guid>https://indexer.example/details/99</guid>
              <link>https://indexer.example/getnzb/99.nzb&amp;i=1&amp;r=key</link>
              <pubDate>Fri, 03 Jul 2026 01:00:00 +0000</pubDate>
              <enclosure url="https://indexer.example/getnzb/99.nzb" length="1073741824" type="application/x-nzb" />
              <newznab:attr name="size" value="1073741824" />
            </item>
          </channel>
        </rss>
        """;

    private const string Caps =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <caps>
          <limits max="60" default="50"/>
          <categories>
            <category id="7000" name="Books">
              <subcat id="7020" name="EBook"/>
            </category>
            <category id="8000" name="Other"/>
          </categories>
        </caps>
        """;

    private static IndexerConnection Connection() =>
        new(Guid.NewGuid(), IndexerKind.Torznab, "https://tracker.example", "key", [7000, 5000, 8000]);

    [Fact]
    public async Task TorznabItemsMapAttributesAndTheTorrentProtocol() {
        var client = new TorznabIndexerClient(new HttpClient(new CannedHandler(uri => uri.Contains("t=caps") ? Caps : TorznabFeed)));

        var releases = await client.SearchAsync(Connection(), new IndexerQuery("book", [7000]), CancellationToken.None);

        var release = Assert.Single(releases);
        Assert.Equal("My.Book.2024.Retail.EPUB-GRP", release.Title);
        Assert.Equal(52_428_800, release.SizeBytes);
        Assert.Equal(12, release.Seeders);
        Assert.Equal(3, release.Peers); // swarm 15 minus 12 seeders
        Assert.Equal(DownloadProtocol.Torrent, release.Protocol);
        Assert.Equal("https://tracker.example/download/42.torrent", release.DownloadUrl);
        Assert.Equal("magnet:?xt=urn:btih:abcdef0123456789", release.MagnetUrl);
        Assert.Equal("ABCDEF0123456789", release.InfoHash);
        Assert.Equal("https://tracker.example/details/42#comments", release.InfoUrl);
        Assert.NotNull(release.PublishedAt);
    }

    [Fact]
    public async Task NewznabItemsUseTheEnclosureAndTheUsenetProtocol() {
        var client = new NewznabIndexerClient(new HttpClient(new CannedHandler(uri => uri.Contains("t=caps") ? Caps : NewznabFeed)));

        var releases = await client.SearchAsync(Connection(), new IndexerQuery("show", [5000]), CancellationToken.None);

        var release = Assert.Single(releases);
        Assert.Equal(DownloadProtocol.Usenet, release.Protocol);
        Assert.Equal("https://indexer.example/getnzb/99.nzb", release.DownloadUrl);
        Assert.Equal(1_073_741_824, release.SizeBytes);
        Assert.Null(release.Seeders);
    }

    [Fact]
    public async Task RequestedCategoriesClampToTheAdvertisedSet() {
        var handler = new CannedHandler(uri => uri.Contains("t=caps") ? Caps : TorznabFeed);
        var client = new TorznabIndexerClient(new HttpClient(handler));

        // 7000 and 8000 are advertised; 5000 is not and is dropped from the search request.
        await client.SearchAsync(Connection(), new IndexerQuery("book", [7000, 5000, 8000]), CancellationToken.None);

        var search = handler.Requests.Single(uri => uri.Contains("t=search"));
        Assert.Contains("7000", search);
        Assert.Contains("8000", search);
        Assert.DoesNotContain("5000", search);
        // The caps advertise max=60, clamping the page size below the default 100.
        Assert.Contains("limit=60", search);
    }

    [Fact]
    public async Task WhenNoRequestedCategoryIsAdvertisedTheRequestPassesThroughUnchanged() {
        var handler = new CannedHandler(uri => uri.Contains("t=caps") ? Caps : TorznabFeed);
        var client = new TorznabIndexerClient(new HttpClient(handler));

        await client.SearchAsync(Connection(), new IndexerQuery("show", [5000, 5040]), CancellationToken.None);

        var search = handler.Requests.Single(uri => uri.Contains("t=search"));
        Assert.Contains("5000", search);
        Assert.Contains("5040", search);
    }

    [Fact]
    public async Task TheNewznabErrorEnvelopeBecomesAReadableException() {
        var handler = new CannedHandler(_ => """<?xml version="1.0"?><error code="100" description="Incorrect user credentials"/>""");
        var client = new TorznabIndexerClient(new HttpClient(handler));

        var result = await client.TestAsync(Connection(), CancellationToken.None);

        Assert.False(result.Connected);
        Assert.Contains("Incorrect user credentials", result.Message);
    }

    /// <summary>Responds with canned XML chosen by the request URI; records the URIs seen.</summary>
    private sealed class CannedHandler(Func<string, string> bodyFor) : HttpMessageHandler {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var uri = request.RequestUri!.ToString();
            Requests.Add(uri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(bodyFor(uri), Encoding.UTF8, "application/xml")
            });
        }
    }
}
