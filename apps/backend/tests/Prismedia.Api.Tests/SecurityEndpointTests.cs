using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Api.Security;
using Prismedia.Application.Collections;
using Prismedia.Application.Entities;
using Prismedia.Application.Jellyfin;
using Prismedia.Contracts.Collections;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Jellyfin;
using Prismedia.Contracts.Security;
using Prismedia.Domain.Entities;
using Prismedia.Contracts.Videos;

namespace Prismedia.Api.Tests;

public sealed partial class SecurityEndpointTests : IDisposable {
    private static readonly Guid SfwVideoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NsfwVideoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), $"prismedia-security-static-{Guid.NewGuid():N}");
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), $"prismedia-security-cache-{Guid.NewGuid():N}");

    public SecurityEndpointTests() {
        Directory.CreateDirectory(_webRoot);
        Directory.CreateDirectory(_cacheRoot);
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<html><body>Prismedia</body></html>");
    }

    [Fact]
    public async Task ProtectedApiRoutesRequireApiKey() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/items")]
    [InlineData("/videos/11111111-1111-1111-1111-111111111111/stream")]
    [InlineData("/sessions/playing/progress")]
    [InlineData("/library/mediafolders")]
    public async Task ProtectedJellyfinRoutesRequireApiKeyForLowercaseUrls(string path) {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BootstrapNavigationSetsHttpOnlyApiKeyCookie() {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Prismedia:StaticWebRoot", _webRoot))
            .WithTestAuth();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions {
            HandleCookies = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/library");
        request.Headers.Accept.ParseAdd("text/html");

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("prismedia-api-key=", StringComparison.Ordinal));
        Assert.Contains("HttpOnly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BootstrapNavigationRefusesNonLoopbackClients() {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.24");
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/library";
        context.Request.Headers.Accept = "text/html";

        var shouldBootstrap = PrismediaAuthentication.ShouldBootstrapCookie(context.Request);

        Assert.False(shouldBootstrap);
    }

    [Fact]
    public void BootstrapNavigationAllowsProxiedUiNavigationFromPrivateProxyNetwork() {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.22.0.5");
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/library";
        context.Request.Host = new HostString("dev-prismedia.pauljoda.com");
        context.Request.Headers.Accept = "text/html";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.8";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-Host"] = "dev-prismedia.pauljoda.com";

        var shouldBootstrap = PrismediaAuthentication.ShouldBootstrapCookie(context.Request);

        Assert.True(shouldBootstrap);
    }

    [Theory]
    [InlineData("/api/settings")]
    [InlineData("/assets/videos/thumb.jpg")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/Items/11111111-1111-1111-1111-111111111111")]
    public void BootstrapNavigationRefusesNonUiRoutesBehindProxy(string path) {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.22.0.5");
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Request.Host = new HostString("dev-prismedia.pauljoda.com");
        context.Request.Headers.Accept = "text/html";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.8";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-Host"] = "dev-prismedia.pauljoda.com";

        var shouldBootstrap = PrismediaAuthentication.ShouldBootstrapCookie(context.Request);

        Assert.False(shouldBootstrap);
    }

    [Fact]
    public void BootstrapNavigationRefusesForwardedHeadersFromPublicRemoteClient() {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.24");
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/library";
        context.Request.Host = new HostString("dev-prismedia.pauljoda.com");
        context.Request.Headers.Accept = "text/html";
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.8";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-Host"] = "dev-prismedia.pauljoda.com";

        var shouldBootstrap = PrismediaAuthentication.ShouldBootstrapCookie(context.Request);

        Assert.False(shouldBootstrap);
    }

    [Fact]
    public async Task ApiKeyHeaderAcceptsHumanNormalizedInput() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Prismedia-Api-Key", " BAVA CADA DAFA ");

        var response = await client.GetFromJsonAsync<ApiKeyResponse>("/api/security/api-key");

        Assert.NotNull(response);
        Assert.Equal(TestAuth.ApiKey, response.ApiKey);
    }

    [Fact]
    public async Task InvalidApiKeyAttemptsAreThrottled() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        HttpResponseMessage? response = null;

        for (var i = 0; i < 9; i++) {
            response?.Dispose();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/security/api-key");
            request.Headers.Add("X-Prismedia-Api-Key", $"bad-key-{i}");
            response = await client.SendAsync(request);
        }

        using (response) {
            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
        }
    }

    [Fact]
    public async Task JellyfinAuthenticationCreatesSessionAndRegenerationInvalidatesIt() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var auth = await AuthenticateAsync(client, "Prismedia", TestAuth.ApiKey);
        using var me = await JellyfinGetAsync(client, "/Users/Me", auth.AccessToken);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var nativeTokenRequest = new HttpRequestMessage(HttpMethod.Get, "/api/security/api-key");
        nativeTokenRequest.Headers.Add("X-Emby-Token", auth.AccessToken);
        using var nativeTokenResponse = await client.SendAsync(nativeTokenRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, nativeTokenResponse.StatusCode);

        using var appClient = factory.CreateAuthenticatedClient();
        using var rotationResponse = await appClient.PostAsJsonAsync("/api/security/api-key/regenerate", new { });
        var rotation = await rotationResponse.Content.ReadFromJsonAsync<ApiKeyRegenerateResponse>();

        Assert.NotNull(rotation);
        Assert.Matches(HumanKeyRegex(), rotation.ApiKey);
        Assert.Equal(1, rotation.InvalidatedSessions);
        using var invalidated = await JellyfinGetAsync(client, "/Users/Me", auth.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidated.StatusCode);
    }

    [Fact]
    public async Task JellyfinConnectionCompatibilityRoutesAcceptCommonClientProbes() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var ping = await client.PostAsync("/System/Ping", null);
        var publicInfo = await client.GetFromJsonAsync<JellyfinPublicSystemInfo>("/System/Info/Public");
        var users = await client.GetFromJsonAsync<IReadOnlyList<JellyfinUserDto>>("/Users/Public");
        var user = Assert.Single(users!);
        using var passwordFieldResponse = await client.PostAsJsonAsync(
            "/Users/AuthenticateByName",
            new { Username = "Prismedia", Password = TestAuth.ApiKey });
        using var legacyResponse = await client.PostAsync(
            $"/Users/{user.Id:D}/Authenticate?pw={Uri.EscapeDataString(TestAuth.ApiKey)}",
            null);

        Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
        Assert.NotNull(publicInfo);
        Assert.True(passwordFieldResponse.IsSuccessStatusCode);
        Assert.True(legacyResponse.IsSuccessStatusCode);

        var auth = await passwordFieldResponse.Content.ReadFromJsonAsync<JellyfinAuthenticationResult>();
        Assert.NotNull(auth);
        var groupingOptions = await JellyfinGetFromJsonAsync<IReadOnlyList<JellyfinSpecialViewOptionDto>>(
            client,
            $"/Users/{auth.User.Id:D}/GroupingOptions",
            auth.AccessToken);

        Assert.NotNull(groupingOptions);
        Assert.Equal(
            ["Collections", "Movies", "Music", "Series", "Unwatched Movies", "Unwatched Series", "Videos"],
            groupingOptions.Select(item => item.Name ?? "").ToArray());

        var virtualFolders = await JellyfinGetFromJsonAsync<IReadOnlyList<JellyfinVirtualFolderInfoDto>>(
            client,
            "/Library/VirtualFolders",
            auth.AccessToken);

        Assert.NotNull(virtualFolders);
        Assert.Equal(
            ["Movies", "Unwatched Movies", "Videos", "Series", "Unwatched Series", "Collections", "Music"],
            virtualFolders.Select(item => item.Name).ToArray());
        Assert.All(virtualFolders, folder => Assert.True(folder.LibraryOptions.Enabled));
    }

    [Fact]
    public async Task JellyfinProfileControlsSfwAndNsfwCatalogVisibility() {
        using var factory = CreateFactory(new CatalogEntityReadService());
        using var client = factory.CreateClient();

        var sfwAuth = await AuthenticateAsync(client, "Prismedia", TestAuth.ApiKey);
        var sfwVisible = await JellyfinGetFromJsonAsync<JellyfinBaseItemDto>(
            client,
            $"/Items/{SfwVideoId:D}",
            sfwAuth.AccessToken);
        using var sfwHidden = await JellyfinGetAsync(client, $"/Items/{NsfwVideoId:D}", sfwAuth.AccessToken);
        Assert.NotNull(sfwVisible);
        Assert.Equal(HttpStatusCode.NotFound, sfwHidden.StatusCode);

        using var appClient = factory.CreateAuthenticatedClient();
        using var adultProfileResponse = await appClient.PostAsJsonAsync(
            "/api/security/jellyfin-profiles",
            new JellyfinProfileCreateRequest("Adult", null, AllowNsfw: true));
        adultProfileResponse.EnsureSuccessStatusCode();

        var adultAuth = await AuthenticateAsync(client, "Adult", TestAuth.ApiKey);
        var bothSfw = await JellyfinGetFromJsonAsync<JellyfinBaseItemDto>(
            client,
            $"/Items/{SfwVideoId:D}",
            adultAuth.AccessToken);
        var bothNsfw = await JellyfinGetFromJsonAsync<JellyfinBaseItemDto>(
            client,
            $"/Items/{NsfwVideoId:D}",
            adultAuth.AccessToken);

        Assert.NotNull(bothSfw);
        Assert.NotNull(bothNsfw);

        using var nsfwOnlyProfileResponse = await appClient.PostAsJsonAsync(
            "/api/security/jellyfin-profiles",
            new JellyfinProfileCreateRequest("AdultOnly", null, AllowSfw: false, AllowNsfw: true));
        nsfwOnlyProfileResponse.EnsureSuccessStatusCode();

        var nsfwOnlyAuth = await AuthenticateAsync(client, "AdultOnly", TestAuth.ApiKey);
        using var nsfwOnlySfwHidden = await JellyfinGetAsync(client, $"/Items/{SfwVideoId:D}", nsfwOnlyAuth.AccessToken);
        var nsfwOnlyNsfwVisible = await JellyfinGetFromJsonAsync<JellyfinBaseItemDto>(
            client,
            $"/Items/{NsfwVideoId:D}",
            nsfwOnlyAuth.AccessToken);
        var nsfwOnlyBrowse = await JellyfinGetFromJsonAsync<JellyfinQueryResult<JellyfinBaseItemDto>>(
            client,
            $"/Items?ParentId={JellyfinCatalogService.VideosViewId:D}",
            nsfwOnlyAuth.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, nsfwOnlySfwHidden.StatusCode);
        Assert.NotNull(nsfwOnlyNsfwVisible);
        Assert.Equal([NsfwVideoId], nsfwOnlyBrowse!.Items.Select(item => item.Id).ToArray());

        using var emptyProfileResponse = await appClient.PostAsJsonAsync(
            "/api/security/jellyfin-profiles",
            new JellyfinProfileCreateRequest("Empty", null, AllowSfw: false, AllowNsfw: false));
        emptyProfileResponse.EnsureSuccessStatusCode();

        var emptyAuth = await AuthenticateAsync(client, "Empty", TestAuth.ApiKey);
        using var emptySfwHidden = await JellyfinGetAsync(client, $"/Items/{SfwVideoId:D}", emptyAuth.AccessToken);
        using var emptyNsfwHidden = await JellyfinGetAsync(client, $"/Items/{NsfwVideoId:D}", emptyAuth.AccessToken);
        var emptyBrowse = await JellyfinGetFromJsonAsync<JellyfinQueryResult<JellyfinBaseItemDto>>(
            client,
            $"/Items?ParentId={JellyfinCatalogService.VideosViewId:D}",
            emptyAuth.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, emptySfwHidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, emptyNsfwHidden.StatusCode);
        Assert.Empty(emptyBrowse!.Items);
    }

    [Fact]
    public async Task JellyfinRootCatalogRoutesReturnVirtualLibraries() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var auth = await AuthenticateAsync(client, "Prismedia", TestAuth.ApiKey);

        var views = await JellyfinGetFromJsonAsync<JellyfinQueryResult<JellyfinBaseItemDto>>(
            client,
            "/UserViews",
            auth.AccessToken);
        var rootItems = await JellyfinGetFromJsonAsync<JellyfinQueryResult<JellyfinBaseItemDto>>(
            client,
            $"/Items?ApiKey={Uri.EscapeDataString(auth.AccessToken)}",
            token: null);

        Assert.NotNull(views);
        Assert.Equal(
            ["Movies", "Unwatched Movies", "Videos", "Series", "Unwatched Series", "Collections", "Music"],
            views.Items.Select(item => item.Name).ToArray());
        Assert.NotNull(rootItems);
        Assert.Equal(7, rootItems.TotalRecordCount);
    }

    [Fact]
    public async Task JellyfinBrowseItemsExposePlayableCatalogShapeForInfuse() {
        using var factory = CreateFactory(new InfuseBrowseEntityReadService());
        using var client = factory.CreateClient();
        var auth = await AuthenticateAsync(client, "Prismedia", TestAuth.ApiKey);

        var videos = await JellyfinGetFromJsonAsync<JellyfinQueryResult<JellyfinBaseItemDto>>(
            client,
            $"/Users/{auth.User.Id:D}/Items?ParentId={JellyfinCatalogService.VideosViewId:D}&IncludeItemTypes=Video&Fields=MediaSources,MediaStreams",
            auth.AccessToken);
        var standalone = Assert.Single(videos!.Items);

        Assert.Equal("Standalone Video", standalone.Name);
        Assert.Null(standalone.ParentId);
        Assert.Equal("FileSystem", standalone.LocationType);
        Assert.Equal("Full", standalone.PlayAccess);
        Assert.True(standalone.RunTimeTicks > 0);
        Assert.Single(standalone.MediaSources!);
        Assert.Single(standalone.MediaStreams!);

        var seriesItems = await JellyfinGetFromJsonAsync<JellyfinQueryResult<JellyfinBaseItemDto>>(
            client,
            $"/Users/{auth.User.Id:D}/Items?ParentId={JellyfinCatalogService.SeriesViewId:D}",
            auth.AccessToken);
        var series = Assert.Single(seriesItems!.Items);

        Assert.Equal(1, series.ChildCount);
        Assert.Equal(1, series.RecursiveItemCount);

        var seasons = await JellyfinGetFromJsonAsync<JellyfinQueryResult<JellyfinBaseItemDto>>(
            client,
            $"/Shows/{series.Id:D}/Seasons",
            auth.AccessToken);
        var season = Assert.Single(seasons!.Items);

        Assert.Equal(InfuseBrowseEntityReadService.SeasonId, season.Id);
        Assert.Equal("Season 1", season.Name);
        Assert.Equal(series.Id, season.ParentId);
        Assert.Equal(1, season.ChildCount);

        var episodes = await JellyfinGetFromJsonAsync<JellyfinQueryResult<JellyfinBaseItemDto>>(
            client,
            $"/Users/{auth.User.Id:D}/Items?ParentId={series.Id:D}&Recursive=true&IncludeItemTypes=Episode&Fields=MediaSources,MediaStreams",
            auth.AccessToken);
        var episode = Assert.Single(episodes!.Items);

        Assert.Equal("Pilot", episode.Name);
        Assert.Equal(series.Id, episode.SeriesId);
        Assert.Equal("The Chair Company", episode.SeriesName);
        Assert.Equal(InfuseBrowseEntityReadService.SeasonId, episode.SeasonId);
        Assert.Equal(1, episode.ParentIndexNumber);
        Assert.Single(episode.MediaSources!);
        Assert.Single(episode.MediaStreams!);

        var showEpisodes = await JellyfinGetFromJsonAsync<JellyfinQueryResult<JellyfinBaseItemDto>>(
            client,
            $"/Shows/{series.Id:D}/Episodes?SeasonId={InfuseBrowseEntityReadService.SeasonId:D}&Fields=MediaSources,MediaStreams",
            auth.AccessToken);
        var showEpisode = Assert.Single(showEpisodes!.Items);

        Assert.Equal(episode.Id, showEpisode.Id);
        Assert.Equal(InfuseBrowseEntityReadService.SeasonId, showEpisode.ParentId);
        Assert.Single(showEpisode.MediaSources!);
    }

    [Fact]
    public async Task JellyfinDirectChildEpisodePrimaryImageEndpointServesAdvertisedThumbnail() {
        var imagePath = Path.Combine(_cacheRoot, "videos", "direct-episode.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        File.WriteAllBytes(imagePath, [0xff, 0xd8, 0xff, 0xd9]);
        using var factory = CreateFactory(
            new DirectChildEpisodeArtworkEntityReadService(),
            cacheRoot: _cacheRoot);
        using var client = factory.CreateClient();
        var auth = await AuthenticateAsync(client, "Prismedia", TestAuth.ApiKey);

        var episodes = await JellyfinGetFromJsonAsync<JellyfinQueryResult<JellyfinBaseItemDto>>(
            client,
            $"/Shows/{DirectChildEpisodeArtworkEntityReadService.SeriesId:D}/Episodes",
            auth.AccessToken);
        var episode = Assert.Single(episodes!.Items);
        Assert.True(episode.ImageTags!.TryGetValue("Primary", out var tag));

        using var image = await JellyfinGetAsync(
            client,
            $"/Items/{DirectChildEpisodeArtworkEntityReadService.EpisodeId:D}/Images/Primary",
            auth.AccessToken);

        image.EnsureSuccessStatusCode();
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"\"{tag}\"", image.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task JellyfinItemDetailExposesPrismediaMetadataFields() {
        using var factory = CreateFactory(new JellyfinMetadataEntityReadService());
        using var client = factory.CreateClient();
        var auth = await AuthenticateAsync(client, "Prismedia", TestAuth.ApiKey);

        var item = await JellyfinGetFromJsonAsync<JellyfinBaseItemDto>(
            client,
            $"/Items/{JellyfinMetadataEntityReadService.VideoId:D}",
            auth.AccessToken);

        Assert.NotNull(item);
        Assert.Equal("Rich Movie", item.Name);
        Assert.Equal("A mapped Prismedia overview.", item.Overview);
        Assert.Equal(new DateTimeOffset(2024, 5, 4, 0, 0, 0, TimeSpan.Zero), item.PremiereDate);
        Assert.Equal(2024, item.ProductionYear);
        Assert.Equal("R", item.OfficialRating);
        Assert.Equal(8, item.CommunityRating);
        Assert.Equal(1920, item.Width);
        Assert.Equal(1080, item.Height);
        Assert.Equal("16:9", item.AspectRatio);
        Assert.True(item.IsHD);
        Assert.True(item.HasSubtitles);
        Assert.Contains("Logo", item.ImageTags.Keys);
        Assert.Single(item.BackdropImageTags);

        Assert.Equal(["Adventure", "Cozy"], item.Tags);
        Assert.Equal(["Adventure", "Cozy"], item.Genres);
        Assert.Equal(["Adventure", "Cozy"], item.GenreItems!.Select(genre => genre.Name).ToArray());

        var studio = Assert.Single(item.Studios!);
        Assert.Equal("Studio One", studio.Name);
        Assert.Equal(JellyfinMetadataEntityReadService.StudioId, studio.Id);

        Assert.Equal("tt1234567", item.ProviderIds!["imdb"]);
        Assert.Contains(item.ExternalUrls!, url => url.Name == "IMDb" && url.Url == "https://www.imdb.com/title/tt1234567/");

        Assert.Equal(["Opening"], item.Chapters!.Select(chapter => chapter.Name).ToArray());
        Assert.Equal(TimeSpan.FromSeconds(12).Ticks, item.Chapters![0].StartPositionTicks);

        var people = item.People!.OrderBy(person => person.Name, StringComparer.Ordinal).ToArray();
        Assert.Collection(
            people,
            person => {
                Assert.Equal("Actor One", person.Name);
                Assert.Equal(JellyfinMetadataEntityReadService.ActorId, person.Id);
                Assert.Equal("Hero", person.Role);
                Assert.Equal("Actor", person.Type);
            },
            person => {
                Assert.Equal("Director One", person.Name);
                Assert.Equal(JellyfinMetadataEntityReadService.DirectorId, person.Id);
                Assert.Equal("Director", person.Role);
                Assert.Equal("Director", person.Type);
            });

        Assert.Contains(item.MediaStreams!, stream =>
            stream.Type == "Subtitle" &&
            stream.Language == "en" &&
            stream.DisplayTitle == "English SDH");
    }

    public void Dispose() {
        if (Directory.Exists(_webRoot)) {
            Directory.Delete(_webRoot, recursive: true);
        }

        if (Directory.Exists(_cacheRoot)) {
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IEntityReadService? entityReadService = null,
        IJellyfinImageFileService? imageFileService = null,
        string? cacheRoot = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                if (cacheRoot is not null) {
                    builder.UseSetting("Prismedia:CacheDir", cacheRoot);
                }

                builder.ConfigureServices(services => {
                    services.AddSingleton(entityReadService ?? new TestAuth.VisibleEntityReadService());
                    services.AddSingleton<ICollectionItemReadService, EmptyCollectionItemReadService>();
                    if (imageFileService is not null) {
                        services.AddSingleton(imageFileService);
                    }
                });
            })
            .WithTestAuth();

    private static async Task<JellyfinAuthenticationResult> AuthenticateAsync(
        HttpClient client,
        string username,
        string password) {
        using var response = await client.PostAsJsonAsync(
            "/Users/AuthenticateByName",
            new JellyfinAuthenticateByNameRequest {
                Username = username,
                Password = password
            });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JellyfinAuthenticationResult>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
        return body;
    }

    private static async Task<HttpResponseMessage> JellyfinGetAsync(HttpClient client, string path, string token) {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Emby-Token", token);
        return await client.SendAsync(request);
    }

    private static async Task<T?> JellyfinGetFromJsonAsync<T>(HttpClient client, string path, string? token) {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrWhiteSpace(token)) {
            request.Headers.Add("X-Emby-Token", token);
        }

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>();
    }

    [GeneratedRegex("^[a-z]{3,5}-[a-z]{3,5}-[a-z]{3,5}$", RegexOptions.CultureInvariant)]
    private static partial Regex HumanKeyRegex();

    private sealed class CatalogEntityReadService : IEntityReadService {
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
            var items = new[] {
                    Thumbnail(SfwVideoId, "Visible Movie", isNsfw: false),
                    Thumbnail(NsfwVideoId, "Hidden Movie", isNsfw: true)
                }
                .Where(item => hideNsfw != true || !item.IsNsfw)
                .Where(item => nsfw is null || item.IsNsfw == nsfw)
                .ToArray();
            return Task.FromResult(new EntityListResponse(items, null, items.Length));
        }

        public Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) {
            if (id == NsfwVideoId && hideNsfw) {
                return Task.FromResult<EntityCard?>(null);
            }

            return Task.FromResult<EntityCard?>(id == SfwVideoId ? Card(id, "Visible Movie", isNsfw: false) :
                id == NsfwVideoId ? Card(id, "Hidden Movie", isNsfw: true) :
                null);
        }

        public Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EntityThumbnailBatchResponse([]));

        public Task<IEntityCard?> GetDetailAsync(Guid id, string kind, bool hideNsfw, CancellationToken cancellationToken) {
            if (id == NsfwVideoId && hideNsfw) {
                return Task.FromResult<IEntityCard?>(null);
            }

            return Task.FromResult<IEntityCard?>(id == SfwVideoId ? Card(id, "Visible Movie", isNsfw: false) with { Kind = kind.DecodeAs<EntityKind>() } :
                id == NsfwVideoId ? Card(id, "Hidden Movie", isNsfw: true) with { Kind = kind.DecodeAs<EntityKind>() } :
                null);
        }

        private static EntityCard Card(Guid id, string title, bool isNsfw) =>
            new() {
                Id = id,
                Kind = EntityKind.Video,
                Title = title,
                ParentEntityId = null,
                SortOrder = null,
                Capabilities = [new FlagsCapability(IsFavorite: false, IsNsfw: isNsfw, IsOrganized: true)],
                ChildrenByKind = [],
                Relationships = []
            };

        private static EntityThumbnail Thumbnail(Guid id, string title, bool isNsfw) =>
            new(
                id,
                EntityKind.Video,
                title,
                null,
                null,
                null,
                null,
                ThumbnailHoverKind.None,
                null,
                [],
                [],
                null,
                false,
                isNsfw,
                true);
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

    private sealed class StaticJellyfinImageFileService(string filePath) : IJellyfinImageFileService {
        public Task<JellyfinImageFile?> ResolveAsync(JellyfinImageAsset asset, CancellationToken cancellationToken) =>
            Task.FromResult<JellyfinImageFile?>(new JellyfinImageFile(
                filePath,
                null,
                asset.ContentType,
                asset.ImageTag));
    }

    private sealed class DirectChildEpisodeArtworkEntityReadService : IEntityReadService {
        public static readonly Guid SeriesId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        public static readonly Guid EpisodeId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        private const string CoverPath = "/assets/videos/direct-episode.jpg";

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
            Task.FromResult<EntityCard?>(id == SeriesId ? SeriesCard() :
                id == EpisodeId ? EpisodeCard() :
                null);

        public Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EntityThumbnailBatchResponse(ids.Contains(EpisodeId) ? [EpisodeThumbnail()] : []));

        public Task<IEntityCard?> GetDetailAsync(Guid id, string kind, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IEntityCard?>(id == EpisodeId ? EpisodeCard() : null);

        private static EntityCard SeriesCard() =>
            new() {
                Id = SeriesId,
                Kind = EntityKind.VideoSeries,
                Title = "Direct Show",
                ParentEntityId = null,
                SortOrder = null,
                Capabilities = [],
                ChildrenByKind = [new EntityGroup(EntityKind.Video, "Episodes", [EpisodeThumbnail()])],
                Relationships = []
            };

        private static EntityCard EpisodeCard() =>
            new() {
                Id = EpisodeId,
                Kind = EntityKind.Video,
                Title = "Episode 1",
                ParentEntityId = SeriesId,
                SortOrder = 0,
                Capabilities = [],
                ChildrenByKind = [],
                Relationships = []
            };

        private static EntityThumbnail EpisodeThumbnail() =>
            new(
                EpisodeId,
                EntityKind.Video,
                "Episode 1",
                SeriesId,
                0,
                CoverPath,
                null,
                ThumbnailHoverKind.None,
                null,
                [],
                [new EntityThumbnailMeta("duration", "42:00")],
                null,
                false,
                false,
                true);
    }

    private sealed class InfuseBrowseEntityReadService : IEntityReadService {
        private static readonly Guid StandaloneId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid SeriesId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        public static readonly Guid SeasonId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        private static readonly Guid EpisodeId = Guid.Parse("55555555-5555-5555-5555-555555555555");

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
            IReadOnlyList<EntityThumbnail> items = kind switch {
                "video" => [Thumbnail(StandaloneId, EntityKind.Video, "Standalone Video", null, null), Thumbnail(EpisodeId, EntityKind.Video, "Pilot", SeasonId, 1)],
                "video-series" => [Thumbnail(SeriesId, EntityKind.VideoSeries, "The Chair Company", null, null)],
                _ => []
            };
            return Task.FromResult(new EntityListResponse(items, null, items.Count));
        }

        public Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<EntityCard?>(id == StandaloneId ? VideoCard(id, "Standalone Video", null, null, "/media/standalone.mkv") :
                id == SeriesId ? SeriesCard() :
                id == SeasonId ? SeasonCard() :
                id == EpisodeId ? VideoCard(id, "Pilot", SeasonId, 1, "/media/the-chair-company/s01e01.mkv") :
                null);

        public Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EntityThumbnailBatchResponse([]));

        public Task<IEntityCard?> GetDetailAsync(Guid id, string kind, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IEntityCard?>(null);

        private static EntityCard SeriesCard() =>
            new() {
                Id = SeriesId,
                Kind = EntityKind.VideoSeries,
                Title = "The Chair Company",
                ParentEntityId = null,
                SortOrder = null,
                Capabilities = [],
                ChildrenByKind = [new EntityGroup(EntityKind.VideoSeason, "Seasons", [Thumbnail(SeasonId, EntityKind.VideoSeason, "Season 1", SeriesId, 1)])],
                Relationships = []
            };

        private static EntityCard SeasonCard() =>
            new() {
                Id = SeasonId,
                Kind = EntityKind.VideoSeason,
                Title = "Season 1",
                ParentEntityId = SeriesId,
                SortOrder = 1,
                Capabilities = [new PositionCapability([new EntityPosition("season", 1)])],
                ChildrenByKind = [new EntityGroup(EntityKind.Video, "Episodes", [Thumbnail(EpisodeId, EntityKind.Video, "Pilot", SeasonId, 1)])],
                Relationships = []
            };

        private static EntityCard VideoCard(Guid id, string title, Guid? parentId, int? sortOrder, string path) =>
            new() {
                Id = id,
                Kind = EntityKind.Video,
                Title = title,
                ParentEntityId = parentId,
                SortOrder = sortOrder,
                Capabilities = [
                    new TechnicalCapability(TimeSpan.FromMinutes(42), 1920, 1080, 23.976, 4_000_000, null, null, "h264", "mkv", "matroska"),
                    new FilesCapability([new Contracts.Entities.EntityFile("source", path, "video/x-matroska")]),
                    new PositionCapability(sortOrder is null ? [] : [new EntityPosition("episode", sortOrder.Value)])
                ],
                ChildrenByKind = [],
                Relationships = []
            };

        private static EntityThumbnail Thumbnail(Guid id, EntityKind kind, string title, Guid? parentId, int? sortOrder) =>
            new(
                id,
                kind,
                title,
                parentId,
                sortOrder,
                null,
                null,
                ThumbnailHoverKind.None,
                null,
                [],
                [new EntityThumbnailMeta("duration", "42:00"), new EntityThumbnailMeta("video", "1080p"), new EntityThumbnailMeta("video", "H264"), new EntityThumbnailMeta("video", "mkv")],
                null,
                false,
                false,
                true);
    }

    private sealed class JellyfinMetadataEntityReadService : IEntityReadService {
        public static readonly Guid VideoId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public static readonly Guid ActorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        public static readonly Guid DirectorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        public static readonly Guid StudioId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        private static readonly Guid AdventureTagId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        private static readonly Guid CozyTagId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

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
            Task.FromResult(new EntityListResponse([Thumbnail(VideoId, EntityKind.Video, "Rich Movie", null, null)], null, 1));

        public Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<EntityCard?>(id == VideoId ? Card() : null);

        public Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EntityThumbnailBatchResponse([]));

        public Task<IEntityCard?> GetDetailAsync(Guid id, string kind, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IEntityCard?>(id == VideoId && kind == "video" ? Detail() : null);

        private static EntityCard Card() =>
            new() {
                Id = VideoId,
                Kind = EntityKind.Video,
                Title = "Rich Movie",
                ParentEntityId = null,
                SortOrder = null,
                Capabilities = Capabilities(),
                ChildrenByKind = [],
                Relationships = Relationships()
            };

        private static VideoDetail Detail() =>
            new() {
                Id = VideoId,
                Kind = EntityKind.Video,
                Title = "Rich Movie",
                ParentEntityId = null,
                SortOrder = null,
                Capabilities = Capabilities(),
                ChildrenByKind = [],
                Relationships = Relationships(),
                CreditMetadata = [
                    new EntityCreditMetadata(ActorId, "actor", "Hero"),
                    new EntityCreditMetadata(DirectorId, "director", null)
                ],
                SubtitlesExtractedAt = null
            };

        private static IReadOnlyList<EntityCapability> Capabilities() =>
            [
                new DescriptionCapability("A mapped Prismedia overview."),
                new RatingCapability(4),
                new FlagsCapability(IsFavorite: true, IsNsfw: false, IsOrganized: true),
                new TechnicalCapability(TimeSpan.FromMinutes(95), 1920, 1080, 24, 6_000_000, null, null, "h264", "mkv", "matroska"),
                new DatesCapability([
                    new EntityDate("release", "2024-05-04", new DateOnly(2024, 5, 4), "day")
                ]),
                new ClassificationCapability("R", "mpaa"),
                new LinksCapability(
                    [new Contracts.Entities.EntityUrl("https://example.test/rich-movie", "Home")],
                    [new Contracts.Entities.EntityExternalId("imdb", "tt1234567", "https://www.imdb.com/title/tt1234567/")]),
                new ImagesCapability(
                    ["thumbnail", "poster", "backdrop", "logo"],
                    [
                        new EntityImageAsset("poster", "/assets/poster.jpg", "image/jpeg"),
                        new EntityImageAsset("backdrop", "/assets/backdrop.jpg", "image/jpeg"),
                        new EntityImageAsset("logo", "/assets/logo.png", "image/png")
                    ],
                    "/assets/poster-thumb.jpg",
                    "/assets/poster.jpg"),
                new FilesCapability([new Contracts.Entities.EntityFile("source", "/media/rich-movie.mkv", "video/x-matroska")]),
                new MarkersCapability([new EntityMarker(Guid.Parse("99999999-9999-9999-9999-999999999999"), "Opening", 12, null)]),
                new SubtitlesCapability([
                    new EntitySubtitle(Guid.Parse("12121212-1212-1212-1212-121212121212"), "en", "English SDH", "vtt", EntitySubtitleSource.Sidecar, "/subs/rich.en.vtt", "srt", "/subs/rich.en.srt", true)
                ])
            ];

        private static IReadOnlyList<EntityGroup> Relationships() =>
            [
                new EntityGroup(EntityKind.Person, "Cast", [Thumbnail(ActorId, EntityKind.Person, "Actor One", null, null)]) { Code = RelationshipKind.Cast },
                new EntityGroup(EntityKind.Person, "Credits", [Thumbnail(DirectorId, EntityKind.Person, "Director One", null, null)]) { Code = RelationshipKind.Credits },
                new EntityGroup(EntityKind.Studio, "Studios", [Thumbnail(StudioId, EntityKind.Studio, "Studio One", null, null)]) { Code = RelationshipKind.Studio },
                new EntityGroup(EntityKind.Tag, "Tags", [
                    Thumbnail(AdventureTagId, EntityKind.Tag, "Adventure", null, null),
                    Thumbnail(CozyTagId, EntityKind.Tag, "Cozy", null, null)
                ]) { Code = RelationshipKind.Tags }
            ];

        private static EntityThumbnail Thumbnail(Guid id, EntityKind kind, string title, Guid? parentId, int? sortOrder) =>
            new(
                id,
                kind,
                title,
                parentId,
                sortOrder,
                null,
                null,
                ThumbnailHoverKind.None,
                null,
                [],
                [],
                null,
                false,
                false,
                true);
    }
}
