using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Plugins;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
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
    public async Task SelectedSearchUsesDeclaredNamespaceOrderInsteadOfPluginIdOrDictionaryOrder() {
        await using var db = await CreateInstalledPluginAsync("cinema-metadata");
        var catalog = Catalog(db);
        var runner = new CapturingRunner();
        var source = new PluginRequestMetadataSource(
            catalog,
            new PluginIdentityRouter(catalog),
            new IdentifyRunnerSelector([runner]));

        var results = await source.SearchAsync(
            RequestKindRegistry.Find(RequestMediaKind.Movie)!,
            "cinema-metadata",
            new Dictionary<string, string> { ["workName"] = "Example" },
            hideNsfw: false,
            CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("tmdb:Movie:CaseSensitive", result.ExternalId);
        Assert.Equal("cinema-metadata", result.PluginId);
        Assert.Equal(new ExternalIdentity("tmdb", "Movie:CaseSensitive"), result.ExternalIdentity);
        Assert.Equal("cinema-metadata", Assert.Single(runner.Calls).Descriptor.Manifest.Id);
    }

    [Fact]
    public async Task SchemaSearchCallsOnlySelectedPluginAndPreservesTrimmedOpaqueFieldsAndIdentity() {
        await using var db = await CreateInstalledPluginAsync("alpha-metadata", "zeta-metadata");
        var catalog = Catalog(db);
        var runner = new CapturingRunner();
        var source = new PluginRequestMetadataSource(
            catalog,
            new PluginIdentityRouter(catalog),
            new IdentifyRunnerSelector([runner]));

        var results = await source.SearchAsync(
            RequestKindRegistry.Find(RequestMediaKind.Movie)!,
            "zeta-metadata",
            new Dictionary<string, string> {
                ["context"] = "   ",
                ["workName"] = "  Film:CaseSensitive  ",
                ["year"] = " 2026 "
            },
            hideNsfw: false,
            CancellationToken.None);

        var call = Assert.Single(runner.Calls);
        Assert.Equal("zeta-metadata", call.Descriptor.Manifest.Id);
        Assert.Equal("Film:CaseSensitive", call.Request.Query.Title);
        Assert.Equal(string.Empty, call.Request.Query.Fields!["context"]);
        Assert.Equal("Film:CaseSensitive", call.Request.Query.Fields["workName"]);
        Assert.Equal("2026", call.Request.Query.Fields["year"]);
        Assert.DoesNotContain("title", call.Request.Query.Fields.Keys);
        var result = Assert.Single(results);
        Assert.Equal("zeta-metadata", result.PluginId);
        Assert.Equal(new ExternalIdentity("tmdb", "Movie:CaseSensitive"), result.ExternalIdentity);
    }

    [Theory]
    [InlineData(false, "year")]
    [InlineData(true, "mysteryField")]
    public async Task SchemaSearchRejectsMissingRequiredAndUnknownFields(bool includeRequired, string extraField) {
        await using var db = await CreateInstalledPluginAsync("cinema-metadata");
        var catalog = Catalog(db);
        var runner = new CapturingRunner();
        var source = new PluginRequestMetadataSource(
            catalog,
            new PluginIdentityRouter(catalog),
            new IdentifyRunnerSelector([runner]));
        var fields = new Dictionary<string, string> { [extraField] = "value" };
        if (includeRequired) {
            fields["workName"] = "Known title";
        }

        await Assert.ThrowsAsync<RequestSearchValidationException>(() => source.SearchAsync(
            RequestKindRegistry.Find(RequestMediaKind.Movie)!,
            "cinema-metadata",
            fields,
            hideNsfw: false,
            CancellationToken.None));

        Assert.Empty(runner.Calls);
    }

    [Theory]
    [InlineData("year", "26")]
    [InlineData("year", "20x6")]
    [InlineData("number", "1,2")]
    [InlineData("number", "NaN")]
    public async Task SchemaSearchRejectsInvalidTypedFields(string key, string value) {
        await using var db = await CreateInstalledPluginAsync("cinema-metadata");
        var catalog = Catalog(db);
        var runner = new CapturingRunner();
        var source = new PluginRequestMetadataSource(
            catalog,
            new PluginIdentityRouter(catalog),
            new IdentifyRunnerSelector([runner]));

        await Assert.ThrowsAsync<RequestSearchValidationException>(() => source.SearchAsync(
            RequestKindRegistry.Find(RequestMediaKind.Movie)!,
            "cinema-metadata",
            new Dictionary<string, string> {
                ["workName"] = "Film",
                [key] = value
            },
            hideNsfw: false,
            CancellationToken.None));

        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task ExactSearchAndReviewRejectAPluginWithMissingRequiredAuth() {
        await using var db = await CreateInstalledPluginAsync("cinema-metadata");
        var manifestPath = Path.Combine(_tempRoot, "cinema-metadata", "manifest.json");
        var manifest = await File.ReadAllTextAsync(manifestPath);
        await File.WriteAllTextAsync(
            manifestPath,
            manifest.Replace(
                "\"auth\": []",
                "\"auth\": [{ \"key\": \"apiKey\", \"label\": \"API key\", \"required\": true, \"url\": null }]",
                StringComparison.Ordinal));
        var catalog = Catalog(db);
        var runner = new CapturingRunner();
        var source = new PluginRequestMetadataSource(
            catalog,
            new PluginIdentityRouter(catalog),
            new IdentifyRunnerSelector([runner]));

        await Assert.ThrowsAsync<RequestSearchValidationException>(() => source.SearchAsync(
            RequestKindRegistry.Find(RequestMediaKind.Movie)!,
            "cinema-metadata",
            new Dictionary<string, string> { ["workName"] = "Film" },
            hideNsfw: false,
            CancellationToken.None));
        var review = await source.ReviewAsync(
            new RequestReviewRequest(
                RequestMediaKind.Movie,
                "cinema-metadata",
                new ExternalIdentity("tmdb", "603")),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Null(review);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task SchemaSearchRejectsADisabledSelectedPlugin() {
        await using var db = await CreateInstalledPluginAsync("cinema-metadata");
        var config = await db.ProviderConfigs.SingleAsync();
        config.Enabled = false;
        await db.SaveChangesAsync();
        var catalog = Catalog(db);
        var runner = new CapturingRunner();
        var source = new PluginRequestMetadataSource(
            catalog,
            new PluginIdentityRouter(catalog),
            new IdentifyRunnerSelector([runner]));

        await Assert.ThrowsAsync<RequestSearchValidationException>(() => source.SearchAsync(
            RequestKindRegistry.Find(RequestMediaKind.Movie)!,
            "cinema-metadata",
            new Dictionary<string, string> { ["workName"] = "Film" },
            hideNsfw: false,
            CancellationToken.None));

        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task SchemaSearchRequiresLookupIdAlongsideSearchSupport() {
        await using var db = await CreateInstalledPluginAsync("cinema-metadata");
        var manifestPath = Path.Combine(_tempRoot, "cinema-metadata", "manifest.json");
        var manifest = await File.ReadAllTextAsync(manifestPath);
        await File.WriteAllTextAsync(
            manifestPath,
            manifest.Replace(
                "\"actions\": [\"search\", \"lookup-id\"]",
                "\"actions\": [\"search\"]",
                StringComparison.Ordinal));
        var catalog = Catalog(db);
        var runner = new CapturingRunner();
        var source = new PluginRequestMetadataSource(
            catalog,
            new PluginIdentityRouter(catalog),
            new IdentifyRunnerSelector([runner]));

        await Assert.ThrowsAsync<RequestSearchValidationException>(() => source.SearchAsync(
            RequestKindRegistry.Find(RequestMediaKind.Movie)!,
            "cinema-metadata",
            new Dictionary<string, string> { ["workName"] = "Film" },
            hideNsfw: false,
            CancellationToken.None));

        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task ExplicitReviewKeepsTheSearchPluginWhenNamespaceIsShared() {
        await using var db = await CreateInstalledPluginAsync("zeta-metadata", "alpha-metadata");
        var catalog = Catalog(db);
        var runner = new CapturingRunner(matchOnlyPluginId: "zeta-metadata");
        var source = new PluginRequestMetadataSource(
            catalog,
            new PluginIdentityRouter(catalog),
            new IdentifyRunnerSelector([runner]));
        var identity = new ExternalIdentity("tmdb", $"selected:{Guid.NewGuid():N}");

        var review = await source.ReviewAsync(
            new RequestReviewRequest(RequestMediaKind.Movie, "zeta-metadata", identity),
            hideNsfw: false,
            CancellationToken.None);

        Assert.NotNull(review);
        Assert.Equal("zeta-metadata", review.PluginId);
        Assert.Equal(identity, review.ExternalIdentity);
        Assert.Equal(identity, Assert.Single(review.Targets).ExternalIdentity);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("zeta-metadata", call.Descriptor.Manifest.Id);
        Assert.Equal(identity.Value, call.Request.Query.ExternalIds!["tmdb"]);

        runner.Calls.Clear();
        var failedSelectedPlugin = await source.RevalidateAsync(
            new RequestReviewRequest(
                RequestMediaKind.Movie,
                "alpha-metadata",
                new ExternalIdentity("tmdb", $"unresolved:{Guid.NewGuid():N}")),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Null(failedSelectedPlugin);
        Assert.Equal("alpha-metadata", Assert.Single(runner.Calls).Descriptor.Manifest.Id);
    }

    [Fact]
    public async Task RevalidateBypassesTheExplicitProposalCache() {
        await using var db = await CreateInstalledPluginAsync("cinema-metadata");
        var catalog = Catalog(db);
        var runner = new ProposalFactoryRunner((descriptor, request, call) =>
            MovieProposal(descriptor.Manifest.Id, Assert.Single(request.Query.ExternalIds!), $"Revision {call}"));
        var source = new PluginRequestMetadataSource(
            catalog,
            new PluginIdentityRouter(catalog),
            new IdentifyRunnerSelector([runner]));
        var request = new RequestReviewRequest(
            RequestMediaKind.Movie,
            "cinema-metadata",
            new ExternalIdentity("tmdb", $"cache:{Guid.NewGuid():N}"));

        var first = await source.ReviewAsync(request, hideNsfw: false, CancellationToken.None);
        var cached = await source.ReviewAsync(request, hideNsfw: false, CancellationToken.None);
        var fresh = await source.RevalidateAsync(request, hideNsfw: false, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(first!.Revision, cached!.Revision);
        Assert.NotEqual(first.Revision, fresh!.Revision);
        Assert.Equal("Revision 2", fresh.Proposal.Patch.Title);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("identity")]
    [InlineData("proposal-id")]
    public async Task ReviewRejectsMismatchedProviderIdentityAndDuplicateProposalIds(string invalidPart) {
        await using var db = await CreateInstalledPluginAsync("cinema-metadata");
        var catalog = Catalog(db);
        var runner = new ProposalFactoryRunner((descriptor, request, _) => {
            var identity = Assert.Single(request.Query.ExternalIds!);
            var proposal = MovieProposal(
                invalidPart == "provider" ? "different-plugin" : descriptor.Manifest.Id,
                invalidPart == "identity"
                    ? new KeyValuePair<string, string>(identity.Key, $"different:{identity.Value}")
                    : identity,
                "Example");
            return invalidPart == "proposal-id"
                ? proposal with {
                    Children = [proposal with { Patch = proposal.Patch with { Title = "Duplicate child" } }]
                }
                : proposal;
        });
        var source = new PluginRequestMetadataSource(
            catalog,
            new PluginIdentityRouter(catalog),
            new IdentifyRunnerSelector([runner]));

        var review = await source.ReviewAsync(
            new RequestReviewRequest(
                RequestMediaKind.Movie,
                "cinema-metadata",
                new ExternalIdentity("tmdb", $"invalid:{Guid.NewGuid():N}")),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Null(review);
        Assert.Single(runner.Calls);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("identity")]
    public async Task ExplicitRouteResolutionRejectsProposalThatDoesNotMatchRequestedRoute(string invalidPart) {
        await using var db = await CreateInstalledPluginAsync("cinema-metadata");
        var catalog = Catalog(db);
        var runner = new ProposalFactoryRunner((descriptor, request, _) => {
            var identity = Assert.Single(request.Query.ExternalIds!);
            return MovieProposal(
                invalidPart == "provider" ? "different-plugin" : descriptor.Manifest.Id,
                invalidPart == "identity"
                    ? new KeyValuePair<string, string>(identity.Key, $"different:{identity.Value}")
                    : identity,
                "Example");
        });
        var source = new PluginRequestMetadataSource(
            catalog,
            new PluginIdentityRouter(catalog),
            new IdentifyRunnerSelector([runner]));
        var descriptor = RequestKindRegistry.Find(RequestMediaKind.Movie)!;
        var route = new PluginIdentityRoute(
            "cinema-metadata",
            new ExternalIdentity("tmdb", $"monitored:{Guid.NewGuid():N}"));

        var first = await source.ResolveProposalAsync(
            descriptor,
            route,
            hideNsfw: false,
            includeChildren: true,
            CancellationToken.None);
        var second = await source.ResolveProposalAsync(
            descriptor,
            route,
            hideNsfw: false,
            includeChildren: true,
            CancellationToken.None);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(2, runner.Calls.Count);
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
        var identity = new ExternalIdentity("tmdb", $"ambiguous-{Guid.NewGuid():N}");

        var resolved = await source.ResolveProposalAsync(
            RequestKindRegistry.Find(RequestMediaKind.Movie)!,
            identity,
            hideNsfw: false,
            includeChildren: false,
            CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal(new PluginIdentityRoute("zeta-metadata", identity), resolved.Route);
        Assert.Equal(
            ["alpha-metadata", "zeta-metadata"],
            runner.Calls.Select(call => call.Descriptor.Manifest.Id).ToArray());
    }

    [Fact]
    public async Task ReviewKeepsNestedProposalAndBuildsTargetsFromEachKindsDeclaredNamespace() {
        await using var db = await CreateSeriesPluginAsync();
        var catalog = Catalog(db);
        var runner = new NestedSeriesRunner();
        var source = new PluginRequestMetadataSource(
            catalog,
            new PluginIdentityRouter(catalog),
            new IdentifyRunnerSelector([runner]));
        var identity = new ExternalIdentity("tmdb", $"Series:{Guid.NewGuid():N}");

        var review = await source.ReviewAsync(
            new RequestReviewRequest(RequestMediaKind.Series, "series-metadata", identity),
            hideNsfw: false,
            CancellationToken.None);

        Assert.NotNull(review);
        Assert.Same(runner.Proposal, review.Proposal);
        Assert.Equal(EntityKind.VideoSeries, review.EntityKind);
        Assert.Equal(64, review.Revision.Length);
        Assert.Equal(review.Revision.ToLowerInvariant(), review.Revision);
        Assert.Collection(
            review.Targets,
            target => {
                Assert.Equal("series-root", target.ProposalId);
                Assert.Equal(RequestMediaKind.Series, target.Kind);
                Assert.Equal(EntityKind.VideoSeries, target.EntityKind);
                Assert.Equal(identity, target.ExternalIdentity);
            },
            target => {
                Assert.Equal("season-one", target.ProposalId);
                Assert.Equal(RequestMediaKind.Season, target.Kind);
                Assert.Equal(EntityKind.VideoSeason, target.EntityKind);
                Assert.Equal(new ExternalIdentity("tvdb", "Season:One"), target.ExternalIdentity);
                Assert.Equal(1, target.Position);
                Assert.Equal(2025, target.Year);
            },
            target => {
                Assert.Equal("episode-one", target.ProposalId);
                Assert.Equal(RequestMediaKind.Episode, target.Kind);
                Assert.Equal(EntityKind.Video, target.EntityKind);
                Assert.Equal(new ExternalIdentity("episode-db", "Episode:One"), target.ExternalIdentity);
                Assert.Equal(1, target.Position);
            });
        Assert.DoesNotContain(review.Targets, target => target.ExternalIdentity.Namespace == "series-metadata");
        Assert.Equal("Episode:One", review.Proposal.Children[0].Children[0].Patch.ExternalIds["episode-db"]);
    }

    [Fact]
    public async Task ReviewRejectsSameKindSiblingsWithTheSameExternalIdentity() {
        await using var db = await CreateSeriesPluginAsync();
        var catalog = Catalog(db);
        var runner = new NestedSeriesRunner(duplicateSeasonIdentity: true);
        var source = new PluginRequestMetadataSource(
            catalog,
            new PluginIdentityRouter(catalog),
            new IdentifyRunnerSelector([runner]));

        var review = await source.ReviewAsync(
            new RequestReviewRequest(
                RequestMediaKind.Series,
                "series-metadata",
                new ExternalIdentity("tmdb", $"duplicate:{Guid.NewGuid():N}")),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Null(review);
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
                  { "key": "context", "label": "Context", "type": "text", "required": false },
                  { "key": "workName", "label": "Work name", "type": "text", "required": true },
                  { "key": "number", "label": "Number", "type": "number", "required": false },
                  { "key": "year", "label": "Year", "type": "year", "required": false }
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

    private async Task<PrismediaDbContext> CreateSeriesPluginAsync() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new PrismediaDbContext(options);
        var directory = Path.Combine(_tempRoot, "series-metadata");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "manifest.json"),
            """
            {
              "manifestVersion": 2,
              "apiTags": ["prismedia"],
              "id": "series-metadata",
              "name": "Series Metadata",
              "version": "2.0.0",
              "runtime": "dotnet-process",
              "entry": "Series.Metadata.dll",
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
                  "actions": ["lookup-id"],
                  "identityNamespaces": ["tmdb"]
                },
                {
                  "entityKind": "video-season",
                  "actions": ["lookup-id"],
                  "identityNamespaces": ["tvdb"]
                },
                {
                  "entityKind": "video",
                  "actions": ["lookup-id"],
                  "identityNamespaces": ["episode-db"]
                }
              ]
            }
            """);
        db.ProviderConfigs.Add(new ProviderConfigRow {
            Id = Guid.NewGuid(),
            ProviderCode = "series-metadata",
            DisplayName = "Series Metadata",
            ProviderType = ProviderType.ExternalProcess,
            Enabled = true,
            SettingsJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
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

    private static EntityMetadataProposal MovieProposal(
        string pluginId,
        KeyValuePair<string, string> identity,
        string title) =>
        new(
            $"proposal-{identity.Value}",
            pluginId,
            ProposalKind.Movie,
            1,
            "external-id",
            new EntityMetadataPatch(
                title,
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
            []);

    private sealed class ProposalFactoryRunner(
        Func<PluginDescriptor, IdentifyPluginRequest, int, EntityMetadataProposal> proposal) : IIdentifyRunner {
        public List<(PluginDescriptor Descriptor, IdentifyPluginRequest Request)> Calls { get; } = [];

        public bool CanRun(PluginDescriptor descriptor) => descriptor.Manifest.Runtime == "dotnet-process";

        public Task<IdentifyPluginResponse> IdentifyAsync(
            PluginDescriptor descriptor,
            IdentifyPluginRequest request,
            CancellationToken cancellationToken) {
            Calls.Add((descriptor, request));
            return Task.FromResult(IdentifyPluginResponse.Match(proposal(descriptor, request, Calls.Count)));
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
                var candidateIdentity = request.Query.Fields is null ? "123" : "Movie:CaseSensitive";
                return Task.FromResult(IdentifyPluginResponse.Candidates(
                    ProposalKind.Movie,
                    [
                        new EntitySearchCandidate(
                            new Dictionary<string, string> {
                                ["imdb"] = "tt123",
                                ["tmdb"] = candidateIdentity
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

    private sealed class NestedSeriesRunner(bool duplicateSeasonIdentity = false) : IIdentifyRunner {
        public EntityMetadataProposal? Proposal { get; private set; }

        public bool CanRun(PluginDescriptor descriptor) => descriptor.Manifest.Runtime == "dotnet-process";

        public Task<IdentifyPluginResponse> IdentifyAsync(
            PluginDescriptor descriptor,
            IdentifyPluginRequest request,
            CancellationToken cancellationToken) {
            var identity = Assert.Single(request.Query.ExternalIds!);
            var episode = new EntityMetadataProposal(
                "episode-one",
                descriptor.Manifest.Id,
                ProposalKind.VideoEpisode,
                1,
                "cascade",
                Patch(
                    "Episode 1",
                    new Dictionary<string, string> {
                        ["tmdb"] = "Wrong:RootNamespace",
                        ["episode-db"] = "Episode:One"
                    },
                    new Dictionary<string, int> { [EntityPositionCodes.Episode] = 1 }),
                [],
                [],
                [],
                null,
                []);
            var season = new EntityMetadataProposal(
                "season-one",
                descriptor.Manifest.Id,
                ProposalKind.VideoSeason,
                1,
                "cascade",
                Patch(
                    "Season 1",
                    new Dictionary<string, string> { ["tvdb"] = "Season:One" },
                    new Dictionary<string, int> { [EntityPositionCodes.Season] = 1 },
                    new Dictionary<string, string> { ["release"] = "2025-01-01" }),
                [],
                [episode],
                [],
                null,
                []);
            EntityMetadataProposal[] children = duplicateSeasonIdentity
                ? [season, season with {
                    ProposalId = "season-two",
                    Patch = season.Patch with { Title = "Season duplicate" }
                }]
                : [season];
            Proposal = new EntityMetadataProposal(
                "series-root",
                descriptor.Manifest.Id,
                ProposalKind.VideoSeries,
                1,
                "external-id",
                Patch(
                    "Series",
                    new Dictionary<string, string> { [identity.Key] = identity.Value },
                    new Dictionary<string, int>()),
                [],
                children,
                [],
                null,
                []);
            return Task.FromResult(IdentifyPluginResponse.Match(Proposal));
        }

        private static EntityMetadataPatch Patch(
            string title,
            IReadOnlyDictionary<string, string> externalIds,
            IReadOnlyDictionary<string, int> positions,
            IReadOnlyDictionary<string, string>? dates = null) =>
            new(
                title,
                null,
                externalIds,
                [],
                [],
                null,
                [],
                dates ?? new Dictionary<string, string>(),
                new Dictionary<string, int>(),
                positions,
                null);
    }
}
