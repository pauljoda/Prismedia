using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Prismedia.Api.Tests;

public sealed class UpdateCheckEndpointTests : IClassFixture<WebApplicationFactory<Program>> {
    private readonly WebApplicationFactory<Program> _factory;

    public UpdateCheckEndpointTests(WebApplicationFactory<Program> factory) {
        _factory = factory;
    }

    [Fact]
    public async Task UpdateCheckEndpointReturnsNonBlockingStatus() {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/update-check");
        var payload = await response.Content.ReadFromJsonAsync<UpdateCheckResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("unknown", payload.Status);
        Assert.False(payload.UpdateAvailable);
    }

    private sealed record UpdateCheckResponse(string Status, bool UpdateAvailable);
}
