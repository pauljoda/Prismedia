using Prismedia.Contracts.Plugins;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class PluginManifestCompatibilityTests {
    [Fact]
    public void FiltersToDotnetPluginsCompatibleWithCurrentAppVersion() {
        var current = new Version(1, 0, 0);
        var candidates = new[]
        {
            Entry("tmdb", "0.9.0", ["v1"], "typescript", "0.1.0", null),
            Entry("tmdb", "1.0.0", ["prismedia"], "dotnet-process", "1.0.0", "1.0.0"),
            Entry("tmdb", "1.1.0", ["prismedia"], "dotnet-process", "1.0.0", null),
            Entry("other", "1.0.0", ["prismedia"], "dotnet-process", "1.0.0", null)
        };

        var result = PluginCompatibilityResolver.LatestCompatible(candidates, "tmdb", current);

        Assert.NotNull(result);
        Assert.Equal("1.1.0", result.Version);
    }

    private static PluginIndexEntry Entry(
        string id,
        string version,
        string[] apiTags,
        string runtime,
        string appMin,
        string? appMax) =>
        new(
            Id: id,
            Name: id,
            Version: version,
            Date: "2026-05-16",
            Path: $"plugins/{id}/{id}.zip",
            Sha256: "abc",
            Runtime: runtime,
            IsNsfw: false,
            ManifestVersion: 1,
            ApiTags: apiTags,
            Compat: new PluginCompatibility("1.0.0", null, appMin, appMax),
            Supports: []);
}
