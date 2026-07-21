using System.Net;
using System.Text;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

public sealed class SlskdIndexerClientTests {
    private static IndexerConnection Connection() =>
        new(Guid.NewGuid(), IndexerKind.Slskd, "http://slskd:5030", "secret", [3000]);

    [Fact]
    public async Task AlbumSearchGroupsAudioFilesByPeerDirectory() {
        var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/responses", StringComparison.Ordinal)
            ? """
              [{"username":"peer","hasFreeUploadSlot":true,"queueLength":0,"uploadSpeed":9000,"files":[
                {"filename":"Music\\Artist\\Album\\01 First.flac","size":100,"bitDepth":16,"sampleRate":44100},
                {"filename":"Music\\Artist\\Album\\02 Second.flac","size":200,"bitDepth":16,"sampleRate":44100},
                {"filename":"Music\\Artist\\Album\\cover.jpg","size":20}
              ]}]
              """
            : """{"id":"11111111-1111-1111-1111-111111111111","state":"Completed"}""");
        var client = new SlskdIndexerClient(new HttpClient(handler));

        var releases = await client.SearchAsync(
            Connection(),
            new IndexerQuery("Artist Album", [3000], EntityKind.AudioLibrary),
            CancellationToken.None);

        var release = Assert.Single(releases);
        Assert.Equal(DownloadProtocol.Soulseek, release.Protocol);
        Assert.Equal(300, release.SizeBytes);
        Assert.StartsWith("Artist Album", release.Title, StringComparison.Ordinal);
        Assert.StartsWith(SoulseekProtocol.LocatorPrefix, release.DownloadUrl, StringComparison.Ordinal);
        Assert.All(handler.Requests, request => Assert.Equal("secret", request.Headers.GetValues(SoulseekProtocol.ApiKeyHeader).Single()));
    }

    [Fact]
    public async Task TrackSearchPresentsEachMatchingAudioFileIndividually() {
        var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/responses", StringComparison.Ordinal)
            ? """[{"username":"peer","files":[{"filename":"Music\\Artist\\Album\\03 Wanted.flac","size":300},{"filename":"Music\\Artist\\Album\\04 Other.flac","size":400}]}]"""
            : """{"id":"22222222-2222-2222-2222-222222222222","state":"Completed"}""");
        var client = new SlskdIndexerClient(new HttpClient(handler));

        var releases = await client.SearchAsync(
            Connection(),
            new IndexerQuery("Artist Album Wanted", [3000], EntityKind.AudioTrack),
            CancellationToken.None);

        var release = Assert.Single(releases);
        Assert.Equal(300, release.SizeBytes);
        Assert.Contains("Wanted", release.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchWaitsForSlskdCompletionBeforeReadingResponses() {
        var handler = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/responses", StringComparison.Ordinal)
            ? """[{"username":"peer","files":[{"filename":"Music\\Artist\\Album\\01 First.flac","size":100}]}]"""
            : request.Method == HttpMethod.Post
                ? """{"id":"33333333-3333-3333-3333-333333333333","state":"InProgress"}"""
                : """{"id":"33333333-3333-3333-3333-333333333333","state":"Completed, TimedOut"}"""
        );
        var client = new SlskdIndexerClient(new HttpClient(handler));

        var releases = await client.SearchAsync(
            Connection(),
            new IndexerQuery("Artist Album", [3000], EntityKind.AudioLibrary),
            CancellationToken.None);

        Assert.Single(releases);
        Assert.Collection(
            handler.Requests,
            request => Assert.Equal(HttpMethod.Post, request.Method),
            request => {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.StartsWith("/api/v0/searches/", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                Assert.DoesNotContain("/responses", request.RequestUri.AbsolutePath, StringComparison.Ordinal);
            },
            request => Assert.EndsWith("/responses", request.RequestUri!.AbsolutePath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SoulseekSearchIsAudioOnly() {
        var handler = new Handler(_ => throw new InvalidOperationException("HTTP should not be called"));
        var client = new SlskdIndexerClient(new HttpClient(handler));

        var releases = await client.SearchAsync(Connection(), new IndexerQuery("Movie", [], EntityKind.Movie), CancellationToken.None);

        Assert.Empty(releases);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ConnectionTestRequiresALoggedInSoulseekSession() {
        var handler = new Handler(_ => """{"state":"Disconnecting","isConnected":false,"isLoggedIn":false}""");
        var client = new SlskdIndexerClient(new HttpClient(handler));

        var result = await client.TestAsync(Connection(), CancellationToken.None);

        Assert.False(result.Connected);
        Assert.Contains("Disconnecting", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Handler(Func<HttpRequestMessage, string> response) : HttpMessageHandler {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(response(request), Encoding.UTF8, "application/json")
            });
        }
    }
}
