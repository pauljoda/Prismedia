using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Entities;
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

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.RemoveAll<IPluginRequestProposalSource>();
                    services.RemoveAll<IWantedEntityWriter>();
                    services.AddSingleton<IPluginRequestProposalSource, MovieProposalSource>();
                    services.AddSingleton<IWantedEntityWriter, AmbiguousWantedEntityWriter>();
                });
            })
            .WithTestAuth();

    private sealed class MovieProposalSource : IPluginRequestProposalSource {
        public Task<EntityMetadataProposal?> ResolveProposalAsync(
            RequestKindDescriptor descriptor,
            string providerId,
            string itemId,
            bool hideNsfw,
            bool includeChildren,
            CancellationToken cancellationToken) =>
            Task.FromResult<EntityMetadataProposal?>(new EntityMetadataProposal(
                ProposalId: "movie-603",
                Provider: providerId,
                TargetKind: ProposalKind.Movie,
                Confidence: 1,
                MatchReason: "lookup-id",
                Patch: new EntityMetadataPatch(
                    Title: "The Matrix",
                    Description: null,
                    ExternalIds: new Dictionary<string, string> { [providerId] = itemId },
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
            string providerId,
            string itemId,
            string title,
            Guid? parentEntityId,
            bool matchTitleKindWide,
            CancellationToken cancellationToken) {
            var identity = new ExternalIdentity(providerId, itemId);
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
