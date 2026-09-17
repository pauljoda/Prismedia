using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Plugins;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Infrastructure.Tests;

public sealed class IntegrationManifestTests : IDisposable {
    [Theory]
    [InlineData("[\"http://files.test\"]")]
    [InlineData("[\"https://files.test/path\"]")]
    [InlineData("[\"https://user@files.test\"]")]
    [InlineData("[\"https://files.test?query\"]")]
    [InlineData("[\"https://files.test#fragment\"]")]
    [InlineData("[\"https://files.test?\"]")]
    [InlineData("[\" https://files.test\"]")]
    [InlineData("[\"https://files.test/..\"]")]
    [InlineData("[\"https:files.test\"]")]
    [InlineData("[null]")]
    [InlineData("[\"https://files.test\",\"https://FILES.test:443/\"]")]
    public void InvalidAnonymousArtifactOriginsAreRejected(string origins) {
        var json = ManifestJson.Replace("catalog-discovery", "acquisition-source").Replace("search", "resolve")
            .Replace("\"settings\": []", "\"settings\": [], \"anonymousArtifactOrigins\": " + origins);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, Wire)!;
        Assert.False(PluginCompatibilityResolver.IsCompatible(manifest, new Version(3, 8, 0)));
    }

    [Fact]
    public void AnonymousOriginsRoundTripAndRequireAnAcquisitionCapability() {
        var json = ManifestJson.Replace("\"settings\": []", "\"settings\": [], \"anonymousArtifactOrigins\": [\"https://files.test\"]");
        Assert.False(PluginCompatibilityResolver.IsCompatible(JsonSerializer.Deserialize<PluginManifest>(json, Wire)!, new Version(3, 8, 0)));
        json = json.Replace("catalog-discovery", "acquisition-source").Replace("search", "resolve");
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, Wire)!;
        Assert.True(PluginCompatibilityResolver.IsCompatible(manifest, new Version(3, 8, 0)));
        Assert.Equal("https://files.test", Assert.Single(manifest.Integration!.AnonymousArtifactOrigins!));
        var index = "[" + json.Replace("\"entry\": \"Fixture.dll\"", "\"path\": \"plugins/fixture.zip\"") + "]";
        Assert.Equal("https://files.test", Assert.Single(Assert.Single(PluginIndexParser.Parse(index, "index.yml")).Integration!.AnonymousArtifactOrigins!));
        manifest = manifest with { Integration = manifest.Integration with { AnonymousArtifactOrigins = Enumerable.Range(0, 9).Select(index => $"https://files{index}.test").ToArray() } };
        Assert.False(PluginCompatibilityResolver.IsCompatible(manifest, new Version(3, 8, 0)));
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"prismedia-integration-manifest-{Guid.NewGuid():N}");
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web) {
        Converters = { new CodecJsonConverterFactory() }
    };

    [Fact]
    public async Task IntegrationOnlyPackageIsDiscoverableWithoutPretendingToIdentifyMetadata() {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "manifest.json"), ManifestJson);
        await using var db = new PrismediaDbContext(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var catalog = new PluginCatalogService(ProviderCredentialTestStore.Create(db), db, new PluginCatalogOptions([_root], _root, "3.8.0"), new HttpClient(new EmptyIndexHandler()));

        var provider = Assert.Single(await catalog.ListProvidersAsync(CancellationToken.None));

        Assert.Empty(provider.Supports);
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(provider, Wire));
        Assert.Equal(1, result.RootElement.GetProperty("integration").GetProperty("protocolVersion").GetInt32());
    }

    [Theory]
    [InlineData("\"protocolVersion\": 1", "\"protocolVersion\": 9")]
    [InlineData("catalog-discovery", "invented-capability")]
    [InlineData("\"search\"", "\"submit\"")]
    [InlineData("\"book\"", "\"invented-kind\"")]
    [InlineData("\"search\"", "\"search\", \"search\"")]
    public void InvalidIntegrationDeclarationsCannotHideBesideValidMetadata(string from, string to) {
        var json = ManifestJson.Replace("\"supports\": []", "\"supports\": [{\"entityKind\":\"book\",\"actions\":[\"lookup-id\"],\"identityNamespaces\":[\"fixture\"]}]")
            .Replace(from, to);
        var compatible = false;
        try {
            var manifest = JsonSerializer.Deserialize<PluginManifest>(json, Wire)!;
            compatible = PluginCompatibilityResolver.IsCompatible(manifest, new Version(3, 8, 0));
        } catch (JsonException) { }
        catch (ArgumentOutOfRangeException) { }
        Assert.False(compatible);
    }

    [Fact]
    public void IntegrationIndexDeclarationsRoundTripInJsonAndYaml() {
        var index = "[" + ManifestJson.Replace("\"entry\": \"Fixture.dll\"", "\"path\": \"plugins/fixture.zip\"") + "]";
        var json = Assert.Single(PluginIndexParser.Parse(index, "index.json"));
        // JSON is valid YAML, including flow mappings and nested capability sequences.
        var yaml = Assert.Single(PluginIndexParser.Parse(index, "index.yml"));
        Assert.NotNull(json.Integration);
        Assert.Equal(JsonSerializer.Serialize(json, Wire), JsonSerializer.Serialize(yaml, Wire));
        Assert.True(PluginCompatibilityResolver.IsCompatible(json, new Version(3, 8, 0)));
        var mixed = "[" + ManifestJson.Replace("catalog-discovery", "future-capability") + "," + index[1..^1] + "]";
        Assert.Single(PluginIndexParser.Parse(mixed, "index.json"));
    }

    private const string ManifestJson = """
        {
          "manifestVersion": 2, "apiTags": ["prismedia"],
          "id": "fixture", "name": "Fixture catalog", "version": "1.0.0",
          "runtime": "dotnet-process", "entry": "Fixture.dll",
          "compat": { "pluginApiMin": "2.0.0", "prismediaMin": "3.8.0" },
          "auth": [], "supports": [],
          "integration": {
            "protocolVersion": 1,
            "capabilities": [{"kind": "catalog-discovery", "operations": ["search"], "entityKinds": ["book"]}],
            "settings": []
          }
        }
        """;

    private sealed class EmptyIndexHandler : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("[]") });
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
