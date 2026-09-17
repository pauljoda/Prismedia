using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Api.Tests;

/// <summary>Locks successful empty responses for optional state queried by Entity detail pages.</summary>
public sealed class OptionalEntityStateEndpointTests {
    [Theory]
    [InlineData("/api/identify/queue/entities/{0}")]
    [InlineData("/api/identify/queue/entities/{0}/status")]
    [InlineData("/api/acquisitions/for-entity/{0}")]
    [InlineData("/api/monitors/for-entity/{0}")]
    public async Task OptionalEntityStateReturnsNoContentWhenItDoesNotExist(string routeTemplate) {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var factory = new WebApplicationFactory<Program>().WithTestAuth()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services => {
                services.RemoveAll<PrismediaDbContext>();
                services.AddScoped(_ => new PrismediaDbContext(options));
            }));
        using var client = factory.CreateAuthenticatedClient();
        var route = string.Format(routeTemplate, Guid.NewGuid());

        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, response.Content.Headers.ContentLength);
    }
}
