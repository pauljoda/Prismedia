using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Api.Tests;

public sealed class IdentifyBulkEndpointTests {
    private static readonly JsonSerializerOptions CodecJson =
        new(JsonSerializerDefaults.Web) { Converters = { new CodecJsonConverterFactory() } };

    [Fact]
    public async Task StartBulkIdentifyRequestsOneSearchPerEntity() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();
        var entityIds = new[] {
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("11111111-1111-1111-1111-111111111112"),
        };

        using var response = await client.PostAsJsonAsync(
            "/api/identify/bulk",
            new IdentifyBulkStartRequest("tmdb", entityIds, new IdentifyQuery("Hint", null, null)),
            CodecJson);
        var body = await response.Content.ReadFromJsonAsync<IdentifyBulkAcceptedResponse>(CodecJson);

        var queue = factory.Services.GetRequiredService<RecordingIdentifyQueueService>();
        var call = Assert.Single(queue.BatchRequests);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(2, body.Requested);
        Assert.Equal(2, body.Enqueued);
        Assert.Equal(entityIds, call.EntityIds);
        Assert.Equal("tmdb", call.Request.Provider);
        Assert.Equal("Hint", call.Request.Query?.Title);
    }

    [Fact]
    public async Task StartBulkIdentifyRejectsEmptyEntityList() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            "/api/identify/bulk",
            new IdentifyBulkStartRequest("tmdb", [], null),
            CodecJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Services.GetRequiredService<RecordingIdentifyQueueService>().BatchRequests);
    }

    [Fact]
    public async Task AddIdentifyQueueItemReturnsConflictForAFilelessTarget() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();
        var entityId = Guid.NewGuid();
        factory.Services.GetRequiredService<RecordingIdentifyQueueService>().EligibilityFailure =
            new IdentifyTargetNotEligibleException(new IdentifyTargetEligibility(
                entityId,
                IdentifyTargetEligibilityStatus.NoSourceMedia));

        using var response = await client.PostAsync($"/api/identify/queue/entities/{entityId}", null);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ApiProblemCodes.IdentifyTargetNotEligible, problem?.Code);
    }

    [Fact]
    public async Task IdentifyEntityReturnsNotFoundWhenTargetDisappears() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();
        var entityId = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync(
            $"/api/identify/entities/{entityId}",
            new IdentifyEntityRequest("tmdb", null),
            CodecJson);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ApiProblemCodes.EntityNotFound, problem?.Code);
    }

    [Fact]
    public async Task ApplyIdentifyProposalReturnsNotFoundWhenTargetDisappears() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();
        var entityId = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync(
            $"/api/identify/entities/{entityId}/apply",
            new ApplyIdentifyProposalRequest(CreateProposal(entityId), [], null),
            CodecJson);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ApiProblemCodes.EntityNotFound, problem?.Code);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton<RecordingIdentifyQueueService>();
                    services.AddScoped<IIdentifyQueueService>(provider =>
                        provider.GetRequiredService<RecordingIdentifyQueueService>());
                    services.AddSingleton<MissingIdentifyTargetService>();
                    services.AddScoped<IIdentifyProviderService>(provider =>
                        provider.GetRequiredService<MissingIdentifyTargetService>());
                });
            })
            .WithTestAuth();

    private static EntityMetadataProposal CreateProposal(Guid entityId) =>
        new(
            ProposalId: "missing-target",
            Provider: "tmdb",
            TargetKind: ProposalKind.Video,
            Confidence: 1m,
            MatchReason: "test",
            Patch: new EntityMetadataPatch(
                Title: "Missing target",
                Description: null,
                ExternalIds: new Dictionary<string, string>(),
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
            TargetEntityId: entityId,
            Relationships: []);

    private sealed class MissingIdentifyTargetService : IIdentifyProviderService {
        public Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(
            string? entityKind,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentifyPluginResponse> IdentifyAsync(
            Guid entityId,
            string providerId,
            IdentifyQuery? query,
            IReadOnlyDictionary<string, string>? parentExternalIds,
            bool hideNsfw,
            CancellationToken cancellationToken,
            bool cascadeChildren = true,
            IIdentifyCascadeSink? sink = null,
            bool hydrateRelationships = true) =>
            Task.FromException<IdentifyPluginResponse>(
                new KeyNotFoundException($"Entity '{entityId}' was not found."));

        public Task<bool> ApplyAsync(
            Guid entityId,
            EntityMetadataProposal proposal,
            IReadOnlyCollection<string> selectedFields,
            IReadOnlyDictionary<string, string?>? selectedImages,
            CancellationToken cancellationToken,
            IIdentifyApplyProgressReporter? progress = null) =>
            Task.FromException<bool>(new KeyNotFoundException($"Entity '{entityId}' was not found."));
    }

    private sealed class RecordingIdentifyQueueService : IIdentifyQueueService {
        public List<(IReadOnlyList<Guid> EntityIds, IdentifyQueueSearchRequest Request, bool HideNsfw)> BatchRequests { get; } = [];
        public IdentifyTargetNotEligibleException? EligibilityFailure { get; set; }

        public Task<IdentifyBulkAcceptedResponse> RequestSearchBatchAsync(
            IReadOnlyList<Guid> entityIds,
            IdentifyQueueSearchRequest request,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            BatchRequests.Add((entityIds, request, hideNsfw));
            return Task.FromResult(new IdentifyBulkAcceptedResponse(entityIds.Count, entityIds.Count));
        }

        public Task<IReadOnlyList<IdentifyQueueItem>> ListAsync(bool includeCompleted, bool hideNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentifyQueueItem> AddAsync(Guid entityId, CancellationToken cancellationToken) =>
            EligibilityFailure is not null
                ? Task.FromException<IdentifyQueueItem>(EligibilityFailure)
                : throw new NotSupportedException();

        public Task<IdentifyQueueItem?> GetAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentifyQueueItem> RequestSearchAsync(Guid entityId, IdentifyQueueSearchRequest request, bool hideNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentifyQueueItem> ResolveCandidateAsync(Guid entityId, IdentifyQueueCandidateRequest request, bool hideNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentifyQueueItem> ApplyAsync(Guid entityId, ApplyIdentifyQueueItemRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentifyQueueItem> SaveProposalAsync(Guid entityId, EntityMetadataProposal proposal, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentifyQueueItem?> DeleteAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
