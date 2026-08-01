using Microsoft.AspNetCore.Mvc.Testing;

namespace Prismedia.Api.Tests;

public sealed class SeriesEndpointTests {
    [Fact]
    public async Task LegacyVideoSeasonDetailAliasIsNotMapped() {
        using var factory = new WebApplicationFactory<Program>().WithTestAuth();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/api/series/{Guid.NewGuid()}/seasons/{Guid.NewGuid()}");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
