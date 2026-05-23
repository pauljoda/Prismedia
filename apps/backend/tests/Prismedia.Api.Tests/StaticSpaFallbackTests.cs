using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Prismedia.Api.Tests;

public sealed class StaticSpaFallbackTests : IDisposable {
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), $"prismedia-static-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public StaticSpaFallbackTests() {
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<html><body>Prismedia Static Shell</body></html>");
        File.WriteAllText(Path.Combine(_webRoot, "asset.txt"), "asset-ok");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Prismedia:StaticWebRoot", _webRoot));
    }

    [Fact]
    public async Task ServesStaticFilesFromConfiguredWebRoot() {
        using var client = _factory.CreateClient();

        var text = await client.GetStringAsync("/asset.txt");

        Assert.Equal("asset-ok", text);
    }

    [Fact]
    public async Task FallsBackToIndexForClientSideRoutes() {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/videos/11111111-1111-1111-1111-111111111111");

        Assert.Contains("Prismedia Static Shell", html);
    }

    public void Dispose() {
        _factory.Dispose();
        if (Directory.Exists(_webRoot)) {
            Directory.Delete(_webRoot, recursive: true);
        }
    }
}
