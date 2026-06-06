using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Prismedia.Api.Tests;

public sealed class StaticSpaFallbackTests : IDisposable {
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), $"prismedia-static-{Guid.NewGuid():N}");
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), $"prismedia-cache-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public StaticSpaFallbackTests() {
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<html><body>Prismedia Static Shell</body></html>");
        File.WriteAllText(Path.Combine(_webRoot, "asset.txt"), "asset-ok");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Prismedia:StaticWebRoot", _webRoot))
            .WithTestAuth();
    }

    [Fact]
    public async Task ServesStaticFilesFromConfiguredWebRoot() {
        using var client = _factory.CreateAuthenticatedClient();

        var text = await client.GetStringAsync("/asset.txt");

        Assert.Equal("asset-ok", text);
    }

    [Fact]
    public async Task FallsBackToIndexForClientSideRoutes() {
        using var client = _factory.CreateAuthenticatedClient();

        var html = await client.GetStringAsync("/videos/11111111-1111-1111-1111-111111111111");

        Assert.Contains("Prismedia Static Shell", html);
    }

    [Fact]
    public async Task JellyfinRouteMissesReturnJsonNotSpaHtml() {
        using var client = _factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync("/Items/Images/Primary?maxWidth=600&quality=90");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("jellyfin_route_not_found", body);
    }

    [Fact]
    public async Task ServesGeneratedAssetsWhenCacheDirectoryIsCreatedByStartup() {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Prismedia:CacheDir", _cacheRoot))
            .WithTestAuth();
        using var client = factory.CreateAuthenticatedClient();

        using var health = await client.GetAsync("/api/health");
        health.EnsureSuccessStatusCode();

        var thumbnailPath = Path.Combine(_cacheRoot, "videos", "example", "thumb.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
        await File.WriteAllBytesAsync(thumbnailPath, [(byte)0xff, (byte)0xd8, (byte)0xff, (byte)0xd9]);

        using var response = await client.GetAsync("/assets/videos/example/thumb.jpg");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.EnsureSuccessStatusCode();
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal([(byte)0xff, (byte)0xd8, (byte)0xff, (byte)0xd9], bytes);
    }

    public void Dispose() {
        _factory.Dispose();
        if (Directory.Exists(_webRoot)) {
            Directory.Delete(_webRoot, recursive: true);
        }
        if (Directory.Exists(_cacheRoot)) {
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }
}
