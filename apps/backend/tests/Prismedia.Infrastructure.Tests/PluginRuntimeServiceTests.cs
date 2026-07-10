using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Processes;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Infrastructure.Tests;

public sealed class PluginRuntimeServiceTests : IDisposable {
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"prismedia-plugin-tests-{Guid.NewGuid():N}");

    [Fact]
    public void CatalogExtractionRejectsCaseVariantSiblingOnUnix() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        var destination = Path.Combine(_tempRoot, "plugins", "community", "tmdb", "1.2.0");
        var caseVariantSibling = Path.Combine(_tempRoot, "plugins", "community", "TMDB", "1.2.0", "payload.dll");

        Assert.False(PluginCatalogService.IsSafeExtractionPath(destination, caseVariantSibling));
    }

    [Fact]
    public async Task CatalogDiscoversOnlyCompatibleDotnetManifests() {
        var pluginDir = Path.Combine(_tempRoot, "tmdb");
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(
            Path.Combine(pluginDir, "manifest.json"),
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "tmdb",
              "name": "TMDB",
              "version": "1.2.0",
              "runtime": "dotnet-process",
              "entry": "Prismedia.Plugin.Tmdb.dll",
              "compat": {
                "pluginApiMin": "1.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [
                { "key": "apiKey", "label": "API key", "required": true, "url": "https://www.themoviedb.org/settings/api" }
              ],
              "supports": [
                { "entityKind": "video", "actions": ["lookup-id", "lookup-url", "search"] }
              ]
            }
            """);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "old"));
        await File.WriteAllTextAsync(
            Path.Combine(_tempRoot, "old", "manifest.json"),
            """{ "manifestVersion": 1, "apiTags": ["other"], "id": "old", "name": "Old", "version": "1.0.0", "runtime": "typescript" }""");

        await using var db = CreateContext();
        var catalog = new PluginCatalogService(db, new PluginCatalogOptions([_tempRoot], _tempRoot, "1.0.0"));

        var providers = await catalog.ListProvidersAsync(CancellationToken.None);

        var provider = Assert.Single(providers);
        Assert.Equal("tmdb", provider.Id);
        Assert.False(provider.Installed);
        Assert.Contains("apiKey", provider.MissingAuthKeys);
    }

    [Fact]
    public async Task CatalogUsesManifestCredentialKeysForAuthFields() {
        var pluginDir = Path.Combine(_tempRoot, "tmdb");
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(
            Path.Combine(pluginDir, "manifest.json"),
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "tmdb",
              "name": "TMDB",
              "version": "1.2.0",
              "runtime": "dotnet-process",
              "entry": "Prismedia.Plugin.Tmdb.dll",
              "compat": {
                "pluginApiMin": "1.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [
                { "key": "apiKey", "label": "API key", "required": true, "url": "https://www.themoviedb.org/settings/api" }
              ],
              "supports": [
                { "entityKind": "video", "actions": ["lookup-id", "lookup-url", "search"] }
              ]
            }
            """);
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var config = new ProviderConfigRow {
            Id = Guid.NewGuid(),
            ProviderCode = "tmdb",
            DisplayName = "TMDB",
            ProviderType = ProviderType.ExternalProcess,
            Enabled = true,
            SettingsJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ProviderConfigs.Add(config);
        db.ProviderCredentials.Add(new ProviderCredentialRow {
            Id = Guid.NewGuid(),
            ProviderConfigId = config.Id,
            CredentialKey = "apiKey",
            EncryptedValue = "stored-secret",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var catalog = new PluginCatalogService(db, new PluginCatalogOptions([_tempRoot], _tempRoot, "1.0.0"));

        var provider = Assert.Single(await catalog.ListProvidersAsync(CancellationToken.None));
        var auth = await catalog.GetAuthAsync((await catalog.FindProviderAsync("tmdb", "video", CancellationToken.None))!.Manifest, CancellationToken.None);

        Assert.True(provider.Installed);
        Assert.Empty(provider.MissingAuthKeys);
        Assert.Equal("stored-secret", auth["apiKey"]);
    }

    [Fact]
    public async Task CatalogListsAndPullsCompatibleRemotePlugins() {
        var archive = CreatePluginArchive(
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "tmdb",
              "name": "TMDB",
              "version": "1.2.0",
              "runtime": "dotnet-process",
              "entry": "Prismedia.Plugin.Tmdb.dll",
              "compat": {
                "pluginApiMin": "1.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [
                { "key": "apiKey", "label": "API key", "required": true, "url": "https://www.themoviedb.org/settings/api" }
              ],
              "supports": [
                { "entityKind": "video", "actions": ["lookup-id", "search"] }
              ]
            }
            """);
        var sha256 = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        var index = $$"""
        {
          "plugins": [
            {
              "id": "tmdb",
              "name": "TMDB",
              "version": "1.2.0",
              "date": "2026-05-28",
              "path": "plugins/tmdb.zip",
              "sha256": "{{sha256}}",
              "runtime": "dotnet-process",
              "isNsfw": false,
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "compat": {
                "pluginApiMin": "1.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "supports": [
                { "entityKind": "video", "actions": ["lookup-id", "search"] }
              ]
            }
          ]
        }
        """;
        var handler = new StaticHttpMessageHandler(new Dictionary<string, byte[]> {
            ["https://plugins.example.test/index.json"] = System.Text.Encoding.UTF8.GetBytes(index),
            ["https://plugins.example.test/plugins/tmdb.zip"] = archive
        });
        await using var db = CreateContext();
        var catalog = new PluginCatalogService(
            db,
            new PluginCatalogOptions([], _tempRoot, "1.0.0", "https://plugins.example.test/index.json"),
            new HttpClient(handler));

        var listed = Assert.Single(await catalog.ListProvidersAsync(CancellationToken.None));
        var installed = await catalog.InstallAsync("tmdb", CancellationToken.None);

        Assert.Equal("tmdb", listed.Id);
        Assert.False(listed.Installed);
        Assert.Equal("video", Assert.Single(listed.Supports).EntityKind);
        Assert.NotNull(installed);
        Assert.True(installed.Installed);
        Assert.True(installed.Enabled);
        Assert.Contains("apiKey", installed.MissingAuthKeys);
        Assert.Contains(handler.Requests, uri => uri.ToString() == "https://plugins.example.test/plugins/tmdb.zip");
        Assert.True(File.Exists(Path.Combine(_tempRoot, "plugins", "community", "tmdb", "1.2.0", "manifest.json")));
    }

    [Fact]
    public async Task CatalogReportsAndInstallsNewerRemotePluginUpdates() {
        var oldPluginDir = Path.Combine(_tempRoot, "plugins", "community", "tmdb", "1.1.0");
        Directory.CreateDirectory(oldPluginDir);
        await File.WriteAllTextAsync(
            Path.Combine(oldPluginDir, "manifest.json"),
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "tmdb",
              "name": "TMDB",
              "version": "1.1.0",
              "runtime": "dotnet-process",
              "entry": "Prismedia.Plugin.Tmdb.dll",
              "compat": {
                "pluginApiMin": "1.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [],
              "supports": [
                { "entityKind": "video", "actions": ["lookup-id", "search"] }
              ]
            }
            """);
        await File.WriteAllBytesAsync(Path.Combine(oldPluginDir, "Prismedia.Plugin.Tmdb.dll"), [0]);
        var archive = CreatePluginArchive(
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "tmdb",
              "name": "TMDB",
              "version": "1.2.0",
              "runtime": "dotnet-process",
              "entry": "Prismedia.Plugin.Tmdb.dll",
              "compat": {
                "pluginApiMin": "1.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [],
              "supports": [
                { "entityKind": "video", "actions": ["lookup-id", "search"] }
              ]
            }
            """);
        var sha256 = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        var index = $$"""
        {
          "plugins": [
            {
              "id": "tmdb",
              "name": "TMDB",
              "version": "1.2.0",
              "date": "2026-05-29",
              "path": "plugins/tmdb.zip",
              "sha256": "{{sha256}}",
              "runtime": "dotnet-process",
              "isNsfw": false,
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "compat": {
                "pluginApiMin": "1.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "supports": [
                { "entityKind": "video", "actions": ["lookup-id", "search"] }
              ]
            }
          ]
        }
        """;
        var handler = new StaticHttpMessageHandler(new Dictionary<string, byte[]> {
            ["https://plugins.example.test/index.json"] = System.Text.Encoding.UTF8.GetBytes(index),
            ["https://plugins.example.test/plugins/tmdb.zip"] = archive
        });
        await using var db = CreateContext();
        db.ProviderConfigs.Add(new ProviderConfigRow {
            Id = Guid.NewGuid(),
            ProviderCode = "tmdb",
            DisplayName = "TMDB",
            ProviderType = ProviderType.ExternalProcess,
            Enabled = true,
            SettingsJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var catalog = new PluginCatalogService(
            db,
            new PluginCatalogOptions([], _tempRoot, "1.0.0", "https://plugins.example.test/index.json"),
            new HttpClient(handler));

        var listed = Assert.Single(await catalog.ListProvidersAsync(CancellationToken.None));
        var updated = await catalog.UpdateAsync("tmdb", CancellationToken.None);

        Assert.Equal("1.1.0", listed.Version);
        Assert.True(listed.UpdateAvailable);
        Assert.Equal("1.2.0", listed.AvailableVersion);
        Assert.NotNull(updated);
        Assert.Equal("1.2.0", updated.Version);
        Assert.False(updated.UpdateAvailable);
        Assert.Null(updated.AvailableVersion);
        Assert.True(File.Exists(Path.Combine(_tempRoot, "plugins", "community", "tmdb", "1.2.0", "manifest.json")));
        Assert.Contains(handler.Requests, uri => uri.ToString() == "https://plugins.example.test/plugins/tmdb.zip");
    }

    [Fact]
    public async Task CatalogResolvesPrismediaPluginsYamlIndexFromRepositoryBaseUrl() {
            var index = """
        # Prismedia Community Plugins Index
        - id: tvdb
          name: The TVDB
          version: 1.0.0
          date: '2026-04-17'
          path: plugins/tvdb/tvdb.zip
          sha256: 168ac2e4daf2aa97fc99a62696f0eaec4f3de05217cd4bf59fb43d8183ad7a14
          runtime: dotnet-process
          isNsfw: false
          manifestVersion: 1
          apiTags:
            - prismedia
          compat:
            pluginApiMin: 1.0.0
            pluginApiMax: null
            prismediaMin: 1.0.0
            prismediaMax: null
          supports:
            - entityKind: video-series
              actions:
                - lookup-id
                - lookup-url
                - search
                - cascade
            - entityKind: video
              actions:
                - lookup-id
                - lookup-url
                - search
        - id: tmdb
          name: The Movie Database
          version: 1.1.0
          date: '2026-05-22'
          path: plugins/tmdb/tmdb.zip
          sha256: acf49f19ab05537fd784f802e32ee406ae906c8a2ec2befdb502aacedab98671
          runtime: dotnet-process
          isNsfw: false
          manifestVersion: 1
          apiTags:
            - prismedia
          compat:
            pluginApiMin: 1.0.0
            pluginApiMax: null
            prismediaMin: 1.0.0
            prismediaMax: null
          supports:
            - entityKind: video
              actions:
                - lookup-id
                - lookup-url
                - search
            - entityKind: video-series
              actions:
                - lookup-id
                - lookup-url
                - search
                - cascade
            - entityKind: person
              actions:
                - lookup-id
                - lookup-url
                - search
        """;
        var handler = new StaticHttpMessageHandler(new Dictionary<string, byte[]> {
            ["https://raw.githubusercontent.com/pauljoda/Prismedia-Plugins/main/index.yml"] =
                System.Text.Encoding.UTF8.GetBytes(index)
        });
        await using var db = CreateContext();
        var catalog = new PluginCatalogService(
            db,
            new PluginCatalogOptions(
                [],
                _tempRoot,
                "1.0.0",
                "https://raw.githubusercontent.com/pauljoda/Prismedia-Plugins/main"),
            new HttpClient(handler));

        var providers = await catalog.ListProvidersAsync(CancellationToken.None);

        Assert.Equal(2, providers.Count);
        // No cache-buster query: the index rides plain CDN-cacheable GETs now that results are memoized.
        Assert.Contains(handler.Requests,
            uri => uri.ToString() == "https://raw.githubusercontent.com/pauljoda/Prismedia-Plugins/main/index.yml");
        var tmdb = providers.Single(provider => provider.Id == "tmdb");
        Assert.Equal("The Movie Database", tmdb.Name);
        Assert.Equal("1.1.0", tmdb.Version);
        Assert.Contains(tmdb.Supports, support =>
            support.EntityKind == "video-series" && support.Actions.Contains("cascade"));
    }

    [Fact]
    public async Task CatalogDoesNotCrashWhenRemoteIndexRequestFailsInsideHttpClient() {
        var pluginDir = Path.Combine(_tempRoot, "local");
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(
            Path.Combine(pluginDir, "manifest.json"),
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "local",
              "name": "Local",
              "version": "1.0.0",
              "runtime": "dotnet-process",
              "entry": "Local.dll",
              "compat": {
                "pluginApiMin": "1.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [],
              "supports": [
                { "entityKind": "video", "actions": ["search"] }
              ]
            }
            """);
        await using var db = CreateContext();
        var catalog = new PluginCatalogService(
            db,
            new PluginCatalogOptions([_tempRoot], _tempRoot, "1.0.0", "https://plugins.example.test/index.yml"),
            new HttpClient(new ThrowingHttpMessageHandler(new NullReferenceException("simulated internal transport failure"))));

        var providers = await catalog.ListProvidersAsync(CancellationToken.None);

        var provider = Assert.Single(providers);
        Assert.Equal("local", provider.Id);
    }

    [Fact]
    public void CredentialResolverReadsCanonicalEnvironmentVariable() {
        const string envName = "PRISMEDIA_PLUGIN_TMDB_API_KEY";
        var previous = Environment.GetEnvironmentVariable(envName);
        try {
            Environment.SetEnvironmentVariable(envName, "env-secret");

            var value = PluginCredentialResolver.ResolveEnvironmentCredential("tmdb", "apiKey");

            Assert.Equal("env-secret", value);
            Assert.True(PluginCredentialResolver.HasEnvironmentCredential("tmdb", "apiKey"));
        } finally {
            Environment.SetEnvironmentVariable(envName, previous);
        }
    }

    [Fact]
    public async Task ProcessRunnerWritesRequestFileAndReadsStdoutResponse() {
        var executor = new CapturingProcessExecutor();
        var runner = new DotnetPluginProcessRunner(
            executor,
            new PluginCatalogOptions([], _tempRoot, "1.0.0"));
        var descriptor = new PluginDescriptor(
            Manifest: new PluginManifest(
                1,
                ["prismedia"],
                "tmdb",
                "TMDB",
                "1.0.0",
                "dotnet-process",
                "tmdb.dll",
                new PluginCompatibility("1.0.0", null, "1.0.0", null),
                [],
                false,
                []),
            ManifestPath: Path.Combine(_tempRoot, "manifest.json"),
            WorkingDirectory: _tempRoot,
            EntryPath: Path.Combine(_tempRoot, "tmdb.dll"));
        var entityId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var request = new IdentifyPluginRequest(
            1,
            IdentifyAction.LookupId,
            new Dictionary<string, string> { ["apiKey"] = "secret" },
            new IdentifyEntitySnapshot(entityId, EntityKind.Video, "Example"),
            new IdentifyQuery(
                null,
                null,
                null,
                Fields: new Dictionary<string, string> {
                    ["seriesTitle"] = "Example",
                    ["year"] = "2026"
                }),
            new IdentifyMatchHints(
                new Dictionary<string, string> { ["tmdb"] = "123" },
                [],
                "Example",
                "/media/example.mkv"));

        var response = await runner.IdentifyAsync(descriptor, request, CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal("tmdb", response.Result?.Provider);
        Assert.Equal("dotnet", executor.FileName);
        Assert.Equal(descriptor.EntryPath, executor.Arguments[0]);
        Assert.Equal(entityId, executor.CapturedRequest?.Entity.Id);
        Assert.Equal(IdentifyAction.LookupId, executor.CapturedRequest?.Action);
        Assert.Equal("Example", executor.CapturedRequest?.Query.Fields?["seriesTitle"]);
        Assert.Equal("2026", executor.CapturedRequest?.Query.Fields?["year"]);
    }

    [Fact]
    public async Task ProcessRunnerUsesProviderNameForNoMatchErrors() {
        var executor = new EmptyCandidateProcessExecutor();
        var runner = new DotnetPluginProcessRunner(
            executor,
            new PluginCatalogOptions([], _tempRoot, "1.0.0"));
        var descriptor = new PluginDescriptor(
            Manifest: new PluginManifest(
                1,
                ["prismedia"],
                "mangadex",
                "MangaDex",
                "1.0.0",
                "dotnet-process",
                "mangadex.dll",
                new PluginCompatibility("1.0.0", null, "1.0.0", null),
                [],
                false,
                []),
            ManifestPath: Path.Combine(_tempRoot, "manifest.json"),
            WorkingDirectory: _tempRoot,
            EntryPath: Path.Combine(_tempRoot, "mangadex.dll"));
        var request = new IdentifyPluginRequest(
            1,
            IdentifyAction.Search,
            new Dictionary<string, string>(),
            new IdentifyEntitySnapshot(Guid.NewGuid(), EntityKind.Book, "Missing"),
            new IdentifyQuery("Missing", null, null),
            new IdentifyMatchHints(new Dictionary<string, string>(), [], "Missing", null));

        var response = await runner.IdentifyAsync(descriptor, request, CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Null(response.Result);
        Assert.Equal("No MangaDex match was found.", response.Error);
    }

    [Fact]
    public async Task ProcessRunnerMapsInstalledPluginCandidateDescriptionAndThumbnail() {
        var executor = new CandidateMetadataProcessExecutor();
        var runner = new DotnetPluginProcessRunner(
            executor,
            new PluginCatalogOptions([], _tempRoot, "1.0.0"));
        var descriptor = new PluginDescriptor(
            Manifest: new PluginManifest(
                1,
                ["prismedia"],
                "tmdb",
                "TMDB",
                "1.0.0",
                "dotnet-process",
                "tmdb.dll",
                new PluginCompatibility("1.0.0", null, "1.0.0", null),
                [],
                false,
                []),
            ManifestPath: Path.Combine(_tempRoot, "manifest.json"),
            WorkingDirectory: _tempRoot,
            EntryPath: Path.Combine(_tempRoot, "tmdb.dll"));
        var request = new IdentifyPluginRequest(
            1,
            IdentifyAction.Search,
            new Dictionary<string, string>(),
            new IdentifyEntitySnapshot(Guid.NewGuid(), EntityKind.VideoSeries, "Abbott Elementary"),
            new IdentifyQuery("Abbott Elementary", null, null),
            new IdentifyMatchHints(new Dictionary<string, string>(), [], "Abbott Elementary", null));

        var response = await runner.IdentifyAsync(descriptor, request, CancellationToken.None);

        var candidate = Assert.Single(response.Result!.Candidates);
        Assert.Equal("Abbott Elementary", candidate.Title);
        Assert.Equal("A workplace comedy.", candidate.Overview);
        Assert.Equal("https://image.tmdb.org/t/p/w342/poster.jpg", candidate.PosterUrl);
        Assert.Equal("tmdb:tv:125935", candidate.CandidateId);
        Assert.Equal("TMDB", candidate.Source);
        Assert.Equal(1m, candidate.Confidence);
        Assert.Equal("title-search", candidate.MatchReason);
    }

    [Fact]
    public async Task IdentifyHidesNsfwEntityBeforeRunningProviderWhenRequested() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var entityId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = "video",
            Title = "Hidden Video",
            IsNsfw = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var executor = new CapturingProcessExecutor();
        var service = CreateIdentifyService(db, executor, _tempRoot);

        var error = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.IdentifyAsync(
                entityId,
                "tmdb",
                null,
                parentExternalIds: null,
                hideNsfw: true,
                CancellationToken.None));

        Assert.Equal($"Entity '{entityId}' was not found.", error.Message);
        Assert.DoesNotContain("Hidden Video", error.Message);
        Assert.Null(executor.CapturedRequest);
    }

    [Fact]
    public async Task IdentifySendsMovieAsVideoToVideoOnlyCompatibleProvider() {
        var pluginDir = Path.Combine(_tempRoot, "tmdb");
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(
            Path.Combine(pluginDir, "manifest.json"),
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "tmdb",
              "name": "TMDB",
              "version": "1.2.0",
              "runtime": "dotnet-process",
              "entry": "Prismedia.Plugin.Tmdb.dll",
              "compat": {
                "pluginApiMin": "1.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [
                { "key": "apiKey", "label": "API key", "required": true, "url": "https://www.themoviedb.org/settings/api" }
              ],
              "supports": [
                { "entityKind": "video", "actions": ["lookup-id", "lookup-url", "search"] }
              ]
            }
            """);

        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var providerConfig = new ProviderConfigRow {
            Id = Guid.NewGuid(),
            ProviderCode = "tmdb",
            DisplayName = "TMDB",
            ProviderType = ProviderType.ExternalProcess,
            Enabled = true,
            SettingsJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        };
        var movieId = Guid.Parse("99999999-aaaa-4444-8888-999999999999");
        db.ProviderConfigs.Add(providerConfig);
        db.ProviderCredentials.Add(new ProviderCredentialRow {
            Id = Guid.NewGuid(),
            ProviderConfigId = providerConfig.Id,
            CredentialKey = "apiKey",
            EncryptedValue = "secret",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Entities.Add(new EntityRow {
            Id = movieId,
            KindCode = EntityKindRegistry.Movie.Code,
            Title = "Friendship",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var executor = new CapturingProcessExecutor();
        var service = CreateIdentifyService(db, executor, pluginDir);

        var response = await service.IdentifyAsync(movieId, "tmdb", null, parentExternalIds: null, hideNsfw: false, CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal(EntityKind.Video, executor.CapturedRequest?.Entity.Kind);
        Assert.Equal(IdentifyAction.Search, executor.CapturedRequest?.Action);
        Assert.Equal(PluginProtocol.CurrentVersion, executor.CapturedRequest?.ProtocolVersion);
        Assert.Equal(ProposalKind.Movie, response.Result?.TargetKind);
        Assert.Equal(movieId, response.Result?.TargetEntityId);
    }

    [Fact]
    public async Task IdentifyPreservesProviderStructuralChildrenThatDoNotMapToLocalChildren() {
        var pluginDir = Path.Combine(_tempRoot, "tmdb");
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(
            Path.Combine(pluginDir, "manifest.json"),
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "tmdb",
              "name": "TMDB",
              "version": "1.2.0",
              "runtime": "dotnet-process",
              "entry": "Prismedia.Plugin.Tmdb.dll",
              "compat": {
                "pluginApiMin": "1.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [
                { "key": "apiKey", "label": "API key", "required": true, "url": "https://www.themoviedb.org/settings/api" }
              ],
              "supports": [
                { "entityKind": "video-series", "actions": ["search"] },
                { "entityKind": "video-season", "actions": ["search"] }
              ]
            }
            """);

        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var providerConfig = new ProviderConfigRow {
            Id = Guid.NewGuid(),
            ProviderCode = "tmdb",
            DisplayName = "TMDB",
            ProviderType = ProviderType.ExternalProcess,
            Enabled = true,
            SettingsJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        };
        var seriesId = Guid.Parse("11111111-aaaa-4444-8888-111111111111");
        db.ProviderConfigs.Add(providerConfig);
        db.ProviderCredentials.Add(new ProviderCredentialRow {
            Id = Guid.NewGuid(),
            ProviderConfigId = providerConfig.Id,
            CredentialKey = "apiKey",
            EncryptedValue = "secret",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Entities.Add(new EntityRow {
            Id = seriesId,
            KindCode = "video-series",
            Title = "Series Without Local Children",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var service = CreateIdentifyService(db, new ProviderChildTreeProcessExecutor(seriesId, null), pluginDir);

        var response = await service.IdentifyAsync(seriesId, "tmdb", null, parentExternalIds: null, hideNsfw: false, CancellationToken.None);

        Assert.True(response.Ok);
        var child = Assert.Single(response.Result!.Children);
        Assert.Null(child.TargetEntityId);
        Assert.Equal(ProposalKind.VideoSeason, child.TargetKind);
        Assert.Equal("Season 1", child.Patch.Title);
        Assert.Equal("Generated Studio", Assert.Single(response.Result.Relationships).Patch.Title);
    }

    [Fact]
    public async Task IdentifyBindsExistingChildrenFromProviderTreeWithoutFreshPluginCalls() {
        var pluginDir = Path.Combine(_tempRoot, "tmdb");
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(
            Path.Combine(pluginDir, "manifest.json"),
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "tmdb",
              "name": "TMDB",
              "version": "1.2.0",
              "runtime": "dotnet-process",
              "entry": "Prismedia.Plugin.Tmdb.dll",
              "compat": {
                "pluginApiMin": "1.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [
                { "key": "apiKey", "label": "API key", "required": true, "url": "https://www.themoviedb.org/settings/api" }
              ],
              "supports": [
                { "entityKind": "video-series", "actions": ["search"] },
                { "entityKind": "video-season", "actions": ["search"] }
              ]
            }
            """);

        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var providerConfig = new ProviderConfigRow {
            Id = Guid.NewGuid(),
            ProviderCode = "tmdb",
            DisplayName = "TMDB",
            ProviderType = ProviderType.ExternalProcess,
            Enabled = true,
            SettingsJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        };
        var seriesId = Guid.Parse("22222222-aaaa-4444-8888-222222222222");
        var seasonId = Guid.Parse("33333333-aaaa-4444-8888-333333333333");
        db.ProviderConfigs.Add(providerConfig);
        db.ProviderCredentials.Add(new ProviderCredentialRow {
            Id = Guid.NewGuid(),
            ProviderConfigId = providerConfig.Id,
            CredentialKey = "apiKey",
            EncryptedValue = "secret",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Entities.AddRange(
            new EntityRow { Id = seriesId, KindCode = "video-series", Title = "Unidentified Series", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = seasonId, KindCode = "video-season", Title = "Season 1", ParentEntityId = seriesId, SortOrder = 1, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        var executor = new ProviderChildTreeProcessExecutor(seriesId, seasonId);
        var service = CreateIdentifyService(db, executor, pluginDir);

        var response = await service.IdentifyAsync(seriesId, "tmdb", null, parentExternalIds: null, hideNsfw: false, CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal([seriesId], executor.Requests.Select(request => request.Entity.Id).ToArray());
        var child = Assert.Single(response.Result!.Children);
        Assert.Equal(seasonId, child.TargetEntityId);
        Assert.Equal("Season 1", child.Patch.Title);
        Assert.Equal("Generated Studio", Assert.Single(response.Result.Relationships).Patch.Title);
    }

    [Fact]
    public async Task IdentifyCascadesIntoLocalChildrenAbsentFromProviderTree() {
        // A container identify walks its local structural children even when the provider did not
        // return them in its own proposal (e.g. an artist's albums), identifying each with the
        // parent's context and merging it into the returned tree.
        var pluginDir = Path.Combine(_tempRoot, "musicbrainz");
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(
            Path.Combine(pluginDir, "manifest.json"),
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "musicbrainz",
              "name": "MusicBrainz",
              "version": "1.0.0",
              "runtime": "dotnet-process",
              "entry": "Prismedia.Plugin.MusicBrainz.dll",
              "compat": { "pluginApiMin": "1.0.0", "pluginApiMax": null, "prismediaMin": "1.0.0", "prismediaMax": null },
              "auth": [],
              "supports": [
                { "entityKind": "music-artist", "actions": ["search"] },
                { "entityKind": "audio-library", "actions": ["search"] }
              ]
            }
            """);

        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        db.ProviderConfigs.Add(new ProviderConfigRow {
            Id = Guid.NewGuid(),
            ProviderCode = "musicbrainz",
            DisplayName = "MusicBrainz",
            ProviderType = ProviderType.ExternalProcess,
            Enabled = true,
            SettingsJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        });
        var artistId = Guid.Parse("aaaa1111-aaaa-4444-8888-aaaaaaaaaaaa");
        var albumId = Guid.Parse("bbbb2222-aaaa-4444-8888-bbbbbbbbbbbb");
        db.Entities.AddRange(
            new EntityRow { Id = artistId, KindCode = "music-artist", Title = "Imagine Dragons", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = albumId, KindCode = "audio-library", Title = "Evolve", ParentEntityId = artistId, SortOrder = 0, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        var executor = new EchoProposalProcessExecutor();
        var service = CreateIdentifyService(db, executor, pluginDir);

        var response = await service.IdentifyAsync(artistId, "musicbrainz", null, parentExternalIds: null, hideNsfw: false, CancellationToken.None);

        Assert.True(response.Ok);
        // Both the artist and its album were identified, and the album is a child of the artist tree.
        Assert.Contains(artistId, executor.Requests.Select(request => request.Entity.Id));
        Assert.Contains(albumId, executor.Requests.Select(request => request.Entity.Id));
        Assert.Equal("Imagine Dragons", response.Result!.Patch.Title);
        var albumChild = Assert.Single(response.Result!.Children);
        Assert.Equal(albumId, albumChild.TargetEntityId);
        Assert.Equal("Evolve", albumChild.Patch.Title);
    }

    [Fact]
    public async Task IdentifyDoesNotCascadeIntoAMovieLeafChild() {
        // A movie is leaf content: its single playable video is the same work, so identifying the
        // movie must not surface (or identify) that video as a structural child.
        var pluginDir = Path.Combine(_tempRoot, "tmdb-movie");
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(
            Path.Combine(pluginDir, "manifest.json"),
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "tmdb",
              "name": "TMDB",
              "version": "1.0.0",
              "runtime": "dotnet-process",
              "entry": "Prismedia.Plugin.Tmdb.dll",
              "compat": { "pluginApiMin": "1.0.0", "pluginApiMax": null, "prismediaMin": "1.0.0", "prismediaMax": null },
              "auth": [],
              "supports": [
                { "entityKind": "movie", "actions": ["search"] },
                { "entityKind": "video", "actions": ["search"] }
              ]
            }
            """);

        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        db.ProviderConfigs.Add(new ProviderConfigRow {
            Id = Guid.NewGuid(),
            ProviderCode = "tmdb",
            DisplayName = "TMDB",
            ProviderType = ProviderType.ExternalProcess,
            Enabled = true,
            SettingsJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        });
        var movieId = Guid.Parse("dddd4444-aaaa-4444-8888-dddddddddddd");
        var videoId = Guid.Parse("eeee5555-aaaa-4444-8888-eeeeeeeeeeee");
        db.Entities.AddRange(
            new EntityRow { Id = movieId, KindCode = "movie", Title = "Friendship", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = videoId, KindCode = "video", Title = "Friendship", ParentEntityId = movieId, SortOrder = 0, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        var executor = new EchoProposalProcessExecutor();
        var service = CreateIdentifyService(db, executor, pluginDir);

        var response = await service.IdentifyAsync(movieId, "tmdb", null, parentExternalIds: null, hideNsfw: false, CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Empty(response.Result!.Children);
        Assert.Equal([movieId], executor.Requests.Select(request => request.Entity.Id).ToArray());
    }

    [Fact]
    public async Task IdentifyFallsBackToLocalTitleWhenProposalTitleIsJustTheProviderId() {
        // A provider that degrades a failed detail lookup to its raw id must not surface (or apply) a
        // bare id as the entity's name: the local title wins.
        var pluginDir = Path.Combine(_tempRoot, "musicbrainz");
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(
            Path.Combine(pluginDir, "manifest.json"),
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "musicbrainz",
              "name": "MusicBrainz",
              "version": "1.0.0",
              "runtime": "dotnet-process",
              "entry": "Prismedia.Plugin.MusicBrainz.dll",
              "compat": { "pluginApiMin": "1.0.0", "pluginApiMax": null, "prismediaMin": "1.0.0", "prismediaMax": null },
              "auth": [],
              "supports": [
                { "entityKind": "audio-library", "actions": ["search"] }
              ]
            }
            """);

        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        db.ProviderConfigs.Add(new ProviderConfigRow {
            Id = Guid.NewGuid(),
            ProviderCode = "musicbrainz",
            DisplayName = "MusicBrainz",
            ProviderType = ProviderType.ExternalProcess,
            Enabled = true,
            SettingsJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        });
        var albumId = Guid.Parse("cccc3333-aaaa-4444-8888-cccccccccccc");
        db.Entities.Add(new EntityRow { Id = albumId, KindCode = "audio-library", Title = "Evolve", CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        var providerId = "dd87fb34-57e1-41cb-9c68-3fad83068dd5";
        var executor = new RawIdTitleProposalProcessExecutor(ProposalKind.AudioLibrary, providerId);
        var service = CreateIdentifyService(db, executor, pluginDir);

        var response = await service.IdentifyAsync(albumId, "musicbrainz", null, parentExternalIds: null, hideNsfw: false, CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal("Evolve", response.Result!.Patch.Title);
    }

    [Fact]
    public async Task IdentifyKeepsFullProviderEpisodeTreeWhenIdentifyingSeasonDirectly() {
        var pluginDir = Path.Combine(_tempRoot, "tmdb");
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(
            Path.Combine(pluginDir, "manifest.json"),
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "tmdb",
              "name": "TMDB",
              "version": "1.2.0",
              "runtime": "dotnet-process",
              "entry": "Prismedia.Plugin.Tmdb.dll",
              "compat": {
                "pluginApiMin": "1.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [
                { "key": "apiKey", "label": "API key", "required": true, "url": "https://www.themoviedb.org/settings/api" }
              ],
              "supports": [
                { "entityKind": "video-series", "actions": ["lookup-id", "search"] },
                { "entityKind": "video-season", "actions": ["lookup-id", "search"] },
                { "entityKind": "video", "actions": ["lookup-id", "search"] }
              ]
            }
            """);

        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var providerConfig = new ProviderConfigRow {
            Id = Guid.NewGuid(),
            ProviderCode = "tmdb",
            DisplayName = "TMDB",
            ProviderType = ProviderType.ExternalProcess,
            Enabled = true,
            SettingsJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        };
        var seriesId = Guid.Parse("55555555-5555-4444-8888-555555555555");
        var seasonId = Guid.Parse("66666666-5555-4444-8888-666666666666");
        var episodeIds = Enumerable.Range(1, 26)
            .Select(number => Guid.Parse($"77777777-5555-4444-8888-{number:000000000000}"))
            .ToArray();
        db.ProviderConfigs.Add(providerConfig);
        db.ProviderCredentials.Add(new ProviderCredentialRow {
            Id = Guid.NewGuid(),
            ProviderConfigId = providerConfig.Id,
            CredentialKey = "apiKey",
            EncryptedValue = "secret",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Entities.AddRange(
            new EntityRow { Id = seriesId, KindCode = "video-series", Title = "Bear in the Big Blue House", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = seasonId, KindCode = "video-season", Title = "Season 1", ParentEntityId = seriesId, SortOrder = 1, CreatedAt = now, UpdatedAt = now });
        for (var episodeNumber = 1; episodeNumber <= 26; episodeNumber++) {
            db.Entities.Add(new EntityRow {
                Id = episodeIds[episodeNumber - 1],
                KindCode = "video",
                Title = episodeNumber == 1
                    ? "Bear in the Big Blue House - S01E01 - Home is Where the Bear Is SDTV"
                    : $"Episode {episodeNumber:00}",
                ParentEntityId = seasonId,
                SortOrder = episodeNumber,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        db.EntityExternalIds.AddRange(
            new EntityExternalIdRow { Id = Guid.NewGuid(), EntityId = seriesId, Provider = "tmdb", Value = "207", CreatedAt = now, UpdatedAt = now },
            new EntityExternalIdRow { Id = Guid.NewGuid(), EntityId = seasonId, Provider = "tmdb", Value = "668", CreatedAt = now, UpdatedAt = now });
        db.EntityPositions.Add(new EntityPositionRow { EntityId = seasonId, Code = "season", Value = 1, UpdatedAt = now });
        await db.SaveChangesAsync();

        var executor = new FullSeasonProcessExecutor(seasonId);
        var service = CreateIdentifyService(db, executor, pluginDir);

        var response = await service.IdentifyAsync(seasonId, "tmdb", null, parentExternalIds: null, hideNsfw: false, CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal([seasonId], executor.Requests.Select(request => request.Entity.Id).ToArray());
        Assert.Equal(26, response.Result!.Children.Count);
        Assert.Equal(
            episodeIds.Select(id => (Guid?)id).ToArray(),
            response.Result.Children.Select(child => child.TargetEntityId).ToArray());
        Assert.Equal("Home Is Where the Bear Is", response.Result.Children[0].Patch.Title);
        Assert.Equal("Friends For Life", response.Result.Children[^1].Patch.Title);
    }

    [Fact]
    public async Task IdentifyChoosesActionsFromCurrentEntityKindOnly() {
        var pluginDir = Path.Combine(_tempRoot, "tmdb");
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(
            Path.Combine(pluginDir, "manifest.json"),
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "tmdb",
              "name": "TMDB",
              "version": "1.2.0",
              "runtime": "dotnet-process",
              "entry": "Prismedia.Plugin.Tmdb.dll",
              "compat": {
                "pluginApiMin": "1.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [
                { "key": "apiKey", "label": "API key", "required": true, "url": "https://www.themoviedb.org/settings/api" }
              ],
              "supports": [
                { "entityKind": "video", "actions": ["lookup-id", "search"] },
                { "entityKind": "video-series", "actions": ["search"] }
              ]
            }
            """);

        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var providerConfig = new ProviderConfigRow {
            Id = Guid.NewGuid(),
            ProviderCode = "tmdb",
            DisplayName = "TMDB",
            ProviderType = ProviderType.ExternalProcess,
            Enabled = true,
            SettingsJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        };
        var seriesId = Guid.Parse("25252525-2525-2525-2525-252525252525");
        db.ProviderConfigs.Add(providerConfig);
        db.ProviderCredentials.Add(new ProviderCredentialRow {
            Id = Guid.NewGuid(),
            ProviderConfigId = providerConfig.Id,
            CredentialKey = "apiKey",
            EncryptedValue = "secret",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Entities.Add(new EntityRow { Id = seriesId, KindCode = "video-series", Title = "Series", CreatedAt = now, UpdatedAt = now });
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = seriesId,
            Provider = "tmdb",
            Value = "123",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var executor = new StructuralContextCapturingProcessExecutor();
        var service = CreateIdentifyService(db, executor, pluginDir);

        var response = await service.IdentifyAsync(seriesId, "tmdb", null, parentExternalIds: null, hideNsfw: false, CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal(IdentifyAction.Search, Assert.Single(executor.Requests).Action);
    }

    [Fact]
    public async Task IdentifyUsesSearchWhenTitleQueryOverridesExactHints() {
        var pluginDir = Path.Combine(_tempRoot, "tmdb");
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(
            Path.Combine(pluginDir, "manifest.json"),
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "tmdb",
              "name": "TMDB",
              "version": "1.2.0",
              "runtime": "dotnet-process",
              "entry": "Prismedia.Plugin.Tmdb.dll",
              "compat": {
                "pluginApiMin": "1.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [
                { "key": "apiKey", "label": "API key", "required": true, "url": "https://www.themoviedb.org/settings/api" }
              ],
              "supports": [
                { "entityKind": "video", "actions": ["lookup-id", "lookup-url", "search"] }
              ]
            }
            """);

        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var providerConfig = new ProviderConfigRow {
            Id = Guid.NewGuid(),
            ProviderCode = "tmdb",
            DisplayName = "TMDB",
            ProviderType = ProviderType.ExternalProcess,
            Enabled = true,
            SettingsJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        };
        var videoId = Guid.Parse("29292929-2929-2929-2929-292929292929");
        db.ProviderConfigs.Add(providerConfig);
        db.ProviderCredentials.Add(new ProviderCredentialRow {
            Id = Guid.NewGuid(),
            ProviderConfigId = providerConfig.Id,
            CredentialKey = "apiKey",
            EncryptedValue = "secret",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Entities.Add(new EntityRow { Id = videoId, KindCode = "video", Title = "Wrong Exact Movie", CreatedAt = now, UpdatedAt = now });
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = videoId,
            Provider = "tmdb",
            Value = "404",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var executor = new StructuralContextCapturingProcessExecutor();
        var service = CreateIdentifyService(db, executor, pluginDir);

        var response = await service.IdentifyAsync(
            videoId,
            "tmdb",
            new IdentifyQuery("Friendship", null, null),
            parentExternalIds: null,
            hideNsfw: false,
            CancellationToken.None);

        Assert.True(response.Ok);
        var request = Assert.Single(executor.Requests);
        Assert.Equal(IdentifyAction.Search, request.Action);
        Assert.Empty(request.Hints.ExternalIds);
        Assert.True(request.IncludeNsfw);
    }

    [Fact]
    public async Task DirectChildIdentifyIncludesHydratedAncestorsWithoutWalkingUpward() {
        var pluginDir = Path.Combine(_tempRoot, "tmdb");
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(
            Path.Combine(pluginDir, "manifest.json"),
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "tmdb",
              "name": "TMDB",
              "version": "1.2.0",
              "runtime": "dotnet-process",
              "entry": "Prismedia.Plugin.Tmdb.dll",
              "compat": {
                "pluginApiMin": "1.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [
                { "key": "apiKey", "label": "API key", "required": true, "url": "https://www.themoviedb.org/settings/api" }
              ],
              "supports": [
                { "entityKind": "video", "actions": ["search"] },
                { "entityKind": "video-season", "actions": ["search"] },
                { "entityKind": "video-series", "actions": ["search"] }
              ]
            }
            """);

        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var providerConfig = new ProviderConfigRow {
            Id = Guid.NewGuid(),
            ProviderCode = "tmdb",
            DisplayName = "TMDB",
            ProviderType = ProviderType.ExternalProcess,
            Enabled = true,
            SettingsJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        };
        var seriesId = Guid.Parse("26262626-2626-2626-2626-262626262626");
        var seasonId = Guid.Parse("27272727-2727-2727-2727-272727272727");
        var episodeId = Guid.Parse("28282828-2828-2828-2828-282828282828");
        db.ProviderConfigs.Add(providerConfig);
        db.ProviderCredentials.Add(new ProviderCredentialRow {
            Id = Guid.NewGuid(),
            ProviderConfigId = providerConfig.Id,
            CredentialKey = "apiKey",
            EncryptedValue = "secret",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Entities.AddRange(
            new EntityRow { Id = seriesId, KindCode = "video-series", Title = "Parent Series", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = seasonId, KindCode = "video-season", Title = "Season 2", ParentEntityId = seriesId, SortOrder = 2, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = episodeId, KindCode = "video", Title = "Episode 3", ParentEntityId = seasonId, SortOrder = 3, CreatedAt = now, UpdatedAt = now });
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = seriesId,
            Provider = "tmdb",
            Value = "999",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityUrls.Add(new EntityUrlRow {
            Id = Guid.NewGuid(),
            EntityId = seriesId,
            Url = "https://www.themoviedb.org/tv/999",
            SortOrder = 0,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        var executor = new StructuralContextCapturingProcessExecutor();
        var service = CreateIdentifyService(db, executor, pluginDir);

        var response = await service.IdentifyAsync(episodeId, "tmdb", null, parentExternalIds: null, hideNsfw: false, CancellationToken.None);

        Assert.True(response.Ok);
        var request = Assert.Single(executor.Requests);
        Assert.Equal(episodeId, request.Entity.Id);
        Assert.NotNull(request.StructuralContext);
        var structuralContext = request.StructuralContext;
        var seriesAncestor = structuralContext.Ancestors.Last();
        Assert.Equal([seasonId, seriesId], structuralContext.Ancestors.Select(ancestor => ancestor.Id).ToArray());
        Assert.Equal("999", seriesAncestor.ExternalIds!["tmdb"]);
        Assert.Equal("https://www.themoviedb.org/tv/999", Assert.Single(seriesAncestor.Urls!));
        Assert.Equal(3, structuralContext.Positions["sortOrder"]);
    }

    public void Dispose() {
        if (Directory.Exists(_tempRoot)) {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"plugin-runtime-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }

    private IdentifyPluginService CreateIdentifyService(
        PrismediaDbContext db,
        ProcessExecutor executor,
        string pluginDir) =>
        new(
            db,
            new PluginCatalogService(db, new PluginCatalogOptions([pluginDir], _tempRoot, "1.0.0")),
            new IdentifyMatchHintResolver(db),
            new IdentifyRunnerSelector([new DotnetPluginProcessRunner(executor, new PluginCatalogOptions([], _tempRoot, "1.0.0"))]),
            new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(_tempRoot)),
            new AlwaysEligibleIdentifyTargetEligibilityService());

    private sealed class AlwaysEligibleIdentifyTargetEligibilityService : IIdentifyTargetEligibilityService {
        public Task<IdentifyTargetEligibility> EvaluateAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(new IdentifyTargetEligibility(entityId, IdentifyTargetEligibilityStatus.Eligible));

        public Task<IReadOnlyDictionary<Guid, IdentifyTargetEligibility>> EvaluateManyAsync(
            IReadOnlyCollection<Guid> entityIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, IdentifyTargetEligibility>>(
                entityIds.Distinct().ToDictionary(
                    entityId => entityId,
                    entityId => new IdentifyTargetEligibility(entityId, IdentifyTargetEligibilityStatus.Eligible)));
    }

    private sealed class CapturingProcessExecutor : ProcessExecutor {
        public string? FileName { get; private set; }
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public IdentifyPluginRequest? CapturedRequest { get; private set; }

        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            FileName = fileName;
            Arguments = arguments.ToArray();
            var requestJson = await File.ReadAllTextAsync(arguments[1], cancellationToken);
            CapturedRequest = JsonSerializer.Deserialize<IdentifyPluginRequest>(
                requestJson,
                WireJson);
            var wireJson = """
                {
                  "ok": true,
                  "result": {
                    "type": "proposal",
                    "proposal": {
                      "proposalId": "tmdb:123",
                      "provider": "tmdb",
                      "targetKind": "video",
                      "confidence": 1,
                      "matchReason": "external-id",
                      "patch": {
                        "title": "Example",
                        "externalIds": { "tmdb": "123" },
                        "urls": [],
                        "tags": [],
                        "credits": [],
                        "dates": {},
                        "stats": {},
                        "positions": {}
                      },
                      "images": [],
                      "children": [],
                      "candidates": []
                    },
                    "candidates": []
                  },
                  "error": null
                }
                """;

            return new ProcessExecutionResult(0, wireJson, string.Empty);
        }
    }

    private sealed class EmptyCandidateProcessExecutor : ProcessExecutor {
        public override Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) =>
            Task.FromResult(new ProcessExecutionResult(
                0,
                """
                {
                  "ok": true,
                  "result": {
                    "type": "candidates",
                    "proposal": null,
                    "candidates": []
                  },
                  "error": null
                }
                """,
                string.Empty));
    }

    private sealed class CandidateMetadataProcessExecutor : ProcessExecutor {
        public override Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) =>
            Task.FromResult(new ProcessExecutionResult(
                0,
                """
                {
                  "ok": true,
                  "result": {
                    "type": "candidates",
                    "proposal": null,
                    "candidates": [
                      {
                        "candidateId": "tmdb:tv:125935",
                        "externalIds": { "tmdb": "125935" },
                        "title": "Abbott Elementary",
                        "description": "A workplace comedy.",
                        "thumbnailUrl": "https://image.tmdb.org/t/p/w342/poster.jpg",
                        "year": 2021,
                        "source": "TMDB",
                        "confidence": 1,
                        "matchReason": "title-search"
                      }
                    ]
                  },
                  "error": null
                }
                """,
                string.Empty));
    }

    private sealed class StructuralContextCapturingProcessExecutor : ProcessExecutor {
        public List<IdentifyPluginRequest> Requests { get; } = [];

        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            var requestJson = await File.ReadAllTextAsync(arguments[1], cancellationToken);
            var request = JsonSerializer.Deserialize<IdentifyPluginRequest>(
                requestJson,
                WireJson)!;
            Requests.Add(request);

            var kindCode = request.Entity.Kind.ToCode();
            var proposal = new EntityMetadataProposal(
                $"tmdb:{kindCode}:{request.Entity.Id}",
                "tmdb",
                request.Entity.Kind.ToProposalKind(),
                request.StructuralContext?.Ancestors.Count > 0 ? 0.9m : 1m,
                request.StructuralContext?.Ancestors.Count > 0 ? "structural-child" : "title-search",
                new EntityMetadataPatch(
                    $"{request.Entity.Title} identified",
                    null,
                    new Dictionary<string, string>(),
                    [],
                    [],
                    null,
                    [],
                    new Dictionary<string, string>(),
                    new Dictionary<string, int>(),
                    new Dictionary<string, int>(),
                    null),
                [],
                [],
                []);

            return new ProcessExecutionResult(0, SerializeAsWire(proposal), string.Empty);
        }
    }

    private sealed class ProviderChildTreeProcessExecutor(Guid seriesId, Guid? seasonId) : ProcessExecutor {
        public List<IdentifyPluginRequest> Requests { get; } = [];

        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            var requestJson = await File.ReadAllTextAsync(arguments[1], cancellationToken);
            var request = JsonSerializer.Deserialize<IdentifyPluginRequest>(
                requestJson,
                WireJson)!;
            Requests.Add(request);

            var proposal = request.Entity.Id == seriesId
                ? SeriesProposal()
                : ChildProposal(request);

            return new ProcessExecutionResult(0, SerializeAsWire(proposal), string.Empty);
        }

        private EntityMetadataProposal SeriesProposal() {
            var studioRelationship = new EntityMetadataProposal(
                "tmdb:studio:generated",
                "tmdb",
                ProposalKind.Studio,
                1,
                "studio",
                EmptyPatch() with { Title = "Generated Studio" },
                [],
                [],
                []);
            var providerStructuralChild = new EntityMetadataProposal(
                "tmdb:season:provider",
                "tmdb",
                ProposalKind.VideoSeason,
                1,
                "provider-tree",
                EmptyPatch() with {
                    Title = "Season 1",
                    Positions = new Dictionary<string, int> { ["seasonNumber"] = 1 }
                },
                [],
                [],
                []);

            return new EntityMetadataProposal(
                "tmdb:series:42",
                "tmdb",
                ProposalKind.VideoSeries,
                1,
                "search",
                EmptyPatch() with {
                    Title = "Identified Series",
                    ExternalIds = new Dictionary<string, string> { ["tmdb"] = "series-42" },
                    Urls = ["https://www.themoviedb.org/tv/42"]
                },
                [],
                [providerStructuralChild],
                [],
                Relationships: [studioRelationship]);
        }

        private EntityMetadataProposal ChildProposal(IdentifyPluginRequest request) =>
            new(
                $"tmdb:season:{seasonId}",
                "tmdb",
                request.Entity.Kind.ToProposalKind(),
                1,
                "child-context",
                EmptyPatch() with { Title = "Season From Child Request" },
                [],
                [],
                []);
    }

    private sealed class FullSeasonProcessExecutor(Guid seasonId) : ProcessExecutor {
        public List<IdentifyPluginRequest> Requests { get; } = [];

        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            var requestJson = await File.ReadAllTextAsync(arguments[1], cancellationToken);
            var request = JsonSerializer.Deserialize<IdentifyPluginRequest>(
                requestJson,
                WireJson)!;
            Requests.Add(request);

            return new ProcessExecutionResult(0, SerializeAsWire(SeasonProposal()), string.Empty);
        }

        private EntityMetadataProposal SeasonProposal() {
            var episodes = Enumerable.Range(1, 26)
                .Select(number => new EntityMetadataProposal(
                    $"tmdb:tv:207:s1:e{number}",
                    "tmdb",
                    ProposalKind.VideoEpisode,
                    0.9m,
                    "cascade",
                    EmptyPatch() with {
                        Title = number switch {
                            1 => "Home Is Where the Bear Is",
                            26 => "Friends For Life",
                            _ => $"Episode {number:00}"
                        },
                        Positions = new Dictionary<string, int> {
                            ["episodeNumber"] = number,
                            ["seasonNumber"] = 1
                        }
                    },
                    [],
                    [],
                    []))
                .ToArray();

            return new EntityMetadataProposal(
                $"tmdb:tv:207:season:{seasonId}",
                "tmdb",
                ProposalKind.VideoSeason,
                0.9m,
                "context",
                EmptyPatch() with {
                    Title = "Season 1",
                    ExternalIds = new Dictionary<string, string> { ["tmdb"] = "668" },
                    Positions = new Dictionary<string, int> { ["seasonNumber"] = 1 }
                },
                [],
                episodes,
                []);
        }
    }

    /// <summary>
    /// Echoes whatever entity was requested back as its own proposal (no provider-advertised
    /// children), so the cascade's own local-child walk is what produces the tree. Records every
    /// request so a test can assert which entities were identified.
    /// </summary>
    private sealed class EchoProposalProcessExecutor : ProcessExecutor {
        public List<IdentifyPluginRequest> Requests { get; } = [];

        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            var request = JsonSerializer.Deserialize<IdentifyPluginRequest>(
                await File.ReadAllTextAsync(arguments[1], cancellationToken),
                WireJson)!;
            Requests.Add(request);

            var kindCode = request.Entity.Kind.ToCode();
            var proposal = new EntityMetadataProposal(
                $"echo:{kindCode}:{request.Entity.Id}",
                request.Entity.Kind == EntityKind.Video ? "tmdb" : "musicbrainz",
                request.Entity.Kind.ToProposalKind(),
                0.9m,
                "external-id",
                EmptyPatch() with {
                    Title = request.Entity.Title,
                    ExternalIds = new Dictionary<string, string> { ["provider"] = request.Entity.Id.ToString() }
                },
                [],
                [],
                []);
            return new ProcessExecutionResult(0, SerializeAsWire(proposal), string.Empty);
        }
    }

    private sealed class RawIdTitleProposalProcessExecutor(ProposalKind kind, string providerId) : ProcessExecutor {
        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            await Task.CompletedTask;
            // Mimics a provider that degraded a failed detail lookup to its raw id: the title is just
            // the external id value.
            var proposal = new EntityMetadataProposal(
                $"musicbrainz:release:{providerId}",
                "musicbrainz",
                kind,
                0.9m,
                "parent-context",
                EmptyPatch() with {
                    Title = providerId,
                    ExternalIds = new Dictionary<string, string> { ["musicbrainz"] = providerId }
                },
                [],
                [],
                []);
            return new ProcessExecutionResult(0, SerializeAsWire(proposal), string.Empty);
        }
    }

    // Mirrors the production plugin wire: codec enums (entity kind, proposal TargetKind) are
    // carried as their stable string code, so the fake executors round-trip exactly as a real
    // dotnet-process plugin does.
    private static readonly JsonSerializerOptions WireJson =
        new(JsonSerializerDefaults.Web) { Converters = { new CodecJsonConverterFactory() } };

    private static string SerializeAsWire(EntityMetadataProposal proposal) {
        var wire = new {
            ok = true,
            result = new {
                type = "proposal",
                proposal,
                candidates = Array.Empty<object>()
            },
            error = (string?)null
        };
        return JsonSerializer.Serialize(wire, WireJson);
    }

    private static EntityMetadataPatch EmptyPatch() => new(
        Title: null,
        Description: null,
        ExternalIds: new Dictionary<string, string>(),
        Urls: [],
        Tags: [],
        Studio: null,
        Credits: [],
        Dates: new Dictionary<string, string>(),
        Stats: new Dictionary<string, int>(),
        Positions: new Dictionary<string, int>(),
        Classification: null);

    private static byte[] CreatePluginArchive(string manifestJson) {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) {
            var manifest = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifest.Open())) {
                writer.Write(manifestJson);
            }

            var entry = archive.CreateEntry("Prismedia.Plugin.Tmdb.dll");
            using var entryStream = entry.Open();
            entryStream.WriteByte(0);
        }

        return stream.ToArray();
    }

    private sealed class StaticHttpMessageHandler : HttpMessageHandler {
        private readonly IReadOnlyDictionary<string, byte[]> _responses;

        public StaticHttpMessageHandler(IReadOnlyDictionary<string, byte[]> responses) {
            _responses = responses;
        }

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) {
            Requests.Add(request.RequestUri!);
            if (!_responses.TryGetValue(request.RequestUri!.ToString(), out var body) &&
                !_responses.TryGetValue(request.RequestUri!.GetLeftPart(UriPartial.Path), out body)) {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new ByteArrayContent(body)
            });
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler {
        private readonly Exception _exception;

        public ThrowingHttpMessageHandler(Exception exception) {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(_exception);
    }
}
