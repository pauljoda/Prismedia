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

    [Fact]
    public async Task CompactReferencesRejectAnUnboundedParentBatchBeforeReadingEntities() {
        var entities = new CapturingEntityReadService();
        using var factory = CreateFactory(entities);
        using var client = factory.CreateAuthenticatedClient();
        var parentIds = Enumerable.Range(0, EntityChildrenBatchRequest.MaximumParentIds + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        using var response = await client.PostAsJsonAsync(
            "/api/entities/children/references",
            new EntityChildrenBatchRequest(parentIds));
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiProblemCodes.RequestInvalid, problem?.Code);
        Assert.Equal(0, entities.ReferenceCallCount);
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

    private sealed class CapturingEntityReadService : EntityReadServiceStub {
        public int CallCount { get; private set; }
        public int ReferenceCallCount { get; private set; }

        public override Task<EntityChildrenBatchResponse> GetChildrenAsync(
            IReadOnlyList<Guid> parentIds,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            CallCount++;
            return Task.FromResult(new EntityChildrenBatchResponse([]));
        }

        public override Task<EntityChildReferenceBatchResponse> GetChildReferencesAsync(
            IReadOnlyList<Guid> parentIds,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            ReferenceCallCount++;
            return Task.FromResult(new EntityChildReferenceBatchResponse([]));
        }
    }
}
