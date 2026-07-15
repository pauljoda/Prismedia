using System.Net;
using System.Text;
using Prismedia.Infrastructure.Subtitles;

namespace Prismedia.Infrastructure.Tests;

public sealed class OpenSubtitlesClientTests {
    [Fact]
    public async Task SearchSendsOshashAndSeparatesIdentityFromQuality() {
        var handler = new RecordingHandler(request => {
            Assert.Equal("demo-key", request.Headers.GetValues("Api-Key").Single());
            Assert.Contains("moviehash=0123456789abcdef", request.RequestUri!.Query);
            Assert.Contains("languages=en", request.RequestUri.Query);
            return JsonResponse("""
                {
                  "data": [{
                    "id": "991",
                    "attributes": {
                      "language": "en",
                      "release": "Example.Movie.2025.1080p.WEB",
                      "url": "https://www.opensubtitles.com/subtitles/991",
                      "hearing_impaired": false,
                      "foreign_parts_only": false,
                      "ai_translated": false,
                      "machine_translated": false,
                      "moviehash_match": true,
                      "download_count": 1234,
                      "ratings": 9.1,
                      "uploader": { "trusted": true },
                      "feature_details": { "year": 2025, "imdb_id": 1234567 },
                      "files": [{ "file_id": 77, "file_name": "Example.Movie.2025.srt" }]
                    }
                  }]
                }
                """);
        });
        var client = new OpenSubtitlesClient(new HttpClient(handler));

        var results = await client.SearchAsync(
            Connection(),
            new OpenSubtitlesSearchContext(
                "Example Movie",
                "Example.Movie.2025.mkv",
                "0123456789abcdef",
                "tt1234567",
                null,
                null,
                2025,
                null,
                null,
                ["en"]),
            CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(100, result.MatchConfidence);
        Assert.True(result.AutomaticEligible);
        Assert.True(result.QualityScore > 50);
        Assert.Equal("991:77", result.CandidateId);
        Assert.Contains("Exact file hash", result.MatchReasons);
    }

    [Fact]
    public async Task DownloadUsesRecentlySearchedCandidateAndRequestsSrt() {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch {
            "/api/v1/subtitles" => JsonResponse("""
                {"data":[{"id":"991","attributes":{"language":"en","release":"Example Release","hearing_impaired":false,"foreign_parts_only":false,"ai_translated":false,"machine_translated":false,"moviehash_match":true,"download_count":1,"ratings":8,"files":[{"file_id":77,"file_name":"original.ass"}]}}]}
                """),
            "/api/v1/login" => JsonResponse("""
                {"token":"jwt-token","base_url":"https://api.opensubtitles.com"}
                """),
            "/api/v1/download" => AssertDownloadRequest(request),
            "/temporary/77" => new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("1\n00:00:01,000 --> 00:00:02,000\nHello\n", Encoding.UTF8, "application/x-subrip"),
            },
            _ => throw new InvalidOperationException(request.RequestUri.AbsoluteUri),
        });
        var client = new OpenSubtitlesClient(new HttpClient(handler));

        var results = await client.SearchAsync(
            Connection(),
            new OpenSubtitlesSearchContext(
                "Example Movie",
                "Example.Movie.2025.mkv",
                null,
                null,
                null,
                null,
                2025,
                null,
                null,
                ["en"]),
            CancellationToken.None);
        var artifact = await client.DownloadAsync(Connection(), "991:77", CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("downloaded.srt", artifact.FileName);
        Assert.Equal("srt", artifact.Format);
        Assert.Equal("en", artifact.Language);
        Assert.Contains("Hello", Encoding.UTF8.GetString(artifact.Content));
    }

    [Fact]
    public async Task DownloadRejectsCandidateThatWasNotReturnedBySearch() {
        var handler = new RecordingHandler(request =>
            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}"));
        var client = new OpenSubtitlesClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<OpenSubtitlesException>(() =>
            client.DownloadAsync(Connection(), "991:77", CancellationToken.None));

        Assert.Contains("candidate expired", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangedCredentialsInvalidateTheCachedLoginToken() {
        var loginCount = 0;
        var expectedToken = "";
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch {
            "/api/v1/login" => LoginResponse(),
            "/api/v1/infos/user" => UserResponse(request),
            _ => throw new InvalidOperationException(request.RequestUri.AbsoluteUri),
        });
        var client = new OpenSubtitlesClient(new HttpClient(handler));

        Assert.True((await client.TestAsync(Connection(), CancellationToken.None)).Success);
        Assert.True((await client.TestAsync(
            new OpenSubtitlesConnection("demo-key", "other-user", "other-password", false, false),
            CancellationToken.None)).Success);

        Assert.Equal(2, loginCount);
        return;

        HttpResponseMessage LoginResponse() {
            expectedToken = $"jwt-token-{++loginCount}";
            return JsonResponse($$"""
                {"token":"{{expectedToken}}","base_url":"https://api.opensubtitles.com"}
                """);
        }

        HttpResponseMessage UserResponse(HttpRequestMessage request) {
            Assert.Equal(expectedToken, request.Headers.Authorization?.Parameter);
            return JsonResponse("{}");
        }
    }

    private static HttpResponseMessage AssertDownloadRequest(HttpRequestMessage request) {
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("jwt-token", request.Headers.Authorization?.Parameter);
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Assert.Contains("\"file_id\":77", body);
        Assert.Contains("\"sub_format\":\"srt\"", body);
        return JsonResponse("""
            {"link":"https://files.opensubtitles.test/temporary/77","file_name":"downloaded.srt","requests":1,"remaining":9}
            """);
    }

    private static OpenSubtitlesConnection Connection() =>
        new("demo-key", "demo-user", "demo-password", false, false);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK) {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }
}
