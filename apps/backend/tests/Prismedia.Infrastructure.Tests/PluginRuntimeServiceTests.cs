using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Tests;

public sealed class PluginRuntimeServiceTests : IDisposable {
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"prismedia-plugin-tests-{Guid.NewGuid():N}");

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
    public async Task CatalogReusesAliasedProviderCredentialKeysForAuthFields() {
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
            CredentialKey = "TMDB_API_KEY",
            EncryptedValue = "aliased-secret",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var catalog = new PluginCatalogService(db, new PluginCatalogOptions([_tempRoot], _tempRoot, "1.0.0"));

        var provider = Assert.Single(await catalog.ListProvidersAsync(CancellationToken.None));
        var auth = await catalog.GetAuthAsync((await catalog.FindProviderAsync("tmdb", "video", CancellationToken.None))!.Manifest, CancellationToken.None);

        Assert.True(provider.Installed);
        Assert.Empty(provider.MissingAuthKeys);
        Assert.Equal("aliased-secret", auth["apiKey"]);
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
                []),
            ManifestPath: Path.Combine(_tempRoot, "manifest.json"),
            WorkingDirectory: _tempRoot,
            EntryPath: Path.Combine(_tempRoot, "tmdb.dll"));
        var entityId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var request = new IdentifyPluginRequest(
            1,
            "lookup-id",
            new Dictionary<string, string> { ["apiKey"] = "secret" },
            new IdentifyEntitySnapshot(entityId, "video", "Example"),
            new IdentifyQuery(null, null, null),
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
        Assert.Equal("lookup-id", executor.CapturedRequest?.Action);
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
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityFlags.Add(new EntityFlagRow {
            EntityId = entityId,
            IsNsfw = true,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var executor = new CapturingProcessExecutor();
        var service = CreateIdentifyService(db, executor, _tempRoot);

        var response = await service.IdentifyAsync(entityId, "tmdb", null, hideNsfw: true, CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Null(response.Result);
        Assert.Contains("was not found", response.Error);
        Assert.Null(executor.CapturedRequest);
    }

    [Fact]
    public async Task IdentifyTraversesGenericStructuralChildrenWhenProviderSupportsChildKinds() {
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
        var seriesId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var seasonId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
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
            new EntityRow { Id = seriesId, KindCode = "video-series", Title = "Example Series", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = seasonId, KindCode = "video-season", Title = "Season 1", ParentEntityId = seriesId, SortOrder = 1, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        var executor = new StructuralContextCapturingProcessExecutor();
        var catalog = new PluginCatalogService(db, new PluginCatalogOptions([_tempRoot], _tempRoot, "1.0.0"));
        var service = new IdentifyPluginService(
            db,
            catalog,
            new IdentifyMatchHintResolver(db),
            new DotnetPluginProcessRunner(executor, new PluginCatalogOptions([], _tempRoot, "1.0.0")),
            new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(_tempRoot)));

        var response = await service.IdentifyAsync(seriesId, "tmdb", null, hideNsfw: false, CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal([seriesId, seasonId], executor.Requests.Select(request => request.Entity.Id).ToArray());
        Assert.Equal(seriesId, response.Result?.TargetEntityId);
        var child = Assert.Single(response.Result!.Children);
        Assert.Equal(seasonId, child.TargetEntityId);
        Assert.Equal("video-season", child.TargetKind);
        Assert.Equal(seriesId, executor.Requests[1].StructuralContext?.Ancestors.Single().Id);
        Assert.Equal(1, executor.Requests[1].StructuralContext?.Positions["sortOrder"]);
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

        var response = await service.IdentifyAsync(seriesId, "tmdb", null, hideNsfw: false, CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal("search", Assert.Single(executor.Requests).Action);
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

        var response = await service.IdentifyAsync(episodeId, "tmdb", null, hideNsfw: false, CancellationToken.None);

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

    [Fact]
    public async Task IdentifyMatchesProviderChildrenWhilePreservingRelationshipProposals() {
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
                { "entityKind": "video-series", "actions": ["lookup-id", "search"] }
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
        var seriesId = Guid.Parse("16161616-1616-1616-1616-161616161616");
        var seasonId = Guid.Parse("17171717-1717-1717-1717-171717171717");
        var episodeId = Guid.Parse("18181818-1818-1818-1818-181818181818");
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
            new EntityRow { Id = seriesId, KindCode = "video-series", Title = "The Chair Company", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = seasonId, KindCode = "video-season", Title = "Season 1", ParentEntityId = seriesId, SortOrder = 1, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = episodeId, KindCode = "video", Title = "Old Episode", ParentEntityId = seasonId, SortOrder = 2, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        var executor = new FullTreeProcessExecutor();
        var catalog = new PluginCatalogService(db, new PluginCatalogOptions([_tempRoot], _tempRoot, "1.0.0"));
        var service = new IdentifyPluginService(
            db,
            catalog,
            new IdentifyMatchHintResolver(db),
            new DotnetPluginProcessRunner(executor, new PluginCatalogOptions([], _tempRoot, "1.0.0")),
            new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(_tempRoot)));

        var response = await service.IdentifyAsync(seriesId, "tmdb", null, hideNsfw: false, CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Single(executor.Requests);
        Assert.Equal(seriesId, response.Result?.TargetEntityId);
        var season = Assert.Single(response.Result!.Children);
        Assert.Equal(seasonId, season.TargetEntityId);
        Assert.Equal("video-season", season.TargetKind);
        Assert.Single(response.Result.Relationships);
        var episode = Assert.Single(season.Children);
        Assert.Equal(episodeId, episode.TargetEntityId);
        Assert.Equal("The Chair Company S01E02", episode.Patch.Title);
        Assert.Equal("Guest Actor", Assert.Single(episode.Relationships).Patch.Title);
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
            new DotnetPluginProcessRunner(executor, new PluginCatalogOptions([], _tempRoot, "1.0.0")),
            new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(_tempRoot)));

    private sealed class CapturingProcessExecutor : ProcessExecutor {
        public string? FileName { get; private set; }
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public IdentifyPluginRequest? CapturedRequest { get; private set; }

        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken) {
            FileName = fileName;
            Arguments = arguments.ToArray();
            var requestJson = await File.ReadAllTextAsync(arguments[1], cancellationToken);
            CapturedRequest = JsonSerializer.Deserialize<IdentifyPluginRequest>(
                requestJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var response = new IdentifyPluginResponse(
                true,
                new EntityMetadataProposal(
                    "tmdb:123",
                    "tmdb",
                    "video",
                    1,
                    "external-id",
                    new EntityMetadataPatch(
                        "Example",
                        null,
                        new Dictionary<string, string> { ["tmdb"] = "123" },
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
                    []),
                null);

            return new ProcessExecutionResult(
                0,
                JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                string.Empty);
        }
    }

    private sealed class StructuralContextCapturingProcessExecutor : ProcessExecutor {
        public List<IdentifyPluginRequest> Requests { get; } = [];

        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken) {
            var requestJson = await File.ReadAllTextAsync(arguments[1], cancellationToken);
            var request = JsonSerializer.Deserialize<IdentifyPluginRequest>(
                requestJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Requests.Add(request);

            var response = new IdentifyPluginResponse(
                true,
                new EntityMetadataProposal(
                    $"tmdb:{request.Entity.Kind}:{request.Entity.Id}",
                    "tmdb",
                    request.Entity.Kind,
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
                    []),
                null);

            return new ProcessExecutionResult(
                0,
                JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                string.Empty);
        }
    }

    private sealed class FullTreeProcessExecutor : ProcessExecutor {
        public List<IdentifyPluginRequest> Requests { get; } = [];

        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken) {
            var requestJson = await File.ReadAllTextAsync(arguments[1], cancellationToken);
            var request = JsonSerializer.Deserialize<IdentifyPluginRequest>(
                requestJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Requests.Add(request);

            var guestRelationship = new EntityMetadataProposal(
                "tmdb:person:guest",
                "tmdb",
                "person",
                1,
                "credit",
                EmptyPatch() with { Title = "Guest Actor" },
                [new ImageCandidate("poster", "https://example.test/guest.jpg", "tmdb", null, null, null, null)],
                [],
                []);
            var episode = new EntityMetadataProposal(
                "tmdb:tv:chair:s1:e2",
                "tmdb",
                "video-episode",
                1,
                "cascade",
                EmptyPatch() with {
                    Title = "The Chair Company S01E02",
                    Positions = new Dictionary<string, int> { ["seasonNumber"] = 1, ["episodeNumber"] = 2 },
                    Credits = [new CreditPatch("Guest Actor", "guest", "Visitor", 0)]
                },
                [new ImageCandidate("still", "https://example.test/still.jpg", "tmdb", null, null, null, null)],
                [],
                [],
                Relationships: [guestRelationship]);
            var season = new EntityMetadataProposal(
                "tmdb:tv:chair:season:1",
                "tmdb",
                "video-season",
                1,
                "cascade",
                EmptyPatch() with {
                    Title = "Season 1",
                    Positions = new Dictionary<string, int> { ["seasonNumber"] = 1 }
                },
                [new ImageCandidate("poster", "https://example.test/season.jpg", "tmdb", null, null, null, null)],
                [episode],
                []);
            var studioRelationship = new EntityMetadataProposal(
                "tmdb:studio:chair",
                "tmdb",
                "studio",
                1,
                "studio",
                EmptyPatch() with { Title = "Chair Pictures" },
                [new ImageCandidate("logo", "https://example.test/studio.png", "tmdb", null, null, null, null)],
                [],
                []);
            var response = new IdentifyPluginResponse(
                true,
                new EntityMetadataProposal(
                    "tmdb:tv:chair",
                    "tmdb",
                    "video-series",
                    1,
                    "external-id",
                    EmptyPatch() with {
                        Title = "The Chair Company",
                        Studio = "Chair Pictures",
                        Credits = [new CreditPatch("Series Actor", "cast", "Ron", 0)]
                    },
                    [new ImageCandidate("poster", "https://example.test/series.jpg", "tmdb", null, null, null, null)],
                    [season],
                    [],
                    Relationships: [studioRelationship]),
                null);

            return new ProcessExecutionResult(
                0,
                JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                string.Empty);
        }
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
}
