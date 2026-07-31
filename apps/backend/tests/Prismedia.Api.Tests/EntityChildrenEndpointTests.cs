using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Tests;

public sealed class EntityChildrenEndpointTests {
    [Fact]
    public async Task RejectsAnUnboundedParentBatchBeforeReadingEntities() {
        var entities = new CapturingEntityReadService();
        using var factory = CreateFactory(entities);
        using var client = factory.CreateAuthenticatedClient();
        var parentIds = Enumerable.Range(0, EntityChildrenBatchRequest.MaximumParentIds + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        using var response = await client.PostAsJsonAsync(
            "/api/entities/children",
            new EntityChildrenBatchRequest(parentIds));
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiProblemCodes.RequestInvalid, problem?.Code);
        Assert.Equal(0, entities.CallCount);
    }

    private static WebApplicationFactory<Program> CreateFactory(IEntityReadService entities) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.RemoveAll<IEntityReadService>();
                    services.AddSingleton(entities);
                });
            })
            .WithTestAuth();

    private sealed class CapturingEntityReadService : IEntityReadService {
        public int CallCount { get; private set; }

        public Task<EntityListResponse> ListAsync(
            string? kind,
            string? query,
            string? cursor,
            bool? hideNsfw,
            int? limit,
            CancellationToken cancellationToken,
            Guid? referencedBy = null,
            string? relationshipCode = null,
            string? sort = null,
            string? sortDir = null,
            int? seed = null,
            bool? favorite = null,
            bool? organized = null,
            int? ratingMin = null,
            int? ratingMax = null,
            bool? unrated = null,
            string? status = null,
            string? bookType = null,
            string? bookFormat = null,
            bool? nsfw = null,
            bool? hasFile = null,
            bool? played = null,
            bool? orphaned = null,
            bool? wanted = null,
            Prismedia.Domain.Entities.AcquisitionStatus? acquisitionStatus = null) =>
            Task.FromResult(new EntityListResponse([], null, 0));

        public Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<EntityCard?>(null);

        public Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EntityThumbnailBatchResponse([]));

        public Task<EntityChildrenBatchResponse> GetChildrenAsync(
            IReadOnlyList<Guid> parentIds,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            CallCount++;
            return Task.FromResult(new EntityChildrenBatchResponse([]));
        }
    }
}
