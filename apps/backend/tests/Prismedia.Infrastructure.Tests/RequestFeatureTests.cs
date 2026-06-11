using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Requests;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Infrastructure.Tests;

public sealed class RequestFeatureTests {
    [Fact]
    public void RequestCodeEnumsSerializeAsStableCodes() {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new CodecJsonConverterFactory());
        var json = JsonSerializer.Serialize(new RequestSearchRequest(
            "blade",
            [RequestMediaKind.Movie],
            [RequestProviderKind.Radarr]),
            options);

        Assert.Contains("\"movie\"", json);
        Assert.Contains("\"radarr\"", json);
        Assert.DoesNotContain(nameof(RequestMediaKind.Movie), json);
        Assert.DoesNotContain(nameof(RequestProviderKind.Radarr), json);
    }

    [Fact]
    public async Task ServiceStoreMakesFirstInstanceDefaultAndRedactsApiKey() {
        await using var db = CreateContext();
        var store = new EfRequestServiceInstanceStore(db);

        var first = await store.SaveAsync(new RequestServiceInstanceSaveRequest(
            null,
            RequestProviderKind.Radarr,
            "Movies",
            "http://radarr.test",
            "secret-key",
            null,
            null,
            null,
            true,
            false),
            CancellationToken.None);

        var second = await store.SaveAsync(new RequestServiceInstanceSaveRequest(
            null,
            RequestProviderKind.Radarr,
            "4K Movies",
            "http://radarr-4k.test",
            "another-key",
            null,
            null,
            null,
            true,
            true),
            CancellationToken.None);

        var services = await store.ListAsync(CancellationToken.None);

        Assert.True(first.IsDefault);
        Assert.True(second.IsDefault);
        Assert.False(services.Single(service => service.Id == first.Id).IsDefault);
        Assert.True(services.Single(service => service.Id == second.Id).IsDefault);
        Assert.All(services, service => {
            Assert.True(service.HasApiKey);
            Assert.Null(service.ApiKey);
        });
    }

    [Fact]
    public async Task RadarrClientMapsSearchAndSendsApiKey() {
        var handler = new FakeHttpHandler((request, body) => {
            Assert.Equal("radarr-key", request.Headers.GetValues(RequestProviderHttp.ApiKeyHeader).Single());
            Assert.Equal("/api/v3/movie/lookup", request.RequestUri!.AbsolutePath);
            return Json("""
                [
                  {
                    "tmdbId": 424,
                    "imdbId": "tt0083658",
                    "title": "Blade Runner",
                    "year": 1982,
                    "overview": "A blade runner must pursue replicants.",
                    "runtime": 117,
                    "certification": "R",
                    "genres": ["Science Fiction", "Drama"],
                    "ratings": { "value": 8.1, "votes": 1234 },
                    "images": [
                      { "coverType": "poster", "remoteUrl": "https://images.test/poster.jpg" }
                    ]
                  }
                ]
                """);
        });
        var client = new RadarrRequestProviderClient(new HttpClient(handler));

        var results = await client.SearchAsync(Instance(RequestProviderKind.Radarr), "blade runner", CancellationToken.None);
        var result = Assert.Single(results);

        Assert.Equal(RequestMediaKind.Movie, result.Kind);
        Assert.Equal("424", result.ExternalId);
        Assert.Equal("Blade Runner", result.Title);
        Assert.Equal("https://images.test/poster.jpg", result.PosterUrl);
        Assert.Contains(result.Tags, tag => tag == "Science Fiction");
        Assert.Equal(8.1m, result.Rating);
    }

    [Fact]
    public async Task SonarrSubmitMarksSelectedSeasonsIncludingSpecials() {
        var handler = new FakeHttpHandler((request, body) => {
            Assert.Equal(HttpMethod.Post, request.Method);
            using var document = JsonDocument.Parse(body);
            var seasons = document.RootElement.GetProperty("seasons").EnumerateArray().ToArray();

            Assert.Contains(seasons, season => season.TryGetProperty("seasonNumber", out var number) && number.GetInt32() == 0);
            Assert.True(seasons.Single(season => season.GetProperty("seasonNumber").GetInt32() == 0).GetProperty("monitored").GetBoolean());
            Assert.True(seasons.Single(season => season.GetProperty("seasonNumber").GetInt32() == 1).GetProperty("monitored").GetBoolean());
            Assert.False(seasons.Single(season => season.GetProperty("seasonNumber").GetInt32() == 2).GetProperty("monitored").GetBoolean());

            return Json("""{ "id": 12 }""");
        });
        var client = new SonarrRequestProviderClient(new HttpClient(handler));
        var detail = new RequestDetailResponse(
            RequestProviderKind.Sonarr,
            RequestMediaKind.Series,
            "79169",
            "Twin Peaks",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            [],
            [
                new RequestChildOption("0", "Specials", RequestMediaKind.Series, true, null, null, null),
                new RequestChildOption("1", "Season 1", RequestMediaKind.Series, true, null, null, null),
                new RequestChildOption("2", "Season 2", RequestMediaKind.Series, true, null, null, null)
            ],
            []);

        var response = await client.SubmitAsync(
            Instance(RequestProviderKind.Sonarr),
            detail,
            new RequestSubmitRequest(Guid.NewGuid(), RequestProviderKind.Sonarr, RequestMediaKind.Series, "79169", "Twin Peaks", 7, "/tv", null, true, true, ["0", "1"]),
            CancellationToken.None);

        Assert.True(response.Submitted);
    }

    [Fact]
    public async Task LidarrClientCanSearchArtistsAndMonitorSelectedAlbums() {
        var calls = new List<string>();
        var handler = new FakeHttpHandler((request, body) => {
            calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (request.Method == HttpMethod.Get) {
                return Json("""[{ "foreignArtistId": "mb-artist", "artistName": "Bowie", "overview": "Artist", "images": [] }]""");
            }

            if (request.RequestUri!.AbsolutePath.EndsWith("/artist", StringComparison.Ordinal)) {
                return Json("""{ "id": 42 }""");
            }

            using var document = JsonDocument.Parse(body);
            Assert.Equal([9], document.RootElement.GetProperty("albumIds").EnumerateArray().Select(item => item.GetInt32()).ToArray());
            Assert.True(document.RootElement.GetProperty("monitored").GetBoolean());
            return Json("""{}""");
        });
        var client = new LidarrRequestProviderClient(new HttpClient(handler));

        var results = await client.SearchAsync(Instance(RequestProviderKind.Lidarr), "bowie", CancellationToken.None);
        Assert.Equal("mb-artist", Assert.Single(results).ExternalId);

        var detail = new RequestDetailResponse(RequestProviderKind.Lidarr, RequestMediaKind.Artist, "mb-artist", "Bowie", null, null, null, null, null, null, null, [], [], [], [
            new RequestChildOption("9", "Low", RequestMediaKind.Album, true, null, null, null)
        ], []);
        await client.SubmitAsync(
            Instance(RequestProviderKind.Lidarr),
            detail,
            new RequestSubmitRequest(Guid.NewGuid(), RequestProviderKind.Lidarr, RequestMediaKind.Artist, "mb-artist", "Bowie", 1, "/music", null, true, true, ["9"]),
            CancellationToken.None);

        Assert.Contains("PUT /api/v1/album/monitor", calls);
    }

    private static RequestServiceInstanceDetail Instance(RequestProviderKind kind) =>
        new(Guid.NewGuid(), kind, "Test", "http://arr.test", true, null, null, null, true, true, kind.ToCode() + "-key");

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class FakeHttpHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request, body);
        }
    }
}
