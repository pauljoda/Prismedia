using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Collections;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Collections;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Jellyfin;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

/// <summary>
/// Covers the Jellyfin-compatibility endpoints Infuse probes on detail/show surfaces that Prismedia
/// added for client parity: Next Up, local trailers, special features, and media segments. The first
/// three previously 404'd; these tests pin their routing and the empty Jellyfin response shapes.
/// </summary>
public sealed class JellyfinCatalogExtrasEndpointTests {
    private static readonly Guid ItemId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PlaylistId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TrackId = Guid.Parse("33333333-3333-3333-3333-333333333333");

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
    public async Task PlaylistItemsReturnsPagedJsonResultWithPlaylistItemIds() {
        using var factory = CreateFactory(
            new PlaylistEntityReadService(PlaylistId, TrackId),
            new PlaylistCollectionItemReadService(PlaylistId, TrackId));
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            $"/Playlists/{PlaylistId:N}/Items?IncludeItemTypes=PlaylistItem&Fields=&Recursive=true&Limit=200&StartIndex=0&UserId={ItemId:N}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JellyfinQueryResult<JellyfinBaseItemDto>>();
        Assert.NotNull(body);
        var track = Assert.Single(body!.Items);
        Assert.Equal(TrackId, track.Id);
        Assert.Equal(JellyfinProtocol.ItemTypes.Audio, track.Type);
        Assert.Equal(PlaylistId, track.ParentId);
        Assert.Equal(TrackId.ToString("N"), track.PlaylistItemId);
        Assert.Equal(1, body.TotalRecordCount);
        Assert.Equal(0, body.StartIndex);
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
    public async Task SimilarItemsReturnsEmptyJellyfinPagedResult() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/Items/{ItemId}/Similar?Limit=5");
        var body = await response.Content.ReadFromJsonAsync<JellyfinQueryResult<JellyfinBaseItemDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(body);
        Assert.Empty(body.Items);
        Assert.Equal(0, body.TotalRecordCount);
        Assert.Equal(0, body.StartIndex);
    }

    [Theory]
    [InlineData("/Items/Images/Primary")]
    [InlineData("/Items/Images/Thumb")]
    [InlineData("/Items/Images/Backdrop")]
    public async Task MalformedItemImageProbeReturnsJsonNotSpaHtml(string path) {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"{path}?fillWidth=400&quality=80");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
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

    private static WebApplicationFactory<Program> CreateFactory(
        IEntityReadService? entityReadService = null,
        ICollectionItemReadService? collectionItemReadService = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.RemoveAll<IEntityReadService>();
                    services.RemoveAll<ICollectionItemReadService>();
                    services.AddSingleton(entityReadService ?? new TestAuth.VisibleEntityReadService());
                    services.AddSingleton(collectionItemReadService ?? new EmptyCollectionItemReadService());
                });
            })
            .WithTestAuth();

    private sealed class PlaylistEntityReadService(Guid playlistId, Guid trackId) : IEntityReadService {
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
            bool? orphaned = null) =>
            Task.FromResult(new EntityListResponse([], null, 0));

        public Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult(id == playlistId
                ? Card(playlistId, EntityKind.Collection, "Mixtape")
                : id == trackId
                    ? Card(trackId, EntityKind.AudioTrack, "Opening Track")
                    : null);

        public Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EntityThumbnailBatchResponse([]));

        public Task<IEntityCard?> GetDetailAsync(Guid id, string kind, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IEntityCard?>(id == playlistId
                ? Card(playlistId, EntityKind.Collection, "Mixtape")
                : id == trackId
                    ? Card(trackId, EntityKind.AudioTrack, "Opening Track")
                    : null);
    }

    private sealed class PlaylistCollectionItemReadService(Guid playlistId, Guid trackId) : ICollectionItemReadService {
        public Task<CollectionItemsResponse> ListItemsAsync(
            Guid collectionId,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult(collectionId == playlistId
                ? new CollectionItemsResponse([
                    new CollectionItemDetail(
                        Guid.Parse("44444444-4444-4444-4444-444444444444"),
                        playlistId,
                        EntityKind.AudioTrack,
                        trackId,
                        CollectionItemSource.Manual,
                        0,
                        DateTimeOffset.UtcNow,
                        Thumbnail(trackId, EntityKind.AudioTrack, "Opening Track"))
                ])
                : new CollectionItemsResponse([]));

        public Task<IReadOnlyDictionary<Guid, string>> ResolveCoverPathsAsync(
            IReadOnlyList<Guid> collectionIds,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class EmptyCollectionItemReadService : ICollectionItemReadService {
        public Task<CollectionItemsResponse> ListItemsAsync(
            Guid collectionId,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CollectionItemsResponse([]));

        public Task<IReadOnlyDictionary<Guid, string>> ResolveCoverPathsAsync(
            IReadOnlyList<Guid> collectionIds,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private static EntityCard Card(Guid id, EntityKind kind, string title) =>
        new() {
            Id = id,
            Kind = kind,
            Title = title,
            ParentEntityId = null,
            SortOrder = null,
            Capabilities = [],
            ChildrenByKind = [],
            Relationships = []
        };

    private static EntityThumbnail Thumbnail(Guid id, EntityKind kind, string title) =>
        new(
            id,
            kind,
            title,
            ParentEntityId: null,
            SortOrder: null,
            CoverUrl: "/assets/cover.jpg",
            CoverThumbUrl: null,
            HoverKind: ThumbnailHoverKind.None,
            HoverUrl: null,
            HoverImages: [],
            Meta: [new EntityThumbnailMeta("duration", "01:30")],
            Rating: null,
            IsFavorite: false,
            IsNsfw: false,
            IsOrganized: true);
}
