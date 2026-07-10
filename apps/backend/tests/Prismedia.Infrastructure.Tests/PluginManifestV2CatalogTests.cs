using System.Net;
using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class PluginManifestV2CatalogTests : IDisposable {
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"prismedia-plugin-v2-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task CatalogNormalizesVersionOneSupportsForApiConsumers() {
        await WriteManifestAsync(
            "legacy",
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "legacy-plugin",
              "name": "Legacy Plugin",
              "version": "1.0.0",
              "runtime": "dotnet-process",
              "entry": "Legacy.dll",
              "compat": {
                "pluginApiMin": "1.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [],
              "supports": [
                { "entityKind": "book", "actions": ["lookup-id", "search"] }
              ]
            }
            """);
        await using var db = CreateContext();
        var catalog = new PluginCatalogService(db, new PluginCatalogOptions([_tempRoot], _tempRoot, "1.0.0"));

        var provider = Assert.Single(await catalog.ListProvidersAsync(CancellationToken.None));
        var support = Assert.Single(provider.Supports);

        Assert.Equal(["legacy-plugin"], support.IdentityNamespaces);
        var field = Assert.Single(support.Search!.Fields);
        Assert.Equal("title", field.Key);
        Assert.Equal("Title", field.Label);
        Assert.Equal(PluginSearchFieldType.Text, field.Type);
        Assert.True(field.Required);
    }

    [Fact]
    public async Task CatalogSurfacesVersionTwoIdentityAndSearchDeclarations() {
        await WriteManifestAsync(
            "modern",
            """
            {
              "manifestVersion": 2,
              "apiTags": ["prismedia"],
              "id": "media-db-plugin",
              "name": "Media DB",
              "version": "2.0.0",
              "runtime": "dotnet-process",
              "entry": "MediaDb.dll",
              "compat": {
                "pluginApiMin": "2.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [],
              "supports": [
                {
                  "entityKind": "video-series",
                  "actions": ["lookup-id", "search"],
                  "identityNamespaces": ["tmdb", "imdb"],
                  "identityUrls": [
                    {
                      "identityNamespace": "tmdb",
                      "valuePattern": "{id}",
                      "urlTemplate": "https://www.themoviedb.org/tv/{id}"
                    }
                  ],
                  "search": {
                    "fields": [
                      { "key": "seriesTitle", "label": "Series title", "type": "text", "required": true, "placeholder": "Title" },
                      { "key": "year", "label": "Year", "type": "year", "required": false, "help": "Original premiere year" }
                    ]
                  }
                }
              ]
            }
            """);
        await using var db = CreateContext();
        var catalog = new PluginCatalogService(db, new PluginCatalogOptions([_tempRoot], _tempRoot, "1.0.0"));

        var provider = Assert.Single(await catalog.ListProvidersAsync(CancellationToken.None));
        var support = Assert.Single(provider.Supports);

        Assert.Equal("media-db-plugin", provider.Id);
        Assert.Equal(["tmdb", "imdb"], support.IdentityNamespaces);
        var identityUrl = Assert.Single(support.IdentityUrls!);
        Assert.Equal("tmdb", identityUrl.IdentityNamespace);
        Assert.Equal("{id}", identityUrl.ValuePattern);
        Assert.Equal("https://www.themoviedb.org/tv/{id}", identityUrl.UrlTemplate);
        Assert.Collection(
            support.Search!.Fields,
            title => {
                Assert.Equal("seriesTitle", title.Key);
                Assert.Equal(PluginSearchFieldType.Text, title.Type);
                Assert.Equal("Title", title.Placeholder);
            },
            year => {
                Assert.Equal("year", year.Key);
                Assert.Equal(PluginSearchFieldType.Year, year.Type);
                Assert.Equal("Original premiere year", year.Help);
            });
    }

    [Fact]
    public async Task CatalogRejectsInvalidVersionTwoManifestRatherThanApplyingLegacyFallbacks() {
        await WriteManifestAsync(
            "invalid",
            """
            {
              "manifestVersion": 2,
              "apiTags": ["prismedia"],
              "id": "broken-plugin",
              "name": "Broken Plugin",
              "version": "2.0.0",
              "runtime": "dotnet-process",
              "entry": "Broken.dll",
              "compat": {
                "pluginApiMin": "2.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [],
              "supports": [
                {
                  "entityKind": "video",
                  "actions": ["search"],
                  "identityNamespaces": ["TMDB"]
                }
              ]
            }
            """);
        await using var db = CreateContext();
        var catalog = new PluginCatalogService(db, new PluginCatalogOptions([_tempRoot], _tempRoot, "1.0.0"));

        Assert.Empty(await catalog.ListProvidersAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CatalogSkipsVersionTwoManifestWithUnknownSearchFieldType() {
        await WriteManifestAsync(
            "unknown-field-type",
            """
            {
              "manifestVersion": 2,
              "apiTags": ["prismedia"],
              "id": "broken-plugin",
              "name": "Broken Plugin",
              "version": "2.0.0",
              "runtime": "dotnet-process",
              "entry": "Broken.dll",
              "compat": {
                "pluginApiMin": "2.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [],
              "supports": [{
                "entityKind": "video",
                "actions": ["search"],
                "identityNamespaces": ["tmdb"],
                "search": { "fields": [
                  { "key": "title", "label": "Title", "type": "date-range", "required": true }
                ] }
              }]
            }
            """);
        await using var db = CreateContext();
        var catalog = new PluginCatalogService(db, new PluginCatalogOptions([_tempRoot], _tempRoot, "1.0.0"));

        Assert.Empty(await catalog.ListProvidersAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CatalogNormalizesVersionOneRemoteSupportsForApiConsumers() {
        const string index = """
        {
          "plugins": [{
            "id": "remote-legacy", "name": "Remote Legacy", "version": "1.0.0", "date": "2026-07-09",
            "path": "plugins/remote-legacy.zip", "sha256": "abc", "runtime": "dotnet-process",
            "manifestVersion": 1, "apiTags": ["prismedia"],
            "compat": { "pluginApiMin": "1.0.0", "prismediaMin": "1.0.0" },
            "supports": [{ "entityKind": "book", "actions": ["search"] }]
          }]
        }
        """;
        await using var db = CreateContext();
        var catalog = new PluginCatalogService(
            db,
            new PluginCatalogOptions([], _tempRoot, "1.0.0", "https://plugins.example.test/index.json"),
            new HttpClient(new StaticIndexHandler(index)));

        var provider = Assert.Single(await catalog.ListProvidersAsync(CancellationToken.None));
        var support = Assert.Single(provider.Supports);

        Assert.Equal(["remote-legacy"], support.IdentityNamespaces);
        Assert.Equal("title", Assert.Single(support.Search!.Fields).Key);
    }

    [Fact]
    public void JsonAndYamlIndexesParseVersionTwoSupportSchemas() {
        const string json = """
        {
          "plugins": [{
            "id": "books", "name": "Books", "version": "2.0.0", "date": "2026-07-09",
            "path": "plugins/books.zip", "sha256": "abc", "runtime": "dotnet-process",
            "manifestVersion": 2, "apiTags": ["prismedia"],
            "compat": { "pluginApiMin": "2.0.0", "prismediaMin": "1.0.0" },
            "supports": [{
              "entityKind": "book", "actions": ["search"], "identityNamespaces": ["openlibrary", "isbn"],
              "identityUrls": [
                {
                  "identityNamespace": "openlibrary",
                  "valuePattern": "{workId}",
                  "urlTemplate": "https://openlibrary.org/works/{workId}"
                }
              ],
              "search": { "fields": [
                { "key": "title", "label": "Title", "type": "text", "required": true },
                { "key": "author", "label": "Author", "type": "text", "required": false, "help": "Optional author context" }
              ] }
            }]
          }]
        }
        """;
        const string yaml = """
        - id: books
          name: Books
          version: 2.0.0
          date: '2026-07-09'
          path: plugins/books.zip
          sha256: abc
          runtime: dotnet-process
          manifestVersion: 2
          apiTags:
            - prismedia
          compat:
            pluginApiMin: 2.0.0
            prismediaMin: 1.0.0
          supports:
            - entityKind: book
              actions:
                - search
              identityNamespaces:
                - openlibrary
                - isbn
              identityUrls:
                - identityNamespace: openlibrary
                  valuePattern: '{workId}'
                  urlTemplate: 'https://openlibrary.org/works/{workId}'
              search:
                fields:
                  - key: title
                    label: Title
                    type: text
                    required: true
                  - key: author
                    label: Author
                    type: text
                    required: false
                    help: Optional author context
        """;

        var jsonSupport = Assert.Single(Assert.Single(PluginIndexParser.Parse(json, "index.json")).Supports);
        var yamlSupport = Assert.Single(Assert.Single(PluginIndexParser.Parse(yaml, "index.yml")).Supports);

        Assert.Equal(jsonSupport.EntityKind, yamlSupport.EntityKind);
        Assert.Equal(jsonSupport.Actions, yamlSupport.Actions);
        Assert.Equal(jsonSupport.IdentityNamespaces, yamlSupport.IdentityNamespaces);
        Assert.Equal(jsonSupport.IdentityUrls, yamlSupport.IdentityUrls);
        Assert.Equal(
            jsonSupport.Search!.Fields.Select(field => (field.Key, field.Label, field.Type, field.Required, field.Placeholder, field.Help)),
            yamlSupport.Search!.Fields.Select(field => (field.Key, field.Label, field.Type, field.Required, field.Placeholder, field.Help)));
        Assert.Equal(["openlibrary", "isbn"], jsonSupport.IdentityNamespaces);
        Assert.Equal(2, jsonSupport.Search!.Fields.Count);
    }

    [Fact]
    public void VersionTwoIndexParsingPreservesMalformedDeclarationsForCompatibilityRejection() {
        const string index = """
        {
          "plugins": [{
            "id": "books", "name": "Books", "version": "2.0.0", "date": "2026-07-09",
            "path": "plugins/books.zip", "sha256": "abc", "runtime": "dotnet-process",
            "manifestVersion": 2, "apiTags": ["prismedia"],
            "compat": { "pluginApiMin": "2.0.0", "prismediaMin": "1.0.0" },
            "supports": [
              {
                "entityKind": "book", "actions": ["lookup-id"],
                "identityNamespaces": ["openlibrary"]
              },
              { "entityKind": "video", "actions": [], "identityNamespaces": ["tmdb"] }
            ]
          }]
        }
        """;

        var entry = Assert.Single(PluginIndexParser.Parse(index, "index.json"));

        Assert.Equal(2, entry.Supports.Count);
        Assert.False(PluginCompatibilityResolver.IsCompatible(entry, new Version(1, 0, 0)));
    }

    private async Task WriteManifestAsync(string directory, string manifest) {
        var pluginDirectory = Path.Combine(_tempRoot, directory);
        Directory.CreateDirectory(pluginDirectory);
        await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "manifest.json"), manifest);
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new PrismediaDbContext(options);
    }

    public void Dispose() {
        if (Directory.Exists(_tempRoot)) {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private sealed class StaticIndexHandler(string body) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(body)
            });
    }
}
