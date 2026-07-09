using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Api.Tests;

public sealed class RequestEndpointTests {
    private static readonly JsonSerializerOptions CodecJson =
        new(JsonSerializerDefaults.Web) { Converters = { new CodecJsonConverterFactory() } };

    [Fact]
    public async Task CommitReturnsConflictWhenAnExternalIdentityMatchesMultipleEntities() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            "/api/requests/commit",
            new RequestCommitRequest(RequestMediaKind.Movie, "tmdb:603", []),
            CodecJson);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ApiProblemCodes.ExternalIdentityAmbiguous, problem!.Code);
    }

    [Fact]
    public async Task ReviewReturnsTheCompleteNestedProposalWithoutFlatteningIt() {
        var reviews = new NestedReviewSource();
        using var factory = CreateFactory(reviews);
        using var client = factory.CreateAuthenticatedClient();
        var identity = new ExternalIdentity("tmdb", "series:603");

        using var response = await client.PostAsJsonAsync(
            "/api/requests/review?hideNsfw=false",
            new RequestReviewRequest(RequestMediaKind.Series, "cinema-metadata", identity),
            CodecJson);
        var review = await response.Content.ReadFromJsonAsync<RequestReviewResponse>(CodecJson);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(review);
        Assert.Equal("cinema-metadata", review.PluginId);
        Assert.Equal(identity, review.ExternalIdentity);
        Assert.Equal("series-root", review.Proposal.ProposalId);
        var season = Assert.Single(review.Proposal.Children);
        Assert.Equal("season-child", season.ProposalId);
        var episode = Assert.Single(season.Children);
        Assert.Equal("episode:one", episode.Patch.ExternalIds["episode-db"]);
        Assert.Equal(["series-root", "season-child", "episode-child"], review.Targets.Select(target => target.ProposalId).ToArray());
        Assert.True(reviews.LastHideNsfw);
    }

    [Fact]
    public async Task PluginSearchUsesTheSelectedSchemaFieldsAndSessionVisibility() {
        var searches = new EndpointPluginSearchSource();
        using var factory = CreateFactory(searchSource: searches);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            "/api/requests/search?hideNsfw=false",
            new RequestPluginSearchRequest(
                RequestMediaKind.Movie,
                "cinema-metadata",
                new Dictionary<string, string> { ["title"] = "Film:CaseSensitive" }),
            CodecJson);
        var body = await response.Content.ReadFromJsonAsync<RequestSearchResponse>(CodecJson);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = Assert.Single(body!.Results);
        Assert.Equal("cinema-metadata", result.PluginId);
        Assert.Equal(new ExternalIdentity("tmdb", "Movie:CaseSensitive"), result.ExternalIdentity);
        Assert.Equal("cinema-metadata", searches.LastPluginId);
        Assert.True(searches.LastHideNsfw);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task PluginSearchRejectsMissingRequiredAndUnknownFields(bool includeTitle, bool includeUnknown) {
        var searches = new EndpointPluginSearchSource();
        using var factory = CreateFactory(searchSource: searches);
        using var client = factory.CreateAuthenticatedClient();
        var fields = new Dictionary<string, string>();
        if (includeTitle) {
            fields["title"] = "Known title";
        }
        if (includeUnknown) {
            fields["unknown"] = "value";
        }

        using var response = await client.PostAsJsonAsync(
            "/api/requests/search",
            new RequestPluginSearchRequest(RequestMediaKind.Movie, "cinema-metadata", fields),
            CodecJson);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(CodecJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiProblemCodes.RequestInvalid, problem!.Code);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IPluginRequestReviewSource? reviewSource = null,
        IPluginRequestSearchSource? searchSource = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.RemoveAll<IPluginRequestProposalSource>();
                    services.RemoveAll<IWantedEntityWriter>();
                    services.AddSingleton<IPluginRequestProposalSource, MovieProposalSource>();
                    services.AddSingleton<IWantedEntityWriter, AmbiguousWantedEntityWriter>();
                    if (reviewSource is not null) {
                        services.RemoveAll<IPluginRequestReviewSource>();
                        services.AddSingleton(reviewSource);
                    }
                    if (searchSource is not null) {
                        services.RemoveAll<IPluginRequestSearchSource>();
                        services.AddSingleton(searchSource);
                    }
                });
            })
            .WithTestAuth();

    private sealed class EndpointPluginSearchSource : IPluginRequestSearchSource {
        public string? LastPluginId { get; private set; }
        public bool LastHideNsfw { get; private set; }

        public Task<IReadOnlyList<RequestSearchResult>> SearchAsync(
            RequestKindDescriptor descriptor,
            string pluginId,
            IReadOnlyDictionary<string, string> fields,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            if (!fields.TryGetValue("title", out var title) || string.IsNullOrWhiteSpace(title)) {
                throw new RequestSearchValidationException("The required title field is missing.");
            }
            if (fields.Keys.Any(key => key != "title")) {
                throw new RequestSearchValidationException("The search contains an unknown field.");
            }

            LastPluginId = pluginId;
            LastHideNsfw = hideNsfw;
            return Task.FromResult<IReadOnlyList<RequestSearchResult>>([
                new(
                    Guid.Empty,
                    RequestProviderKind.Plugin,
                    descriptor.Kind,
                    "tmdb:Movie:CaseSensitive",
                    title,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    [],
                    false,
                    null,
                    null,
                    true,
                    "Cinema Metadata",
                    pluginId,
                    new ExternalIdentity("tmdb", "Movie:CaseSensitive"))
            ]);
        }
    }

    private sealed class NestedReviewSource : IPluginRequestReviewSource {
        public bool LastHideNsfw { get; private set; }

        public Task<RequestReviewResponse?> ReviewAsync(
            RequestReviewRequest request,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            LastHideNsfw = hideNsfw;
            var episode = new EntityMetadataProposal(
                "episode-child",
                request.PluginId,
                ProposalKind.VideoEpisode,
                1,
                "cascade",
                Patch("Episode", "episode-db", "episode:one"),
                [],
                [],
                [],
                null,
                []);
            var season = new EntityMetadataProposal(
                "season-child",
                request.PluginId,
                ProposalKind.VideoSeason,
                1,
                "cascade",
                Patch("Season 1", "tvdb", "season:one"),
                [],
                [episode],
                [],
                null,
                []);
            var proposal = new EntityMetadataProposal(
                "series-root",
                request.PluginId,
                ProposalKind.VideoSeries,
                1,
                "external-id",
                Patch("Series", request.ExternalIdentity.Namespace, request.ExternalIdentity.Value),
                [],
                [season],
                [],
                null,
                []);

            return Task.FromResult<RequestReviewResponse?>(new RequestReviewResponse(
                request.PluginId,
                request.ExternalIdentity,
                EntityKind.VideoSeries,
                request.Kind,
                proposal,
                "revision",
                [
                    new("series-root", RequestMediaKind.Series, EntityKind.VideoSeries, request.ExternalIdentity, true),
                    new("season-child", RequestMediaKind.Season, EntityKind.VideoSeason, new ExternalIdentity("tvdb", "season:one"), true, 1),
                    new("episode-child", RequestMediaKind.Episode, EntityKind.Video, new ExternalIdentity("episode-db", "episode:one"), true, 1)
                ]));
        }

        private static EntityMetadataPatch Patch(string title, string identityNamespace, string identityValue) =>
            new(
                title,
                null,
                new Dictionary<string, string> { [identityNamespace] = identityValue },
                [],
                [],
                null,
                [],
                new Dictionary<string, string>(),
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                null);
    }

    private sealed class MovieProposalSource : IPluginRequestProposalSource {
        public Task<EntityMetadataProposal?> ResolveProposalAsync(
            RequestKindDescriptor descriptor,
            PluginIdentityRoute route,
            bool hideNsfw,
            bool includeChildren,
            CancellationToken cancellationToken) =>
            ResolveProposalAsync(descriptor, route.Identity, hideNsfw, includeChildren, cancellationToken);

        public Task<EntityMetadataProposal?> ResolveProposalAsync(
            RequestKindDescriptor descriptor,
            ExternalIdentity identity,
            bool hideNsfw,
            bool includeChildren,
            CancellationToken cancellationToken) =>
            Task.FromResult<EntityMetadataProposal?>(new EntityMetadataProposal(
                ProposalId: "movie-603",
                Provider: "movie-plugin",
                TargetKind: ProposalKind.Movie,
                Confidence: 1,
                MatchReason: "lookup-id",
                Patch: new EntityMetadataPatch(
                    Title: "The Matrix",
                    Description: null,
                    ExternalIds: new Dictionary<string, string> { [identity.Namespace] = identity.Value },
                    Urls: [],
                    Tags: [],
                    Studio: null,
                    Credits: [],
                    Dates: new Dictionary<string, string>(),
                    Stats: new Dictionary<string, int>(),
                    Positions: new Dictionary<string, int>(),
                    Classification: null),
                Images: [],
                Children: [],
                Candidates: [],
                TargetEntityId: null,
                Relationships: []));
    }

    private sealed class AmbiguousWantedEntityWriter : IWantedEntityWriter {
        public Task<WantedEntityResult> EnsureAsync(
            EntityKind kind,
            ExternalIdentity identity,
            string title,
            Guid? parentEntityId,
            bool matchTitleKindWide,
            CancellationToken cancellationToken) {
            var resolution = new ExternalIdentityResolution([
                new ExternalIdentityMatch(Guid.Parse("11111111-1111-1111-1111-111111111111"), [identity]),
                new ExternalIdentityMatch(Guid.Parse("22222222-2222-2222-2222-222222222222"), [identity])
            ]);
            throw new ExternalIdentityAmbiguityException(kind, resolution);
        }

        public Task ApplyProposalAsync(
            Guid entityId,
            EntityMetadataProposal proposal,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DeleteIfWantedAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MonitorableContainer?> GetContainerAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> ListWantedChildIdsAsync(
            Guid parentEntityId,
            EntityKind childKind,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> ListChildIdsAsync(
            Guid parentEntityId,
            EntityKind childKind,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
