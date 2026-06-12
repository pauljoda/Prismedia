using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Api.Endpoints;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Tests;

public sealed class UpdateCheckEndpointTests : IClassFixture<WebApplicationFactory<Program>> {
    private const string TagsUrl = "https://ghcr.io/v2/pauljoda/prismedia/tags/list?n=200";
    private readonly WebApplicationFactory<Program> _factory;

    public UpdateCheckEndpointTests(WebApplicationFactory<Program> factory) {
        _factory = factory;
    }

    [Fact]
    public async Task UpdateCheckEndpointReturnsNonBlockingStatus() {
        using var factory = CreateFactory(new UpdateCheckResponse(
            "current",
            "release",
            "1.0.0",
            "1.0.0",
            "https://github.com/pauljoda/Prismedia/pkgs/container/prismedia",
            false,
            DateTimeOffset.UtcNow,
            false,
            null));
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync("/api/update-check");
        var payload = await response.Content.ReadFromJsonAsync<UpdateCheckResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("current", payload.Status);
        Assert.Equal("release", payload.Channel);
        Assert.False(payload.UpdateAvailable);
        Assert.Equal("1.0.0", payload.LocalVersion);
    }

    [Fact]
    public async Task UpdateCheckEndpointPassesForceFlagToService() {
        var service = new FakeUpdateCheckService(new UpdateCheckResponse(
            "available",
            "alpha",
            "1.0.0",
            "1.1.0",
            "https://github.com/pauljoda/Prismedia/pkgs/container/prismedia",
            true,
            DateTimeOffset.UtcNow,
            false,
            null));
        using var factory = CreateFactory(service);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync("/api/update-check?force=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(service.LastForce);
    }

    [Fact]
    public async Task ChangelogEndpointReturnsConfiguredMarkdownFile() {
        var tempDir = Directory.CreateTempSubdirectory("prismedia-changelog-test-");
        try {
            var changelogPath = Path.Combine(tempDir.FullName, "CHANGELOG.md");
            await File.WriteAllTextAsync(changelogPath, "# Changelog\n\n## [Unreleased]\n- Local test entry\n");
            using var factory = _factory.WithWebHostBuilder(builder => {
                    builder.ConfigureAppConfiguration((_, config) => {
                        config.AddInMemoryCollection(new Dictionary<string, string?> {
                            ["CHANGELOG_PATH"] = changelogPath,
                        });
                    });
                })
                .WithTestAuth();
            using var client = factory.CreateAuthenticatedClient();

            using var response = await client.GetAsync("/api/changelog");
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Local test entry", content, StringComparison.Ordinal);
            Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);
        } finally {
            tempDir.Delete(recursive: true);
        }
    }

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

    private WebApplicationFactory<Program> CreateFactory(UpdateCheckResponse response) =>
        CreateFactory(new FakeUpdateCheckService(response));

    private WebApplicationFactory<Program> CreateFactory(FakeUpdateCheckService service) =>
        _factory.WithWebHostBuilder(builder => {
            builder.ConfigureServices(services => {
                services.RemoveAll<IUpdateCheckService>();
                services.AddSingleton<IUpdateCheckService>(service);
            });
        })
        .WithTestAuth();

    private static GhcrUpdateCheckService CreateGhcrService(
        HttpMessageHandler handler,
        IReadOnlyDictionary<string, string?> settings) {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        return new GhcrUpdateCheckService(
            new StubHttpClientFactory(handler),
            configuration,
            new StubWebHostEnvironment(Directory.GetCurrentDirectory()),
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

    private sealed class FakeUpdateCheckService(UpdateCheckResponse response) : IUpdateCheckService {
        public bool LastForce { get; private set; }

        public Task<UpdateCheckResponse> CheckAsync(bool force, CancellationToken cancellationToken) {
            LastForce = force;
            return Task.FromResult(response);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class StubWebHostEnvironment(string contentRootPath) : IWebHostEnvironment {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Prismedia.Api.Tests";
        public string WebRootPath { get; set; } = contentRootPath;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
