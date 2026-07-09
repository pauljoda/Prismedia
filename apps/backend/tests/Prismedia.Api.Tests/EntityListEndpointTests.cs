using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class EntityListEndpointTests {
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WantedQueryParameterReachesEntityReadService(bool wanted) {
        var entityReadService = new CapturingEntityReadService();
        using var factory = CreateFactory(entityReadService);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/api/entities?wanted={wanted.ToString().ToLowerInvariant()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(wanted, entityReadService.Wanted);
    }

    [Theory]
    [InlineData("downloaded", AcquisitionStatus.Downloaded)]
    [InlineData("awaiting-selection", AcquisitionStatus.AwaitingSelection)]
    [InlineData("manual-import-required", AcquisitionStatus.ManualImportRequired)]
    public async Task AcquisitionStatusQueryUsesCanonicalCode(string code, AcquisitionStatus expected) {
        var entityReadService = new CapturingEntityReadService();
        using var factory = CreateFactory(entityReadService);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/api/entities?acquisitionStatus={code}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, entityReadService.AcquisitionStatus);
    }

    private static WebApplicationFactory<Program> CreateFactory(IEntityReadService entityReadService) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.RemoveAll<IEntityReadService>();
                    services.AddSingleton(entityReadService);
                });
            })
            .WithTestAuth();

    private sealed class CapturingEntityReadService : IEntityReadService {
        public bool? Wanted { get; private set; }
        public AcquisitionStatus? AcquisitionStatus { get; private set; }

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
            AcquisitionStatus? acquisitionStatus = null) {
            Wanted = wanted;
            AcquisitionStatus = acquisitionStatus;
            return Task.FromResult(new EntityListResponse([], null, 0));
        }

        public Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<EntityCard?>(null);

        public Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EntityThumbnailBatchResponse([]));

        public Task<IEntityCard?> GetDetailAsync(
            Guid id,
            string kind,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult<IEntityCard?>(null);
    }
}
