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
    private readonly WebApplicationFactory<Program> _factory;

    public UpdateCheckEndpointTests(WebApplicationFactory<Program> factory) {
        _factory = factory;
    }

    [Fact]
    public async Task UpdateCheckEndpointReturnsNonBlockingStatus() {
        using var factory = CreateFactory(new UpdateCheckResponse(
            "current",
            "1.0.0",
            "1.0.0",
            "https://github.com/pauljoda/Prismedia/releases/tag/v1.0.0",
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
        Assert.False(payload.UpdateAvailable);
        Assert.Equal("1.0.0", payload.LocalVersion);
    }

    [Fact]
    public async Task UpdateCheckEndpointPassesForceFlagToService() {
        var service = new FakeUpdateCheckService(new UpdateCheckResponse(
            "available",
            "1.0.0",
            "1.1.0",
            "https://github.com/pauljoda/Prismedia/releases/tag/v1.1.0",
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
    public async Task UpdateCheckServiceReportsAvailableReleaseFromConfiguredGitHubRepo() {
        using var handler = new StubHttpMessageHandler(request => {
            Assert.Equal("https://api.github.com/repos/pauljoda/Prismedia/releases/latest", request.RequestUri?.ToString());
            return JsonResponse("""
                {
                  "tag_name": "v1.1.0",
                  "html_url": "https://github.com/pauljoda/Prismedia/releases/tag/v1.1.0"
                }
                """);
        });
        var service = CreateGitHubService(handler, new Dictionary<string, string?> {
            ["PRISMEDIA_VERSION"] = "1.0.0",
        });

        var result = await service.CheckAsync(force: false, CancellationToken.None);

        Assert.Equal("available", result.Status);
        Assert.True(result.UpdateAvailable);
        Assert.Equal("1.1.0", result.LatestVersion);
        Assert.Equal("https://github.com/pauljoda/Prismedia/releases/tag/v1.1.0", result.LatestUrl);
        Assert.False(result.FromCache);
    }

    [Fact]
    public async Task UpdateCheckServiceCachesSuccessfulResultsUntilForced() {
        var calls = 0;
        using var handler = new StubHttpMessageHandler(_ => {
            calls++;
            return JsonResponse("""
                {
                  "tag_name": "v1.0.0",
                  "html_url": "https://github.com/pauljoda/Prismedia/releases/tag/v1.0.0"
                }
                """);
        });
        var service = CreateGitHubService(handler, new Dictionary<string, string?> {
            ["PRISMEDIA_VERSION"] = "1.0.0",
        });

        var first = await service.CheckAsync(force: false, CancellationToken.None);
        var second = await service.CheckAsync(force: false, CancellationToken.None);
        var third = await service.CheckAsync(force: true, CancellationToken.None);

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.False(third.FromCache);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("v1.1.0", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.0+local", 0)]
    [InlineData("1.0.0", "1.1.0-dev", -1)]
    public void UpdateCheckVersionComparisonHandlesReleaseTagsAndLocalSuffixes(
        string latest,
        string local,
        int expectedSign) {
        var comparison = GitHubReleaseUpdateCheckService.CompareVersions(latest, local);

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

    private static GitHubReleaseUpdateCheckService CreateGitHubService(
        HttpMessageHandler handler,
        IReadOnlyDictionary<string, string?> settings) {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        return new GitHubReleaseUpdateCheckService(
            new StubHttpClientFactory(handler),
            configuration,
            new StubWebHostEnvironment(Directory.GetCurrentDirectory()),
            NullLogger<GitHubReleaseUpdateCheckService>.Instance);
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
