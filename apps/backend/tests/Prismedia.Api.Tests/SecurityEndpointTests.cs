using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Collections;
using Prismedia.Application.Entities;
using Prismedia.Application.Jellyfin;
using Prismedia.Contracts.Collections;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Jellyfin;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.Security;
using Prismedia.Domain.Entities;
using Prismedia.Contracts.Videos;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Api.Tests;

public sealed class SecurityEndpointTests : IDisposable {
    private static readonly JsonSerializerOptions CodecJson =
        new(JsonSerializerDefaults.Web) { Converters = { new CodecJsonConverterFactory() } };

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

    [Theory]
    [InlineData("/")]
    [InlineData("/series")]
    public async Task SpaShellResponsesDisableBrowserCache(string path) {
        using var factory = CreateFactory(cacheRoot: _cacheRoot, staticWebRoot: _webRoot);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaContentTypes.Html, response.Content.Headers.ContentType?.MediaType);
        var cacheControl = response.Headers.CacheControl?.ToString() ?? string.Empty;
        Assert.Contains("no-store", cacheControl);
        Assert.Contains("no-cache", cacheControl);
        Assert.Contains("must-revalidate", cacheControl);
    }

    [Fact]
    public async Task JellyfinAuthenticationCreatesUnifiedSessionAndPasswordChangeInvalidatesIt() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var auth = await AuthenticateAsync(client, TestAuth.Username, TestAuth.Password);
        using var me = await JellyfinGetAsync(client, "/Users/Me", auth.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        // Sessions are unified: a Jellyfin-issued token also authenticates /api routes.
        using var apiRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        apiRequest.Headers.Add("X-Emby-Token", auth.AccessToken);
        using var apiResponse = await client.SendAsync(apiRequest);
        Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);

        // Changing the password from the web session invalidates the other (Jellyfin) session.
        using var appClient = factory.CreateAuthenticatedClient();
        using var changeResponse = await appClient.PostAsJsonAsync(
            "/api/auth/password",
            new ChangeOwnPasswordRequest(TestAuth.Password, "a-brand-new-password"));
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);

        using var invalidated = await JellyfinGetAsync(client, "/Users/Me", auth.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidated.StatusCode);
        using var stillSignedIn = await appClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, stillSignedIn.StatusCode);
    }

    [Fact]
    public async Task JellyfinConnectionCompatibilityRoutesAcceptCommonClientProbes() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var ping = await client.PostAsync("/System/Ping", null);
        var publicInfo = await client.GetFromJsonAsync<JellyfinPublicSystemInfo>("/System/Info/Public");
        var users = await client.GetFromJsonAsync<IReadOnlyList<JellyfinUserDto>>("/Users/Public");
        var user = Assert.Single(users!);
        using var splashscreen = await client.GetAsync("/Branding/Splashscreen");
        using var passwordFieldResponse = await client.PostAsJsonAsync(
            "/Users/AuthenticateByName",
            new { Username = TestAuth.Username, Password = TestAuth.Password });
        using var legacyResponse = await client.PostAsync(
            $"/Users/{user.Id:D}/Authenticate?pw={Uri.EscapeDataString(TestAuth.Password)}",
            null);

        Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
        Assert.NotNull(publicInfo);
        Assert.Equal("Prismedia", publicInfo.ServerName);
        Assert.Equal(JellyfinProtocol.CompatibleProductName, publicInfo.ProductName);
        Assert.Equal(JellyfinProtocol.CompatibleServerVersion, publicInfo.Version);
        Assert.Equal("", publicInfo.OperatingSystem);
        Assert.Equal(HttpStatusCode.NotFound, splashscreen.StatusCode);
        Assert.Equal(JellyfinProtocol.UserPolicyProviders.DefaultAuthentication, user.Policy.AuthenticationProviderId);
        Assert.Equal(JellyfinProtocol.UserPolicyProviders.DefaultPasswordReset, user.Policy.PasswordResetProviderId);
        Assert.True(passwordFieldResponse.IsSuccessStatusCode);
        Assert.True(legacyResponse.IsSuccessStatusCode);

        var auth = await passwordFieldResponse.Content.ReadFromJsonAsync<JellyfinAuthenticationResult>();
        Assert.NotNull(auth);
        Assert.Equal(JellyfinProtocol.UserPolicyProviders.DefaultAuthentication, auth.User.Policy.AuthenticationProviderId);
        Assert.Equal(JellyfinProtocol.UserPolicyProviders.DefaultPasswordReset, auth.User.Policy.PasswordResetProviderId);
        var systemInfo = await JellyfinGetFromJsonAsync<JellyfinSystemInfo>(
            client,
            "/System/Info",
            auth.AccessToken);
        var endpointInfo = await JellyfinGetFromJsonAsync<JellyfinEndpointInfo>(
            client,
            "/System/Endpoint",
            auth.AccessToken);
        var sessions = await JellyfinGetFromJsonAsync<IReadOnlyList<JellyfinSessionInfoDto>>(
            client,
            "/Sessions",
            auth.AccessToken);

        Assert.NotNull(systemInfo);
        Assert.Equal(publicInfo.Id, systemInfo.Id);
        Assert.Equal("Prismedia", systemInfo.ServerName);
        Assert.Equal(JellyfinProtocol.CompatibleProductName, systemInfo.ProductName);
        Assert.Equal(JellyfinProtocol.CompatibleServerVersion, systemInfo.Version);
        Assert.NotNull(endpointInfo);
        Assert.NotNull(sessions);
        Assert.Contains(sessions, session => session.UserId == auth.User.Id);

        var groupingOptions = await JellyfinGetFromJsonAsync<IReadOnlyList<JellyfinSpecialViewOptionDto>>(
            client,
            $"/Users/{auth.User.Id:D}/GroupingOptions",
            auth.AccessToken);

        Assert.NotNull(groupingOptions);
        Assert.Equal(
            ["Movies", "Music", "Series", "Unwatched Movies", "Unwatched Series", "Videos"],
            groupingOptions.Select(item => item.Name ?? "").ToArray());

        var virtualFolders = await JellyfinGetFromJsonAsync<IReadOnlyList<JellyfinVirtualFolderInfoDto>>(
            client,
            "/Library/VirtualFolders",
            auth.AccessToken);

        Assert.NotNull(virtualFolders);
        Assert.Equal(
            ["Movies", "Unwatched Movies", "Videos", "Series", "Unwatched Series", "Music"],
            virtualFolders.Select(item => item.Name).ToArray());
        Assert.All(virtualFolders, folder => Assert.True(folder.LibraryOptions.Enabled));
    }

    [Fact]
    public async Task JellyfinPublicSystemInfoUsesForwardedOriginForCaptureProxy() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/System/Info/Public");
        request.Headers.Host = "127.0.0.1:8008";
        request.Headers.Add("X-Forwarded-Host", "10.68.95.131:8096");
        request.Headers.Add("X-Forwarded-Proto", "http");
        request.Headers.Add("X-Forwarded-For", "10.68.95.22");

        using var response = await client.SendAsync(request);
        var publicInfo = await response.Content.ReadFromJsonAsync<JellyfinPublicSystemInfo>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(publicInfo);
        Assert.Equal("http://10.68.95.131:8096", publicInfo.LocalAddress);
    }

    [Fact]
    public async Task JellyfinDisabledQuickConnectRoutesReturnJsonInsteadOfFallingThrough() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var initiate = await client.PostAsync("/QuickConnect/Initiate", null);
        using var connect = await client.GetAsync("/QuickConnect/Connect?Secret=missing");
        using var authenticate = await client.PostAsJsonAsync(
            "/Users/AuthenticateWithQuickConnect",
            new { Secret = "missing" });

        Assert.Equal(HttpStatusCode.Unauthorized, initiate.StatusCode);
        Assert.Equal("application/json", initiate.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, connect.StatusCode);
        Assert.Equal("application/json", connect.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Unauthorized, authenticate.StatusCode);
        Assert.Equal("application/json", authenticate.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UserPermissionsControlSfwAndNsfwCatalogVisibility() {
        using var factory = CreateFactory(new CatalogEntityReadService());
        using var client = factory.CreateClient();

        var sfwAuth = await AuthenticateAsync(client, TestAuth.Username, TestAuth.Password);
        var sfwVisible = await JellyfinGetFromJsonAsync<JellyfinBaseItemDto>(
            client,
            $"/Items/{SfwVideoId:D}",
            sfwAuth.AccessToken);
        using var sfwHidden = await JellyfinGetAsync(client, $"/Items/{NsfwVideoId:D}", sfwAuth.AccessToken);
        Assert.NotNull(sfwVisible);
        Assert.Equal(HttpStatusCode.NotFound, sfwHidden.StatusCode);

        using var appClient = factory.CreateAuthenticatedClient();
        using var adultUserResponse = await appClient.PostAsJsonAsync(
            "/api/users",
            new UserCreateRequest("Adult", TestAuth.Password, AllowNsfw: true), CodecJson);
        adultUserResponse.EnsureSuccessStatusCode();

        var adultAuth = await AuthenticateAsync(client, "Adult", TestAuth.Password);
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

        using var nsfwOnlyUserResponse = await appClient.PostAsJsonAsync(
            "/api/users",
            new UserCreateRequest("AdultOnly", TestAuth.Password, AllowSfw: false, AllowNsfw: true), CodecJson);
        nsfwOnlyUserResponse.EnsureSuccessStatusCode();

        var nsfwOnlyAuth = await AuthenticateAsync(client, "AdultOnly", TestAuth.Password);
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

        using var emptyUserResponse = await appClient.PostAsJsonAsync(
            "/api/users",
            new UserCreateRequest("Empty", TestAuth.Password, AllowSfw: false, AllowNsfw: false), CodecJson);
        emptyUserResponse.EnsureSuccessStatusCode();

        var emptyAuth = await AuthenticateAsync(client, "Empty", TestAuth.Password);
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
        var auth = await AuthenticateAsync(client, TestAuth.Username, TestAuth.Password);

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
            ["Movies", "Unwatched Movies", "Videos", "Series", "Unwatched Series", "Music"],
            views.Items.Select(item => item.Name).ToArray());
        Assert.NotNull(rootItems);
        Assert.Equal(6, rootItems.TotalRecordCount);
    }

    [Fact]
    public async Task JellyfinBrowseItemsExposePlayableCatalogShapeForInfuse() {
        using var factory = CreateFactory(new InfuseBrowseEntityReadService());
        using var client = factory.CreateClient();
        var auth = await AuthenticateAsync(client, TestAuth.Username, TestAuth.Password);

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
        var auth = await AuthenticateAsync(client, TestAuth.Username, TestAuth.Password);

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
        var auth = await AuthenticateAsync(client, TestAuth.Username, TestAuth.Password);

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

        Assert.Empty(item.Tags);
        Assert.Empty(item.Genres);
        Assert.Empty(item.GenreItems);
        Assert.DoesNotContain("Adventure", item.Tags);
        Assert.DoesNotContain("Adventure", item.Genres);
        Assert.DoesNotContain("Cozy", item.Tags);
        Assert.DoesNotContain("Cozy", item.Genres);

        var studio = Assert.Single(item.Studios);
        Assert.Equal("Studio One", studio.Name);
        Assert.Equal(JellyfinMetadataEntityReadService.StudioId, studio.Id);

        Assert.Equal("tt1234567", item.ProviderIds!["imdb"]);
        Assert.Contains(item.ExternalUrls, url => url.Name == "IMDb" && url.Url == "https://www.imdb.com/title/tt1234567/");

        Assert.Equal(["Opening"], item.Chapters.Select(chapter => chapter.Name).ToArray());
        Assert.Equal(TimeSpan.FromSeconds(12).Ticks, item.Chapters[0].StartPositionTicks);

        var people = item.People.OrderBy(person => person.Name, StringComparer.Ordinal).ToArray();
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
        string? cacheRoot = null,
        string? staticWebRoot = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                if (cacheRoot is not null) {
                    builder.UseSetting("Prismedia:CacheDir", cacheRoot);
                }
                if (staticWebRoot is not null) {
                    builder.UseSetting("Prismedia:StaticWebRoot", staticWebRoot);
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
            bool? orphaned = null,
            bool? wanted = null,
            AcquisitionStatus? acquisitionStatus = null) {
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

        public Task<IReadOnlyDictionary<Guid, CollectionListContext>> GetListContextsAsync(
            IReadOnlyList<Guid> collectionIds,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CollectionListContext>>(new Dictionary<Guid, CollectionListContext>());

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
            bool? orphaned = null,
            bool? wanted = null,
            AcquisitionStatus? acquisitionStatus = null) =>
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
            bool? orphaned = null,
            bool? wanted = null,
            AcquisitionStatus? acquisitionStatus = null) {
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

        // The series has one season and the season one episode — the batched folder context the
        // catalog reads for list rows instead of hydrating each folder row's full detail.
        public Task<IReadOnlyDictionary<Guid, EntityFolderListContext>> GetFolderListContextsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, EntityFolderListContext>>(
                ids.Where(id => id == SeriesId || id == SeasonId).ToDictionary(
                    id => id,
                    id => new EntityFolderListContext(1, null, [], null, null, [])));

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
            bool? orphaned = null,
            bool? wanted = null,
            AcquisitionStatus? acquisitionStatus = null) =>
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
                    new EntityCreditMetadata(ActorId, "actor", "Hero", ["actor"], ["Hero"]),
                    new EntityCreditMetadata(DirectorId, "director", null, ["director"], [])
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
                    null,
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
