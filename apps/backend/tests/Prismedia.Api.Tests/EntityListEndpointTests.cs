using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class EntityListEndpointTests {
    [Fact]
    public async Task KindQueryAcceptsMultipleCanonicalEntityKinds() {
        var entityReadService = new CapturingEntityReadService();
        using var factory = CreateFactory(entityReadService);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            "/api/entities?kind=movie%2Cvideo%2Cvideo-series%2Cvideo-season");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "movie,video,video-series,video-season",
            entityReadService.Kind);
    }

    [Theory]
    [InlineData("movie,unknown")]
    [InlineData("unknown,movie")]
    [InlineData("movie,,video")]
    public async Task KindQueryRejectsTheWholeListWhenAnyTokenIsInvalid(string kind) {
        var entityReadService = new CapturingEntityReadService();
        using var factory = CreateFactory(entityReadService);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/api/entities?kind={Uri.EscapeDataString(kind)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, entityReadService.ListCallCount);
    }

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

    [Fact]
    public async Task KindSpecificListUsesCanonicalFiltersAndForcesItsRouteKind() {
        var entityReadService = new CapturingEntityReadService();
        using var factory = CreateFactory(entityReadService);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            "/api/movies?kind=video&wanted=true&acquisitionStatus=downloaded");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(EntityKind.Movie.ToCode(), entityReadService.Kind);
        Assert.True(entityReadService.Wanted);
        Assert.Equal(AcquisitionStatus.Downloaded, entityReadService.AcquisitionStatus);
    }

    [Fact]
    public async Task CanonicalConsumptionFiltersReachEntityReadService() {
        var entityReadService = new CapturingEntityReadService();
        using var factory = CreateFactory(entityReadService);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            "/api/entities?sort=last-active&sortDirection=desc&engaged=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(EntityListSort.LastActive, entityReadService.Sort);
        Assert.Equal(EntitySortDirection.Descending, entityReadService.SortDirection);
        Assert.True(entityReadService.Engaged);
    }

    [Fact]
    public async Task RemovedLastPlayedSortCodeIsRejected() {
        var entityReadService = new CapturingEntityReadService();
        using var factory = CreateFactory(entityReadService);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync("/api/entities?sort=last-played");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, entityReadService.ListCallCount);
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

    private sealed class CapturingEntityReadService : EntityReadServiceStub {
        public string? Kind { get; private set; }
        public int ListCallCount { get; private set; }
        public bool? Wanted { get; private set; }
        public AcquisitionStatus? AcquisitionStatus { get; private set; }
        public EntityListSort? Sort { get; private set; }
        public EntitySortDirection? SortDirection { get; private set; }
        public bool? Engaged { get; private set; }

        public override Task<EntityListResponse> ListAsync(
            string? kind,
            string? query,
            string? cursor,
            bool? hideNsfw,
            int? limit,
            CancellationToken cancellationToken,
            Guid? referencedBy = null,
            string? relationshipCode = null,
            EntityListSort? sort = null,
            EntitySortDirection? sortDirection = null,
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
            bool? engaged = null,
            bool? orphaned = null,
            bool? wanted = null,
            AcquisitionStatus? acquisitionStatus = null) {
            Kind = kind;
            ListCallCount++;
            Wanted = wanted;
            AcquisitionStatus = acquisitionStatus;
            Sort = sort;
            SortDirection = sortDirection;
            Engaged = engaged;
            return Task.FromResult(new EntityListResponse([], null, 0));
        }

    }
}
