using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Jellyfin;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
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

    [Fact]
    public async Task ItemsKeepsRepeatedIncludeItemTypesFromSwiftfinQueries() {
        using var factory = CreateFactory(new MovieEntityReadService());
        using var client = factory.CreateAuthenticatedClient();
        var userId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var moviesView = JellyfinCatalogService.MoviesViewId.ToString("N");

        using var response = await client.GetAsync(
            $"/Users/{userId:N}/Items?parentId={moviesView}&includeItemTypes=BoxSet&includeItemTypes=Movie&includeItemTypes=Series");
        var body = await response.Content.ReadFromJsonAsync<JellyfinQueryResult<JellyfinBaseItemDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        var item = Assert.Single(body.Items);
        Assert.Equal(MovieEntityReadService.MovieId, item.Id);
        Assert.Equal(JellyfinProtocol.ItemTypes.Movie, item.Type);
    }

    [Fact]
    public async Task ItemFiltersReturnsJellyfinJsonShape() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/Items/Filters?userId={Guid.NewGuid():N}&parentId={ItemId:N}");
        var body = await response.Content.ReadFromJsonAsync<JellyfinQueryFiltersLegacyDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(body);
        Assert.Empty(body.Genres);
        Assert.Empty(body.Tags);
        Assert.Empty(body.OfficialRatings);
        Assert.Empty(body.Years);
    }

    [Fact]
    public async Task SimilarItemsReturnsEmptyPagedResult() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/Items/{ItemId:N}/Similar?userId={Guid.NewGuid():N}&limit=20");
        var body = await response.Content.ReadFromJsonAsync<JellyfinQueryResult<JellyfinBaseItemDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(body);
        Assert.Empty(body.Items);
        Assert.Equal(0, body.TotalRecordCount);
    }

    private static WebApplicationFactory<Program> CreateFactory(IEntityReadService? entities = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(entities ?? new TestAuth.VisibleEntityReadService());
                });
            })
            .WithTestAuth();

    private sealed class MovieEntityReadService : IEntityReadService {
        public static readonly Guid MovieId = Guid.Parse("22222222-2222-2222-2222-222222222222");

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
            bool? orphaned = null) {
            var items = kind == "movie"
                ? new[] { Thumb(MovieId, "movie", "Repeated Query Movie") }
                : [];
            return Task.FromResult(new EntityListResponse(items, null, items.Length));
        }

        public Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<EntityCard?>(null);

        public Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EntityThumbnailBatchResponse([]));

        public Task<IEntityCard?> GetDetailAsync(Guid id, string kind, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IEntityCard?>(null);

        private static EntityThumbnail Thumb(Guid id, string kind, string title) =>
            new(
                id,
                kind,
                title,
                ParentEntityId: null,
                SortOrder: null,
                CoverUrl: "/assets/cover.jpg",
                CoverThumbUrl: null,
                HoverKind: "none",
                HoverUrl: null,
                HoverImages: [],
                Meta: [new EntityThumbnailMeta("duration", "01:30")],
                Rating: null,
                IsFavorite: false,
                IsNsfw: false,
                IsOrganized: true);
    }
}
