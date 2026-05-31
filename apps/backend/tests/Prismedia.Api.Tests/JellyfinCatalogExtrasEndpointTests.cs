using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Jellyfin;

namespace Prismedia.Api.Tests;

/// <summary>
/// Covers the Jellyfin-compatibility endpoints Infuse probes on detail/show surfaces that Prismedia
/// added for client parity: Next Up, local trailers, special features, and media segments. The first
/// three previously 404'd; these tests pin their routing and the empty Jellyfin response shapes.
/// </summary>
public sealed class JellyfinCatalogExtrasEndpointTests {
    private static readonly Guid ItemId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task LocalTrailersReturnsEmptyArray() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/Items/{ItemId}/LocalTrailers");
        var body = await response.Content.ReadFromJsonAsync<IReadOnlyList<JellyfinBaseItemDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Empty(body);
    }

    [Fact]
    public async Task SpecialFeaturesReturnsEmptyArray() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/Items/{ItemId}/SpecialFeatures");
        var body = await response.Content.ReadFromJsonAsync<IReadOnlyList<JellyfinBaseItemDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Empty(body);
    }

    [Fact]
    public async Task MediaSegmentsReturnsEmptyPagedResult() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var dashed = await client.GetAsync($"/MediaSegments/{ItemId}");
        using var dashless = await client.GetAsync($"/MediaSegments/{ItemId:N}");

        Assert.Equal(HttpStatusCode.OK, dashed.StatusCode);
        Assert.Equal("application/json", dashed.Content.Headers.ContentType?.MediaType);
        var body = await dashed.Content.ReadFromJsonAsync<JellyfinQueryResult<JellyfinMediaSegmentDto>>();
        Assert.NotNull(body);
        Assert.Empty(body.Items);
        Assert.Equal(0, body.TotalRecordCount);
        Assert.Equal(0, body.StartIndex);

        // Infuse requests the dashless (32-hex "N") id form; the :guid constraint must accept it too.
        Assert.Equal(HttpStatusCode.OK, dashless.StatusCode);
        Assert.Equal("application/json", dashless.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task NextUpReturnsJellyfinPagedResult() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync("/Shows/NextUp?Limit=20");
        var body = await response.Content.ReadFromJsonAsync<JellyfinQueryResult<JellyfinBaseItemDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        // The test read service exposes no in-progress content, so the shelf is an empty envelope.
        Assert.Empty(body.Items);
        Assert.Equal(0, body.TotalRecordCount);
    }

    [Fact]
    public async Task UserViewsIncludesMoviesLibrary() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync("/UserViews");
        var body = await response.Content.ReadFromJsonAsync<JellyfinQueryResult<JellyfinBaseItemDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        var movies = body.Items.SingleOrDefault(view => view.Name == "Movies");
        Assert.NotNull(movies);
        Assert.Equal("movies", movies!.CollectionType);
        // The generic Videos library is distinct from Movies so the two don't collide as one library.
        var videos = body.Items.SingleOrDefault(view => view.Name == "Videos");
        Assert.NotNull(videos);
        Assert.Equal("homevideos", videos!.CollectionType);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton<IEntityReadService, TestAuth.VisibleEntityReadService>();
                });
            })
            .WithTestAuth();
}
