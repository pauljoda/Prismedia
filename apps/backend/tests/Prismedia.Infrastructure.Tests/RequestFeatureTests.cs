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
            [RequestProviderKind.Radarr],
            false),
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
            RequestMinimumAvailability.Released,
            [],
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
            RequestMinimumAvailability.Released,
            [],
            true,
            true),
            CancellationToken.None);

        var services = await store.ListAsync(CancellationToken.None);

        Assert.True(first.IsDefault);
        Assert.True(second.IsDefault);
        Assert.False(services.Single(service => service.Id == first.Id).IsDefault);
        Assert.True(services.Single(service => service.Id == second.Id).IsDefault);
        Assert.All(services, service => Assert.True(service.HasApiKey));
    }

    [Fact]
    public async Task ServiceStoreReportsExistingApiKeyWhenEditingWithoutReplacement() {
        await using var db = CreateContext();
        var store = new EfRequestServiceInstanceStore(db);
        var created = await store.SaveAsync(new RequestServiceInstanceSaveRequest(
            null,
            RequestProviderKind.Radarr,
            "Movies",
            "http://radarr.test",
            "secret-key",
            null,
            null,
            null,
            RequestMinimumAvailability.Released,
            [],
            true,
            false),
            CancellationToken.None);

        var edited = await store.SaveAsync(new RequestServiceInstanceSaveRequest(
            created.Id,
            RequestProviderKind.Radarr,
            "Movies",
            "http://radarr.test",
            null,
            "/movies",
            4,
            null,
            RequestMinimumAvailability.Released,
            [],
            true,
            true),
            CancellationToken.None);

        Assert.True(edited.HasApiKey);
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
    public async Task LookupRecordsWithPositiveIdsAreReportedAsTracked() {
        var handler = new FakeHttpHandler((request, body) => Json("""
            [
              {
                "id": 31,
                "tmdbId": 424,
                "title": "Blade Runner",
                "monitored": true,
                "images": [
                  { "coverType": "poster", "url": "/MediaCover/31/poster.jpg", "remoteUrl": "https://images.test/poster.jpg" }
                ]
              },
              {
                "tmdbId": 9999,
                "title": "Untracked Movie",
                "images": [{ "coverType": "poster", "url": "/MediaCover/raw.jpg" }]
              }
            ]
            """));
        var client = new RadarrRequestProviderClient(new HttpClient(handler));

        var results = await client.SearchAsync(Instance(RequestProviderKind.Radarr), "blade", CancellationToken.None);

        var tracked = results.Single(result => result.ExternalId == "424");
        Assert.True(tracked.Tracked);
        Assert.Equal("31", tracked.UpstreamId);
        Assert.True(tracked.Monitored);
        var untracked = results.Single(result => result.ExternalId == "9999");
        Assert.False(untracked.Tracked);
        Assert.Null(untracked.UpstreamId);
        // Relative /MediaCover paths only resolve inside the *arr instance; they must not surface as artwork.
        Assert.Null(untracked.PosterUrl);
        Assert.Equal("https://images.test/poster.jpg", tracked.PosterUrl);
    }

    [Fact]
    public async Task RadarrSubmitUpdatesMonitoringAndSearchesWhenMovieAlreadyTracked() {
        var calls = new List<string>();
        var handler = new FakeHttpHandler((request, body) => {
            calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (request.Method == HttpMethod.Get) {
                return Json("""[{ "id": 31, "tmdbId": 424, "title": "Blade Runner", "monitored": false, "qualityProfileId": 2, "images": [] }]""");
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/command", StringComparison.Ordinal)) {
                using var commandDocument = JsonDocument.Parse(body);
                Assert.Equal("MoviesSearch", commandDocument.RootElement.GetProperty("name").GetString());
                Assert.Equal([31], commandDocument.RootElement.GetProperty("movieIds").EnumerateArray().Select(id => id.GetInt32()).ToArray());
                return Json("""{}""");
            }

            Assert.Equal(HttpMethod.Put, request.Method);
            using var document = JsonDocument.Parse(body);
            Assert.True(document.RootElement.GetProperty("monitored").GetBoolean());
            // Update keeps the upstream quality profile untouched.
            Assert.Equal(2, document.RootElement.GetProperty("qualityProfileId").GetInt32());
            return Json("""{ "id": 31 }""");
        });
        var client = new RadarrRequestProviderClient(new HttpClient(handler));
        var detail = await client.GetDetailAsync(Instance(RequestProviderKind.Radarr), RequestMediaKind.Movie, "424", CancellationToken.None);

        var response = await client.SubmitAsync(
            Instance(RequestProviderKind.Radarr),
            detail,
            new RequestSubmitRequest(Guid.NewGuid(), RequestProviderKind.Radarr, RequestMediaKind.Movie, "424", "Blade Runner", 7, "/movies", null, true, true, []),
            CancellationToken.None);

        Assert.True(response.Submitted);
        Assert.True(detail.Tracked);
        Assert.Equal("31", response.UpstreamId);
        Assert.Contains("PUT /api/v3/movie/31", calls);
        Assert.Contains("POST /api/v3/command", calls);
        Assert.DoesNotContain("POST /api/v3/movie", calls);
    }

    [Fact]
    public async Task SonarrSubmitUpdatesSeasonMonitoringWhenSeriesAlreadyTracked() {
        var calls = new List<string>();
        var handler = new FakeHttpHandler((request, body) => {
            calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (request.Method == HttpMethod.Get) {
                return Json("""
                    [
                      {
                        "id": 12,
                        "tvdbId": 79169,
                        "title": "Twin Peaks",
                        "monitored": true,
                        "images": [],
                        "seasons": [
                          { "seasonNumber": 1, "monitored": true, "statistics": { "episodeCount": 8 } },
                          { "seasonNumber": 2, "monitored": false, "statistics": { "episodeCount": 22 } }
                        ]
                      }
                    ]
                    """);
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/command", StringComparison.Ordinal)) {
                using var commandDocument = JsonDocument.Parse(body);
                Assert.Equal("MissingEpisodeSearch", commandDocument.RootElement.GetProperty("name").GetString());
                Assert.Equal(12, commandDocument.RootElement.GetProperty("seriesId").GetInt32());
                return Json("""{}""");
            }

            Assert.Equal(HttpMethod.Put, request.Method);
            using var document = JsonDocument.Parse(body);
            var seasons = document.RootElement.GetProperty("seasons").EnumerateArray().ToArray();
            Assert.True(seasons.Single(season => season.GetProperty("seasonNumber").GetInt32() == 1).GetProperty("monitored").GetBoolean());
            Assert.True(seasons.Single(season => season.GetProperty("seasonNumber").GetInt32() == 2).GetProperty("monitored").GetBoolean());
            return Json("""{ "id": 12 }""");
        });
        var client = new SonarrRequestProviderClient(new HttpClient(handler));
        var detail = await client.GetDetailAsync(Instance(RequestProviderKind.Sonarr), RequestMediaKind.Series, "79169", CancellationToken.None);

        Assert.True(detail.Tracked);
        Assert.True(detail.Children.Single(child => child.Id == "1").Monitored);
        Assert.False(detail.Children.Single(child => child.Id == "2").Monitored);

        var response = await client.SubmitAsync(
            Instance(RequestProviderKind.Sonarr),
            detail,
            new RequestSubmitRequest(Guid.NewGuid(), RequestProviderKind.Sonarr, RequestMediaKind.Series, "79169", "Twin Peaks", 7, "/tv", null, true, true, ["1", "2"]),
            CancellationToken.None);

        Assert.True(response.Submitted);
        Assert.Contains("PUT /api/v3/series/12", calls);
        Assert.Contains("POST /api/v3/command", calls);
        Assert.DoesNotContain("POST /api/v3/series", calls);
    }

    [Fact]
    public void AdultCertificationsGateOnlyAdultsOnlyRatings() {
        Assert.True(AdultCertifications.IsAdult("NC-17"));
        Assert.True(AdultCertifications.IsAdult("x"));
        Assert.True(AdultCertifications.IsAdult("XXX"));
        Assert.True(AdultCertifications.IsAdult("R18+"));
        Assert.False(AdultCertifications.IsAdult("R"));
        Assert.False(AdultCertifications.IsAdult("TV-MA"));
        Assert.False(AdultCertifications.IsAdult("18"));
        Assert.False(AdultCertifications.IsAdult(null));
    }

    [Fact]
    public async Task RequestHistoryStoreRecordsAndRefreshesStatuses() {
        await using var db = CreateContext();
        var store = new EfRequestHistoryStore(db);

        await store.AddAsync(new RequestHistoryAddRequest(
            Guid.NewGuid(), "Movies", RequestProviderKind.Radarr, RequestMediaKind.Movie, "424", "Blade Runner",
            null, 1982, "https://images.test/poster.jpg", "31", true, ["1", "2"]), CancellationToken.None);

        var entries = await store.ListAsync(50, CancellationToken.None);
        var entry = Assert.Single(entries);
        Assert.Equal(RequestHistoryStatus.Submitted, entry.Status);
        Assert.Equal(2, entry.SelectedChildCount);

        await store.UpdateStatusesAsync([new RequestHistoryStatusUpdate(entry.Id, RequestHistoryStatus.Downloading, null, "31")], CancellationToken.None);
        var refreshed = Assert.Single(await store.ListAsync(50, CancellationToken.None));
        Assert.Equal(RequestHistoryStatus.Downloading, refreshed.Status);

        Assert.True(await store.DeleteAsync(entry.Id, CancellationToken.None));
        Assert.Empty(await store.ListAsync(50, CancellationToken.None));
    }

    [Fact]
    public async Task RadarrStatusesResolveQueueFilesAndRemovals() {
        var handler = new FakeHttpHandler((request, body) => request.RequestUri!.AbsolutePath switch {
            "/api/v3/movie" => Json("""
                [
                  { "id": 1, "tmdbId": 100, "title": "Done", "hasFile": true, "monitored": true },
                  { "id": 2, "tmdbId": 200, "title": "Queued", "hasFile": false, "monitored": true },
                  { "id": 3, "tmdbId": 300, "title": "Waiting", "hasFile": false, "monitored": true }
                ]
                """),
            "/api/v3/queue" => Json("""{ "records": [{ "movieId": 2 }] }"""),
            _ => throw new InvalidOperationException(request.RequestUri.AbsolutePath)
        });
        var client = new RadarrRequestProviderClient(new HttpClient(handler));
        var ids = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var statuses = await client.GetStatusesAsync(Instance(RequestProviderKind.Radarr), [
            new RequestStatusProbe(ids.Item1, RequestMediaKind.Movie, "100", "1"),
            new RequestStatusProbe(ids.Item2, RequestMediaKind.Movie, "200", "2"),
            new RequestStatusProbe(ids.Item3, RequestMediaKind.Movie, "300", "3"),
            new RequestStatusProbe(ids.Item4, RequestMediaKind.Movie, "999", "4")
        ], CancellationToken.None);

        Assert.Equal(RequestHistoryStatus.Available, statuses.Single(status => status.HistoryId == ids.Item1).Status);
        Assert.Equal(RequestHistoryStatus.Downloading, statuses.Single(status => status.HistoryId == ids.Item2).Status);
        Assert.Equal(RequestHistoryStatus.Pending, statuses.Single(status => status.HistoryId == ids.Item3).Status);
        Assert.Equal(RequestHistoryStatus.Removed, statuses.Single(status => status.HistoryId == ids.Item4).Status);
    }

    [Fact]
    public async Task SonarrSubmitMarksSelectedSeasonsIncludingSpecials() {
        var calls = new List<string>();
        var handler = new FakeHttpHandler((request, body) => {
            calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (request.Method == HttpMethod.Get) {
                return Json("""
                    [
                      {
                        "tvdbId": 79169,
                        "title": "Twin Peaks",
                        "originalTitle": "Twin Peaks Original",
                        "titleSlug": "twin-peaks",
                        "cleanTitle": "twinpeaks",
                        "status": "ended",
                        "images": [],
                        "seasons": [
                          { "seasonNumber": 0, "statistics": { "episodeCount": 1 } },
                          { "seasonNumber": 1, "statistics": { "episodeCount": 8 } },
                          { "seasonNumber": 2, "statistics": { "episodeCount": 22 } }
                        ]
                      }
                    ]
                    """);
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            using var document = JsonDocument.Parse(body);
            Assert.Equal("Twin Peaks Original", document.RootElement.GetProperty("originalTitle").GetString());
            Assert.Equal("twin-peaks", document.RootElement.GetProperty("titleSlug").GetString());
            var seasons = document.RootElement.GetProperty("seasons").EnumerateArray().ToArray();

            Assert.True(document.RootElement.GetProperty("seasonFolder").GetBoolean());
            Assert.Contains(seasons, season => season.TryGetProperty("seasonNumber", out var number) && number.GetInt32() == 0);
            Assert.True(seasons.Single(season => season.GetProperty("seasonNumber").GetInt32() == 0).GetProperty("monitored").GetBoolean());
            Assert.True(seasons.Single(season => season.GetProperty("seasonNumber").GetInt32() == 1).GetProperty("monitored").GetBoolean());
            Assert.False(seasons.Single(season => season.GetProperty("seasonNumber").GetInt32() == 2).GetProperty("monitored").GetBoolean());

            return Json("""{ "id": 12 }""");
        });
        var client = new SonarrRequestProviderClient(new HttpClient(handler));
        var detail = await client.GetDetailAsync(Instance(RequestProviderKind.Sonarr), RequestMediaKind.Series, "79169", CancellationToken.None);

        var response = await client.SubmitAsync(
            Instance(RequestProviderKind.Sonarr),
            detail,
            new RequestSubmitRequest(Guid.NewGuid(), RequestProviderKind.Sonarr, RequestMediaKind.Series, "79169", "Twin Peaks", 7, "/tv", null, true, true, ["0", "1"]),
            CancellationToken.None);

        Assert.True(response.Submitted);
        Assert.Contains("GET /api/v3/series/lookup", calls);
    }

    [Fact]
    public async Task RadarrSubmitPreservesLookupResourceAndMutatesRequestFields() {
        var handler = new FakeHttpHandler((request, body) => {
            if (request.Method == HttpMethod.Get) {
                return Json("""
                    [
                      {
                        "tmdbId": 424,
                        "title": "Blade Runner",
                        "titleSlug": "blade-runner",
                        "images": [{ "coverType": "poster", "remoteUrl": "https://images.test/poster.jpg" }],
                        "genres": ["Science Fiction"]
                      }
                    ]
                    """);
            }

            using var document = JsonDocument.Parse(body);
            Assert.Equal("blade-runner", document.RootElement.GetProperty("titleSlug").GetString());
            Assert.Equal("Science Fiction", document.RootElement.GetProperty("genres")[0].GetString());
            Assert.Equal(7, document.RootElement.GetProperty("qualityProfileId").GetInt32());
            Assert.Equal("/movies", document.RootElement.GetProperty("rootFolderPath").GetString());
            Assert.True(document.RootElement.GetProperty("monitored").GetBoolean());
            Assert.Equal(RequestMinimumAvailability.Released.ToCode(), document.RootElement.GetProperty("minimumAvailability").GetString());
            Assert.Equal([3, 9], document.RootElement.GetProperty("tags").EnumerateArray().Select(tag => tag.GetInt32()).ToArray());
            Assert.True(document.RootElement.GetProperty("addOptions").GetProperty("searchForMovie").GetBoolean());
            return Json("""{ "id": 12 }""");
        });
        var client = new RadarrRequestProviderClient(new HttpClient(handler));
        var detail = await client.GetDetailAsync(Instance(RequestProviderKind.Radarr), RequestMediaKind.Movie, "424", CancellationToken.None);

        var response = await client.SubmitAsync(
            Instance(RequestProviderKind.Radarr, [9, 3]),
            detail,
            new RequestSubmitRequest(Guid.NewGuid(), RequestProviderKind.Radarr, RequestMediaKind.Movie, "424", "Blade Runner", 7, "/movies", null, true, true, []),
            CancellationToken.None);

        Assert.True(response.Submitted);
    }

    [Fact]
    public async Task ProviderOptionsAreGroupedByUsageAndLidarrIncludesMetadataProfiles() {
        var handler = new FakeHttpHandler((request, body) => request.RequestUri!.AbsolutePath switch {
            "/api/v1/qualityprofile" => Json("""[{ "id": 1, "name": "Lossless" }]"""),
            "/api/v1/metadataprofile" => Json("""[{ "id": 2, "name": "Standard Metadata" }]"""),
            "/api/v1/rootfolder" => Json("""[{ "path": "/music", "freeSpace": 123 }]"""),
            "/api/v1/tag" => Json("""[{ "id": 7, "label": "prismedia" }]"""),
            _ => throw new InvalidOperationException(request.RequestUri.AbsolutePath)
        });
        var client = new LidarrRequestProviderClient(new HttpClient(handler));

        var options = await client.GetOptionsAsync(Instance(RequestProviderKind.Lidarr), CancellationToken.None);

        Assert.Equal("Lossless", Assert.Single(options.QualityProfiles).Name);
        Assert.Equal("/music", Assert.Single(options.RootFolders).Path);
        Assert.Equal("Standard Metadata", Assert.Single(options.MetadataProfiles).Name);
        Assert.Equal("prismedia", Assert.Single(options.Tags).Name);
    }

    [Fact]
    public async Task LidarrClientCanSearchArtistsWithoutPostingLookupAlbumIdsToMonitor() {
        var calls = new List<string>();
        var handler = new FakeHttpHandler((request, body) => {
            calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (request.Method == HttpMethod.Get) {
                if (request.RequestUri!.AbsolutePath.EndsWith("/album/lookup", StringComparison.Ordinal)) {
                    return Json("[]");
                }

                return Json("""[{ "foreignArtistId": "mb-artist", "artistName": "Bowie", "overview": "Artist", "images": [] }]""");
            }

            if (request.RequestUri!.AbsolutePath.EndsWith("/artist", StringComparison.Ordinal)) {
                using var artistDocument = JsonDocument.Parse(body);
                Assert.Equal("all", artistDocument.RootElement.GetProperty("addOptions").GetProperty("monitor").GetString());
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

        var detail = new RequestDetailResponse(RequestProviderKind.Lidarr, RequestMediaKind.Artist, "mb-artist", "Bowie", null, null, null, null, null, null, null, null, null, [], [], [], [
            new RequestChildOption("9", "Low", RequestMediaKind.Album, true, null, null, null, null, null, null)
        ], [], false, null, null, new RequestServiceOptionsResponse([], [], [], []));
        await client.SubmitAsync(
            Instance(RequestProviderKind.Lidarr),
            detail,
            new RequestSubmitRequest(Guid.NewGuid(), RequestProviderKind.Lidarr, RequestMediaKind.Artist, "mb-artist", "Bowie", 1, "/music", null, true, true, ["9"]),
            CancellationToken.None);

        Assert.DoesNotContain("PUT /api/v1/album/monitor", calls);
        Assert.Contains("POST /api/v1/artist", calls);
    }

    [Fact]
    public async Task LidarrClientMapsAlbumsAndSubmitsStandaloneAlbumWithUnmonitoredArtist() {
        var calls = new List<string>();
        var handler = new FakeHttpHandler((request, body) => {
            calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (request.RequestUri.Host == "musicbrainz.org") {
                return Json("""{ "releases": [] }""");
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/artist/lookup", StringComparison.Ordinal)) {
                return Json("[]");
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/api/v1/album", StringComparison.Ordinal) && request.Method == HttpMethod.Get) {
                return Json("[]");
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/api/v1/album", StringComparison.Ordinal) && request.Method == HttpMethod.Post) {
                using var document = JsonDocument.Parse(body);
                Assert.True(document.RootElement.GetProperty("monitored").GetBoolean());
                Assert.True(document.RootElement.GetProperty("addOptions").GetProperty("searchForNewAlbum").GetBoolean());
                var artist = document.RootElement.GetProperty("artist");
                Assert.Equal(1, artist.GetProperty("qualityProfileId").GetInt32());
                Assert.Equal(2, artist.GetProperty("metadataProfileId").GetInt32());
                Assert.Equal("/music", artist.GetProperty("rootFolderPath").GetString());
                Assert.False(artist.GetProperty("monitored").GetBoolean());
                Assert.Equal("none", artist.GetProperty("monitorNewItems").GetString());
                Assert.Equal("none", artist.GetProperty("addOptions").GetProperty("monitor").GetString());
                return Json("""{ "id": 77 }""");
            }

            return Json("""
                [
                  {
                    "id": 9,
                    "foreignAlbumId": "mb-album",
                    "title": "Low",
                    "releaseDate": "1977-01-14",
                    "overview": "Album",
                    "artist": { "foreignArtistId": "mb-artist", "artistName": "David Bowie" },
                    "images": [{ "coverType": "cover", "remoteUrl": "https://images.test/low.jpg" }]
                  }
                ]
                """);
        });
        var client = new LidarrRequestProviderClient(new HttpClient(handler));

        var results = await client.SearchAsync(Instance(RequestProviderKind.Lidarr), "low", CancellationToken.None);
        var album = Assert.Single(results);
        var detail = await client.GetDetailAsync(Instance(RequestProviderKind.Lidarr), RequestMediaKind.Album, "mb-album", CancellationToken.None);

        Assert.Equal(RequestMediaKind.Album, album.Kind);
        Assert.Equal("mb-album", album.ExternalId);
        Assert.Equal(1977, album.Year);
        Assert.Equal("https://images.test/low.jpg", detail.PosterUrl);

        var response = await client.SubmitAsync(
            Instance(RequestProviderKind.Lidarr),
            detail,
            new RequestSubmitRequest(Guid.NewGuid(), RequestProviderKind.Lidarr, RequestMediaKind.Album, "mb-album", "Low", 1, "/music", 2, true, true, []),
            CancellationToken.None);

        Assert.True(response.Submitted);
        Assert.Equal("77", response.UpstreamId);
        Assert.Contains("POST /api/v1/album", calls);
    }

    [Fact]
    public async Task LidarrAlbumSubmitMonitorsAlbumAlreadyInLibrary() {
        var calls = new List<string>();
        var handler = new FakeHttpHandler((request, body) => {
            calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (request.RequestUri.AbsolutePath.EndsWith("/api/v1/album", StringComparison.Ordinal) && request.Method == HttpMethod.Get) {
                return Json("""[{ "id": 42, "foreignAlbumId": "mb-album", "monitored": false }]""");
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/album/monitor", StringComparison.Ordinal)) {
                using var monitorDocument = JsonDocument.Parse(body);
                Assert.Equal([42], monitorDocument.RootElement.GetProperty("albumIds").EnumerateArray().Select(id => id.GetInt32()).ToArray());
                Assert.True(monitorDocument.RootElement.GetProperty("monitored").GetBoolean());
                return Json("""{}""");
            }

            Assert.EndsWith("/command", request.RequestUri.AbsolutePath);
            using var commandDocument = JsonDocument.Parse(body);
            Assert.Equal("AlbumSearch", commandDocument.RootElement.GetProperty("name").GetString());
            Assert.Equal([42], commandDocument.RootElement.GetProperty("albumIds").EnumerateArray().Select(id => id.GetInt32()).ToArray());
            return Json("""{}""");
        });
        var client = new LidarrRequestProviderClient(new HttpClient(handler));
        var detail = new RequestDetailResponse(RequestProviderKind.Lidarr, RequestMediaKind.Album, "mb-album", "Low", null, null, null, null, null, null, null, null, null, [], [], [], [], [], false, null, null, new RequestServiceOptionsResponse([], [], [], []));

        var response = await client.SubmitAsync(
            Instance(RequestProviderKind.Lidarr),
            detail,
            new RequestSubmitRequest(Guid.NewGuid(), RequestProviderKind.Lidarr, RequestMediaKind.Album, "mb-album", "Low", 1, "/music", 2, true, true, []),
            CancellationToken.None);

        Assert.True(response.Submitted);
        Assert.Equal("42", response.UpstreamId);
        Assert.Contains("PUT /api/v1/album/monitor", calls);
        Assert.Contains("POST /api/v1/command", calls);
    }

    [Fact]
    public async Task LidarrArtistDetailListsMusicBrainzDiscographyNewestFirst() {
        string? lookupTerm = null;
        var handler = new FakeHttpHandler((request, body) => {
            if (request.RequestUri!.Host == "musicbrainz.org") {
                Assert.Contains("Prismedia", request.Headers.UserAgent.ToString());
                return Json("""
                    {
                      "release-groups": [
                        { "id": "rg-1", "title": "Low", "primary-type": "Album", "secondary-types": [], "first-release-date": "1977-01-14" },
                        { "id": "rg-2", "title": "Blackstar", "primary-type": "Album", "secondary-types": [], "first-release-date": "2016-01-08" },
                        { "id": "rg-3", "title": "Stage", "primary-type": "Album", "secondary-types": ["Live"], "first-release-date": "1978-09-08" }
                      ]
                    }
                    """);
            }

            lookupTerm = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["term"];
            return Json("""[{ "foreignArtistId": "mb-artist", "artistName": "Bowie", "overview": "Artist", "images": [] }]""");
        });
        var client = new LidarrRequestProviderClient(new HttpClient(handler));

        var detail = await client.GetDetailAsync(Instance(RequestProviderKind.Lidarr), RequestMediaKind.Artist, "mb-artist", CancellationToken.None);

        Assert.Equal("lidarr:mb-artist", lookupTerm);
        Assert.Equal(3, detail.Children.Count);
        Assert.Equal(["Blackstar", "Stage", "Low"], detail.Children.Select(child => child.Title).ToArray());
        Assert.Equal(2016, detail.Children[0].Year);
        Assert.Equal("Album · Live", detail.Children[1].Overview);
        Assert.StartsWith("https://coverartarchive.org/release-group/rg-2", detail.Children[0].PosterUrl);
    }

    [Fact]
    public async Task LidarrArtistDetailFallsBackToLidarrAlbumSearchWhenMusicBrainzFails() {
        var handler = new FakeHttpHandler((request, body) => {
            if (request.RequestUri!.Host == "musicbrainz.org") {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/artist/lookup", StringComparison.Ordinal)) {
                return Json("""[{ "foreignArtistId": "mb-artist", "artistName": "Bowie", "overview": "Artist", "images": [] }]""");
            }

            return Json("""
                [
                  { "foreignAlbumId": "mb-album-1", "title": "Low", "releaseDate": "1977-01-14", "albumType": "Album", "artist": { "foreignArtistId": "mb-artist", "artistName": "Bowie" } },
                  { "foreignAlbumId": "mb-album-2", "title": "Other", "artist": { "foreignArtistId": "mb-other", "artistName": "Other Guy" } }
                ]
                """);
        });
        var client = new LidarrRequestProviderClient(new HttpClient(handler));

        var detail = await client.GetDetailAsync(Instance(RequestProviderKind.Lidarr), RequestMediaKind.Artist, "mb-artist", CancellationToken.None);

        var child = Assert.Single(detail.Children);
        Assert.Equal("mb-album-1", child.Id);
        Assert.Equal("Low", child.Title);
        Assert.Equal(1977, child.Year);
    }

    [Fact]
    public async Task LidarrAlbumDetailLooksUpByMbidPrefixAndSurfacesArtistAndTracks() {
        string? lastTerm = null;
        var handler = new FakeHttpHandler((request, body) => {
            if (request.RequestUri!.Host == "musicbrainz.org") {
                return Json("""
                    {
                      "releases": [
                        {
                          "id": "rel-deluxe",
                          "media": [{ "track-count": 25, "tracks": [] }]
                        },
                        {
                          "id": "rel-standard",
                          "media": [{
                            "track-count": 2,
                            "tracks": [
                              { "position": 1, "title": "Speed of Life", "length": 166000 },
                              { "position": 2, "title": "Breaking Glass", "length": 112000 }
                            ]
                          }]
                        }
                      ]
                    }
                    """);
            }

            lastTerm = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["term"];
            return Json("""
                [
                  {
                    "foreignAlbumId": "mb-album",
                    "title": "Low",
                    "albumType": "Album",
                    "secondaryTypes": [],
                    "duration": 3029555,
                    "releaseDate": "1977-01-14",
                    "genres": ["Art Rock"],
                    "artist": { "foreignArtistId": "mb-artist", "artistName": "David Bowie" },
                    "releases": [{ "trackCount": 11 }, { "trackCount": 25 }],
                    "images": [{ "coverType": "cover", "remoteUrl": "https://images.test/low.jpg" }]
                  }
                ]
                """);
        });
        var client = new LidarrRequestProviderClient(new HttpClient(handler));

        var detail = await client.GetDetailAsync(Instance(RequestProviderKind.Lidarr), RequestMediaKind.Album, "mb-album", CancellationToken.None);

        Assert.Equal("lidarr:mb-album", lastTerm);
        Assert.Equal("Low", detail.Title);
        Assert.Equal("David Bowie", detail.Subtitle);
        Assert.Equal("Album", detail.Certification);
        Assert.Equal(50, detail.RuntimeMinutes);
        Assert.Contains("Art Rock", detail.Tags);
        Assert.Equal(2, detail.Tracks.Count);
        Assert.Equal(2, detail.TrackCount);
        Assert.Equal(new RequestTrack(1, "Speed of Life", 166), detail.Tracks[0]);
        Assert.Equal(new RequestTrack(2, "Breaking Glass", 112), detail.Tracks[1]);
    }

    [Fact]
    public async Task TestServiceReusesStoredKeyAndReturnsOptionsOnSuccess() {
        await using var db = CreateContext();
        var store = new EfRequestServiceInstanceStore(db);
        var created = await store.SaveAsync(new RequestServiceInstanceSaveRequest(
            null, RequestProviderKind.Radarr, "Movies", "http://radarr.test", "stored-key", null, null, null,
            RequestMinimumAvailability.Released, [], true, false), CancellationToken.None);

        string? seenKey = null;
        var handler = new FakeHttpHandler((request, body) => {
            seenKey = request.Headers.TryGetValues(RequestProviderHttp.ApiKeyHeader, out var values) ? values.Single() : null;
            return request.RequestUri!.AbsolutePath switch {
                "/api/v3/system/status" => Json("{}"),
                "/api/v3/qualityprofile" => Json("""[{ "id": 1, "name": "HD" }]"""),
                "/api/v3/rootfolder" => Json("""[{ "path": "/movies" }]"""),
                "/api/v3/tag" => Json("[]"),
                _ => throw new InvalidOperationException(request.RequestUri.AbsolutePath)
            };
        });
        var clients = new RequestProviderClientFactory([new RadarrRequestProviderClient(new HttpClient(handler))]);
        var tester = new RequestServiceTestService(store, clients);

        var response = await tester.TestAsync(
            new RequestServiceTestRequest(created.Id, RequestProviderKind.Radarr, "http://radarr.test", null),
            CancellationToken.None);

        Assert.True(response.Connected);
        Assert.Equal("stored-key", seenKey);
        Assert.Equal("HD", Assert.Single(response.Options!.QualityProfiles).Name);
        Assert.Equal("/movies", Assert.Single(response.Options.RootFolders).Path);
    }

    [Fact]
    public async Task TestServiceReportsFailureWhenConnectionFails() {
        await using var db = CreateContext();
        var store = new EfRequestServiceInstanceStore(db);
        var handler = new FakeHttpHandler((request, body) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var clients = new RequestProviderClientFactory([new RadarrRequestProviderClient(new HttpClient(handler))]);
        var tester = new RequestServiceTestService(store, clients);

        var response = await tester.TestAsync(
            new RequestServiceTestRequest(null, RequestProviderKind.Radarr, "http://radarr.test", "bad-key"),
            CancellationToken.None);

        Assert.False(response.Connected);
        Assert.Null(response.Options);
    }

    private static RequestServiceInstanceDetail Instance(RequestProviderKind kind, int[]? defaultTagIds = null) =>
        new(Guid.NewGuid(), kind, "Test", "http://arr.test", true, null, null, null, RequestMinimumAvailability.Released,
            defaultTagIds ?? [], true, true, kind.ToCode() + "-key");

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
