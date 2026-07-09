using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Plugins;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Requests;

namespace Prismedia.Infrastructure.Tests;

public sealed class PluginRequestMetadataSourceRoutingTests : IDisposable {
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"prismedia-request-routing-{Guid.NewGuid():N}");

    [Fact]
    public async Task LookupRoutesNamespaceThroughDistinctPluginIdAndSendsNamespaceToPlugin() {
        await using var db = await CreateInstalledPluginAsync("cinema-metadata");
        var catalog = Catalog(db);
        IPluginIdentityRouter router = new PluginIdentityRouter(catalog);
        var runner = new CapturingRunner();
        var source = new PluginRequestMetadataSource(catalog, router, new IdentifyRunnerSelector([runner]));
        var identity = new ExternalIdentity("tmdb", $"movie-{Guid.NewGuid():N}");

        var proposal = await source.ResolveProposalAsync(
            RequestKindRegistry.Find(RequestMediaKind.Movie)!,
            identity,
            hideNsfw: false,
            includeChildren: false,
            CancellationToken.None);

        Assert.NotNull(proposal);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("cinema-metadata", call.Descriptor.Manifest.Id);
        Assert.Equal(identity.Value, call.Request.Query.ExternalIds!["tmdb"]);
        Assert.DoesNotContain("cinema-metadata", call.Request.Query.ExternalIds.Keys);
    }

    [Fact]
    public async Task SearchUsesDeclaredNamespaceOrderInsteadOfPluginIdOrDictionaryOrder() {
        await using var db = await CreateInstalledPluginAsync("cinema-metadata");
        var catalog = Catalog(db);
        var runner = new CapturingRunner();
        var source = new PluginRequestMetadataSource(
            catalog,
            new PluginIdentityRouter(catalog),
            new IdentifyRunnerSelector([runner]));

        var results = await source.SearchAsync(
            RequestKindRegistry.Find(RequestMediaKind.Movie)!,
            "Example",
            hideNsfw: false,
            CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("tmdb:123", result.ExternalId);
        Assert.Equal("cinema-metadata", Assert.Single(runner.Calls).Descriptor.Manifest.Id);
    }

    [Fact]
    public async Task SharedNamespaceRoutesAreTriedInDeterministicOrderUntilOneResolves() {
        await using var db = await CreateInstalledPluginAsync("zeta-metadata", "alpha-metadata");
        var catalog = Catalog(db);
        var runner = new CapturingRunner(matchOnlyPluginId: "zeta-metadata");
        var source = new PluginRequestMetadataSource(
            catalog,
            new PluginIdentityRouter(catalog),
            new IdentifyRunnerSelector([runner]));

        var proposal = await source.ResolveProposalAsync(
            RequestKindRegistry.Find(RequestMediaKind.Movie)!,
            new ExternalIdentity("tmdb", $"ambiguous-{Guid.NewGuid():N}"),
            hideNsfw: false,
            includeChildren: false,
            CancellationToken.None);

        Assert.NotNull(proposal);
        Assert.Equal(
            ["alpha-metadata", "zeta-metadata"],
            runner.Calls.Select(call => call.Descriptor.Manifest.Id).ToArray());
    }

    private async Task<PrismediaDbContext> CreateInstalledPluginAsync(params string[] pluginIds) {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new PrismediaDbContext(options);
        var now = DateTimeOffset.UtcNow;
        foreach (var pluginId in pluginIds) {
            var directory = Path.Combine(_tempRoot, pluginId);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "manifest.json"),
                $$"""
            {
              "manifestVersion": 2,
              "apiTags": ["prismedia"],
              "id": "{{pluginId}}",
              "name": "{{pluginId}}",
              "version": "2.0.0",
              "runtime": "dotnet-process",
              "entry": "Cinema.Metadata.dll",
              "compat": {
                "pluginApiMin": "2.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [],
              "supports": [{
                "entityKind": "movie",
                "actions": ["search", "lookup-id"],
                "identityNamespaces": ["tmdb", "imdb"],
                "search": { "fields": [
                  { "key": "title", "label": "Title", "type": "text", "required": true }
                ] }
              }]
            }
            """);
            db.ProviderConfigs.Add(new ProviderConfigRow {
                Id = Guid.NewGuid(),
                ProviderCode = pluginId,
                DisplayName = pluginId,
                ProviderType = ProviderType.ExternalProcess,
                Enabled = true,
                SettingsJson = "{}",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync();
        return db;
    }

    private PluginCatalogService Catalog(PrismediaDbContext db) =>
        new(db, new PluginCatalogOptions([_tempRoot], _tempRoot, "1.0.0"));

    public void Dispose() {
        if (Directory.Exists(_tempRoot)) {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private sealed class CapturingRunner(string? matchOnlyPluginId = null) : IIdentifyRunner {
        public sealed record Call(PluginDescriptor Descriptor, IdentifyPluginRequest Request);

        public List<Call> Calls { get; } = [];

        public bool CanRun(PluginDescriptor descriptor) => descriptor.Manifest.Runtime == "dotnet-process";

        public Task<IdentifyPluginResponse> IdentifyAsync(
            PluginDescriptor descriptor,
            IdentifyPluginRequest request,
            CancellationToken cancellationToken) {
            Calls.Add(new Call(descriptor, request));
            if (request.Action == IdentifyAction.Search) {
                return Task.FromResult(IdentifyPluginResponse.Candidates(
                    ProposalKind.Movie,
                    [
                        new EntitySearchCandidate(
                            new Dictionary<string, string> {
                                ["imdb"] = "tt123",
                                ["tmdb"] = "123"
                            },
                            "Example",
                            2026,
                            null,
                            null,
                            null)
                    ]));
            }

            if (matchOnlyPluginId is not null &&
                !descriptor.Manifest.Id.Equals(matchOnlyPluginId, StringComparison.OrdinalIgnoreCase)) {
                return Task.FromResult(IdentifyPluginResponse.NoMatch());
            }

            var identity = Assert.Single(request.Query.ExternalIds!);
            return Task.FromResult(IdentifyPluginResponse.Match(new EntityMetadataProposal(
                $"proposal-{identity.Value}",
                descriptor.Manifest.Id,
                ProposalKind.Movie,
                1,
                "external-id",
                new EntityMetadataPatch(
                    "Example",
                    null,
                    new Dictionary<string, string> { [identity.Key] = identity.Value },
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
                [],
                null,
                [])));
        }
    }
}
