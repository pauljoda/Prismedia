using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Prismedia.Api.Tests;

/// <summary>Locks successful empty responses for optional state queried by Entity detail pages.</summary>
public sealed class OptionalEntityStateEndpointTests {
    [Theory]
    [InlineData("/api/identify/queue/entities/{0}")]
    [InlineData("/api/acquisitions/for-entity/{0}")]
    [InlineData("/api/monitors/for-entity/{0}")]
    public async Task OptionalEntityStateReturnsNoContentWhenItDoesNotExist(string routeTemplate) {
        using var factory = new WebApplicationFactory<Program>().WithTestAuth();
        using var client = factory.CreateAuthenticatedClient();
        var route = string.Format(routeTemplate, Guid.NewGuid());

        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, response.Content.Headers.ContentLength);
    }
}
