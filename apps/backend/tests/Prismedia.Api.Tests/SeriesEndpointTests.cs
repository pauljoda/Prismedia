using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class SeriesEndpointTests {
    [Fact]
    public async Task VideoSeasonAliasRequiresTheDirectSeriesParent() {
        var seriesId = Guid.NewGuid();
        var otherSeriesId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var season = new EntityCard {
            Id = seasonId,
            Kind = EntityKind.VideoSeason,
            Title = "Season 1",
            ParentEntityId = seriesId,
            SortOrder = 1,
            Capabilities = [],
            ChildrenByKind = [],
            Relationships = []
        };
        using var factory = CreateFactory(season);
        using var client = factory.CreateAuthenticatedClient();

        using var wrongParent = await client.GetAsync($"/api/series/{otherSeriesId}/seasons/{seasonId}");
        using var canonical = await client.GetAsync($"/api/entities/{seasonId}");
        using var matchingParent = await client.GetAsync($"/api/series/{seriesId}/seasons/{seasonId}");

        Assert.Equal(HttpStatusCode.NotFound, wrongParent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, canonical.StatusCode);
        Assert.Equal(canonical.StatusCode, matchingParent.StatusCode);
        Assert.Equal(
            await canonical.Content.ReadAsStringAsync(),
            await matchingParent.Content.ReadAsStringAsync());
    }

    private static WebApplicationFactory<Program> CreateFactory(EntityCard season) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.RemoveAll<IEntityReadService>();
                    services.AddSingleton<IEntityReadService>(new SeasonReadService(season));
                });
            })
            .WithTestAuth();

    private sealed class SeasonReadService(EntityCard season) : EntityReadServiceStub {
        public override Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<EntityCard?>(id == season.Id ? season : null);
    }
}
