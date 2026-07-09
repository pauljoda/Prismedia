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

    [Fact]
    public void ExistingProtocolOneManifestWithoutMaximumRemainsCompatible() {
        var compatibility = new PluginCompatibility("1.0.0", null, "1.0.0", null);

        Assert.True(PluginCompatibilityResolver.IsCompatible(
            Entry("tmdb", "1.0.0", ["prismedia"], "dotnet-process", "1.0.0", null, compatibility),
            new Version(1, 0, 0)));
        Assert.True(PluginCompatibilityResolver.IsCompatible(
            Manifest(compatibility),
            new Version(1, 0, 0)));
    }

    [Theory]
    [InlineData("3.0.0", null)]
    [InlineData("1.0.0", "1.9.9")]
    public void RejectsProtocolBoundsThatExcludeTheCurrentVersion(string pluginApiMin, string? pluginApiMax) {
        var compatibility = new PluginCompatibility(pluginApiMin, pluginApiMax, "1.0.0", null);

        Assert.False(PluginCompatibilityResolver.IsCompatible(
            Entry("tmdb", "1.0.0", ["prismedia"], "dotnet-process", "1.0.0", null, compatibility),
            new Version(1, 0, 0)));
        Assert.False(PluginCompatibilityResolver.IsCompatible(
            Manifest(compatibility),
            new Version(1, 0, 0)));
    }

    [Fact]
    public void AcceptsProtocolRangeContainingTheCurrentVersion() {
        var compatibility = new PluginCompatibility(
            "1.0.0",
            PluginProtocol.CurrentSemanticVersion,
            "1.0.0",
            null);

        Assert.True(PluginCompatibilityResolver.IsCompatible(
            Entry("tmdb", "1.0.0", ["prismedia"], "dotnet-process", "1.0.0", null, compatibility),
            new Version(1, 0, 0)));
        Assert.True(PluginCompatibilityResolver.IsCompatible(
            Manifest(compatibility),
            new Version(1, 0, 0)));
    }

    [Theory]
    [InlineData("not-a-version", null)]
    [InlineData("1.0.0", "not-a-version")]
    public void RejectsMalformedProtocolBounds(string pluginApiMin, string? pluginApiMax) {
        var compatibility = new PluginCompatibility(pluginApiMin, pluginApiMax, "1.0.0", null);

        Assert.False(PluginCompatibilityResolver.IsCompatible(
            Entry("tmdb", "1.0.0", ["prismedia"], "dotnet-process", "1.0.0", null, compatibility),
            new Version(1, 0, 0)));
        Assert.False(PluginCompatibilityResolver.IsCompatible(
            Manifest(compatibility),
            new Version(1, 0, 0)));
    }

    [Fact]
    public void IntegerAndSemanticProtocolVersionsDescribeTheSameCurrentVersion() =>
        Assert.Equal(
            PluginProtocol.CurrentVersion,
            Version.Parse(PluginProtocol.CurrentSemanticVersion).Major);

    private static PluginIndexEntry Entry(
        string id,
        string version,
        string[] apiTags,
        string runtime,
        string appMin,
        string? appMax,
        PluginCompatibility? compatibility = null) =>
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
            Compat: compatibility ?? new PluginCompatibility("1.0.0", null, appMin, appMax),
            Supports: []);

    private static PluginManifest Manifest(PluginCompatibility compatibility) =>
        new(
            ManifestVersion: 1,
            ApiTags: ["prismedia"],
            Id: "tmdb",
            Name: "TMDB",
            Version: "1.0.0",
            Runtime: "dotnet-process",
            Entry: "tmdb.dll",
            Compat: compatibility,
            Auth: [],
            IsNsfw: false,
            Supports: []);
}
