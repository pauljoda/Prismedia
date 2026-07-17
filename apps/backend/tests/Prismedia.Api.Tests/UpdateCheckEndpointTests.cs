using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Infrastructure.Updates;

namespace Prismedia.Api.Tests;

public sealed class UpdateCheckEndpointTests {
    private const string TagsUrl = "https://ghcr.io/v2/pauljoda/prismedia/tags/list?n=200";

    [Fact]
    public async Task VersionedChannelReportsAvailableFromNewestChannelTag() {
        using var handler = new StubHttpMessageHandler(request => request.RequestUri?.AbsoluteUri switch {
            var uri when uri!.StartsWith("https://ghcr.io/token", StringComparison.Ordinal) => TokenResponse(),
            TagsUrl => TagsResponse("release-1.0.0", "release-1.1.0", "alpha-1.2.0", "dev", "sha-abc1234"),
            _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}"),
        });
        var service = CreateGhcrService(handler, new Dictionary<string, string?> {
            ["PRISMEDIA_CHANNEL"] = "release",
            ["PRISMEDIA_VERSION"] = "1.0.0",
            ["PRISMEDIA_COMMIT"] = "abc1234",
        });

        var result = await service.CheckAsync(force: false, CancellationToken.None);

        Assert.Equal("available", result.Status);
        Assert.Equal("release", result.Channel);
        Assert.True(result.UpdateAvailable);
        Assert.Equal("1.1.0", result.LatestVersion);
    }

    [Fact]
    public async Task VersionedChannelReportsCurrentWhenLatestMatchesLocal() {
        using var handler = new StubHttpMessageHandler(request => request.RequestUri?.AbsoluteUri switch {
            var uri when uri!.StartsWith("https://ghcr.io/token", StringComparison.Ordinal) => TokenResponse(),
            TagsUrl => TagsResponse("alpha-1.2.0", "alpha-1.1.0"),
            _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}"),
        });
        var service = CreateGhcrService(handler, new Dictionary<string, string?> {
            ["PRISMEDIA_CHANNEL"] = "alpha",
            ["PRISMEDIA_VERSION"] = "1.2.0",
            ["PRISMEDIA_COMMIT"] = "abc1234",
        });

        var result = await service.CheckAsync(force: false, CancellationToken.None);

        Assert.Equal("current", result.Status);
        Assert.False(result.UpdateAvailable);
        Assert.Equal("1.2.0", result.LatestVersion);
    }

    [Fact]
    public async Task VersionedChannelReportsUnknownWhenNoChannelImagesPublished() {
        using var handler = new StubHttpMessageHandler(request => request.RequestUri?.AbsoluteUri switch {
            var uri when uri!.StartsWith("https://ghcr.io/token", StringComparison.Ordinal) => TokenResponse(),
            TagsUrl => TagsResponse("release-1.0.0", "dev"),
            _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}"),
        });
        var service = CreateGhcrService(handler, new Dictionary<string, string?> {
            ["PRISMEDIA_CHANNEL"] = "beta",
            ["PRISMEDIA_VERSION"] = "1.0.0",
            ["PRISMEDIA_COMMIT"] = "abc1234",
        });

        var result = await service.CheckAsync(force: false, CancellationToken.None);

        Assert.Equal("unknown", result.Status);
        Assert.False(result.UpdateAvailable);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task DevChannelReportsAvailableWhenPublishedCommitDiffersFromLocal() {
        using var handler = new StubHttpMessageHandler(request =>
            DevImageResponder(request, publishedCommit: "new1234"));
        var service = CreateGhcrService(handler, new Dictionary<string, string?> {
            ["PRISMEDIA_CHANNEL"] = "dev",
            ["PRISMEDIA_VERSION"] = "1.0.0",
            ["PRISMEDIA_COMMIT"] = "abc1234",
        });

        var result = await service.CheckAsync(force: false, CancellationToken.None);

        Assert.Equal("available", result.Status);
        Assert.Equal("dev", result.Channel);
        Assert.True(result.UpdateAvailable);
        Assert.Null(result.LatestVersion);
        Assert.NotNull(result.LatestUrl);
    }

    [Fact]
    public async Task DevChannelReportsCurrentWhenPublishedCommitMatchesLocal() {
        using var handler = new StubHttpMessageHandler(request =>
            DevImageResponder(request, publishedCommit: "abc1234"));
        var service = CreateGhcrService(handler, new Dictionary<string, string?> {
            ["PRISMEDIA_CHANNEL"] = "dev",
            ["PRISMEDIA_VERSION"] = "1.0.0",
            ["PRISMEDIA_COMMIT"] = "abc1234",
        });

        var result = await service.CheckAsync(force: false, CancellationToken.None);

        Assert.Equal("current", result.Status);
        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public async Task DevChannelReportsUnknownWhenPublishedImageHasNoCommit() {
        using var handler = new StubHttpMessageHandler(request =>
            DevImageResponder(request, publishedCommit: null));
        var service = CreateGhcrService(handler, new Dictionary<string, string?> {
            ["PRISMEDIA_CHANNEL"] = "dev",
            ["PRISMEDIA_VERSION"] = "1.0.0",
            ["PRISMEDIA_COMMIT"] = "abc1234",
        });

        var result = await service.CheckAsync(force: false, CancellationToken.None);

        Assert.Equal("unknown", result.Status);
        Assert.False(result.UpdateAvailable);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task LocalBuildWithoutCommitReportsDevelopmentWithoutNetworkCalls() {
        var calls = 0;
        using var handler = new StubHttpMessageHandler(_ => {
            calls++;
            throw new InvalidOperationException("The registry must not be contacted for local builds.");
        });
        var service = CreateGhcrService(handler, new Dictionary<string, string?> {
            ["PRISMEDIA_CHANNEL"] = "dev",
            ["PRISMEDIA_VERSION"] = "1.0.0",
            // No PRISMEDIA_COMMIT -> local build.
        });

        var result = await service.CheckAsync(force: false, CancellationToken.None);

        Assert.Equal("development", result.Status);
        Assert.Equal("dev", result.Channel);
        Assert.False(result.UpdateAvailable);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task UpdateCheckServiceCachesSuccessfulResultsUntilForced() {
        var tagCalls = 0;
        using var handler = new StubHttpMessageHandler(request => {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (uri.StartsWith("https://ghcr.io/token", StringComparison.Ordinal)) return TokenResponse();
            if (uri == TagsUrl) {
                tagCalls++;
                return TagsResponse("release-1.0.0");
            }

            throw new InvalidOperationException($"Unexpected request: {uri}");
        });
        var service = CreateGhcrService(handler, new Dictionary<string, string?> {
            ["PRISMEDIA_CHANNEL"] = "release",
            ["PRISMEDIA_VERSION"] = "1.0.0",
            ["PRISMEDIA_COMMIT"] = "abc1234",
        });

        var first = await service.CheckAsync(force: false, CancellationToken.None);
        var second = await service.CheckAsync(force: false, CancellationToken.None);
        var third = await service.CheckAsync(force: true, CancellationToken.None);

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.False(third.FromCache);
        Assert.Equal(2, tagCalls);
    }

    [Theory]
    [InlineData("v1.1.0", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.0+local", 0)]
    [InlineData("1.0.0", "1.1.0-dev", -1)]
    public void UpdateCheckVersionComparisonHandlesReleaseTagsAndLocalSuffixes(
        string latest,
        string local,
        int expectedSign) {
        var comparison = GhcrUpdateCheckService.CompareVersions(latest, local);

        Assert.NotNull(comparison);
        Assert.Equal(expectedSign, Math.Sign(comparison.Value));
    }

    private static GhcrUpdateCheckService CreateGhcrService(
        HttpMessageHandler handler,
        IReadOnlyDictionary<string, string?> settings) {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        return new GhcrUpdateCheckService(
            new StubHttpClientFactory(handler),
            configuration,
            new UpdateCheckOptions(Directory.GetCurrentDirectory()),
            NullLogger<GhcrUpdateCheckService>.Instance);
    }

    private static HttpResponseMessage TokenResponse() =>
        JsonResponse("""{"token":"test-token"}""");

    private static HttpResponseMessage TagsResponse(params string[] tags) {
        var quoted = string.Join(",", tags.Select(tag => $"\"{tag}\""));
        return JsonResponse($$"""{"name":"pauljoda/prismedia","tags":[{{quoted}}]}""");
    }

    /// <summary>
    /// Stubs the GHCR hops the dev check walks, mirroring a buildx push: the <c>dev</c> tag is an
    /// OCI index whose first entry is an attestation manifest (architecture "unknown") and whose
    /// platform entry leads to an image manifest, then a config blob carrying the build commit env.
    /// </summary>
    private static HttpResponseMessage DevImageResponder(HttpRequestMessage request, string? publishedCommit) {
        var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
        if (uri.StartsWith("https://ghcr.io/token", StringComparison.Ordinal)) return TokenResponse();
        if (uri.EndsWith("/manifests/dev", StringComparison.Ordinal)) {
            return JsonResponse("""
                {
                  "schemaVersion": 2,
                  "mediaType": "application/vnd.oci.image.index.v1+json",
                  "manifests": [
                    {"digest": "sha256:attestation", "platform": {"architecture": "unknown", "os": "unknown"}},
                    {"digest": "sha256:platform", "platform": {"architecture": "amd64", "os": "linux"}}
                  ]
                }
                """);
        }

        if (uri.EndsWith("/manifests/sha256:platform", StringComparison.Ordinal)) {
            return JsonResponse("""{"schemaVersion": 2, "config": {"digest": "sha256:config"}}""");
        }

        if (uri.EndsWith("/blobs/sha256:config", StringComparison.Ordinal)) {
            var env = publishedCommit is null
                ? """["PRISMEDIA_CHANNEL=dev"]"""
                : $$"""["PRISMEDIA_CHANNEL=dev", "PRISMEDIA_COMMIT={{publishedCommit}}"]""";
            return JsonResponse($$"""{"config": {"Env": {{env}} } }""");
        }

        throw new InvalidOperationException($"Unexpected request: {uri}");
    }

    private static HttpResponseMessage JsonResponse(string content) =>
        new(HttpStatusCode.OK) {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

}
