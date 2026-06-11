using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Requests;

public sealed class RequestProviderClientFactory(IEnumerable<IRequestProviderClient> clients) : IRequestProviderClientFactory {
    private readonly Dictionary<RequestProviderKind, IRequestProviderClient> _clients = clients.ToDictionary(client => client.Kind);

    public IRequestProviderClient Get(RequestProviderKind kind) =>
        _clients.TryGetValue(kind, out var client)
            ? client
            : throw new NotSupportedException($"Request provider '{kind.ToCode()}' is not registered.");
}

public sealed class RadarrRequestProviderClient(HttpClient http) : ArrRequestProviderClient(http, RequestProviderKind.Radarr, RequestProviderHttp.RadarrApiPath) {
    public override async Task<IReadOnlyList<RequestSearchResult>> SearchAsync(RequestServiceInstanceDetail instance, string query, CancellationToken cancellationToken) {
        var items = await GetArrayAsync(instance, $"{ApiPath}/movie/lookup?term={Uri.EscapeDataString(query)}", cancellationToken);
        return items.Select(item => MapMovie(instance.Id, item)).ToArray();
    }

    public override async Task<RequestDetailResponse> GetDetailAsync(RequestServiceInstanceDetail instance, RequestMediaKind kind, string externalId, CancellationToken cancellationToken) {
        var items = await GetArrayAsync(instance, $"{ApiPath}/movie/lookup?term=tmdb:{Uri.EscapeDataString(externalId)}", cancellationToken);
        var item = SelectDetail(items, candidate => Text(candidate, "tmdbId") == externalId, "Radarr did not return a movie detail.");
        return DetailFromSearch(MapMovie(instance.Id, item), item, []);
    }

    public override async Task<RequestSubmitResponse> SubmitAsync(RequestServiceInstanceDetail instance, RequestDetailResponse detail, RequestSubmitRequest request, CancellationToken cancellationToken) {
        var payload = new JsonObject {
            ["title"] = detail.Title,
            ["tmdbId"] = int.TryParse(detail.ExternalId, out var tmdbId) ? tmdbId : null,
            ["qualityProfileId"] = request.QualityProfileId ?? instance.DefaultQualityProfileId,
            ["rootFolderPath"] = request.RootFolderPath ?? instance.DefaultRootFolderPath,
            ["monitored"] = request.Monitored,
            ["minimumAvailability"] = "released",
            ["addOptions"] = new JsonObject {
                ["monitor"] = request.Monitored ? "movieOnly" : "none",
                ["searchForMovie"] = request.SearchNow
            }
        };

        var response = await SendJsonAsync(instance, HttpMethod.Post, $"{ApiPath}/movie", payload, cancellationToken);
        return Submitted(response);
    }
}

public sealed class SonarrRequestProviderClient(HttpClient http) : ArrRequestProviderClient(http, RequestProviderKind.Sonarr, RequestProviderHttp.SonarrApiPath) {
    public override async Task<IReadOnlyList<RequestSearchResult>> SearchAsync(RequestServiceInstanceDetail instance, string query, CancellationToken cancellationToken) {
        var items = await GetArrayAsync(instance, $"{ApiPath}/series/lookup?term={Uri.EscapeDataString(query)}", cancellationToken);
        return items.Select(item => MapSeries(instance.Id, item)).ToArray();
    }

    public override async Task<RequestDetailResponse> GetDetailAsync(RequestServiceInstanceDetail instance, RequestMediaKind kind, string externalId, CancellationToken cancellationToken) {
        var items = await GetArrayAsync(instance, $"{ApiPath}/series/lookup?term=tvdb:{Uri.EscapeDataString(externalId)}", cancellationToken);
        var item = SelectDetail(items, candidate => Text(candidate, "tvdbId") == externalId, "Sonarr did not return a series detail.");
        var children = Array(item, "seasons")
            .Select(season => new RequestChildOption(
                Text(season, "seasonNumber") ?? string.Empty,
                SeasonTitle(Int(season, "seasonNumber")),
                RequestMediaKind.Series,
                true,
                Int(season, "seasonNumber"),
                null,
                null))
            .Where(child => !string.IsNullOrWhiteSpace(child.Id))
            .OrderBy(child => child.Number)
            .ToArray();
        return DetailFromSearch(MapSeries(instance.Id, item), item, children);
    }

    public override async Task<RequestSubmitResponse> SubmitAsync(RequestServiceInstanceDetail instance, RequestDetailResponse detail, RequestSubmitRequest request, CancellationToken cancellationToken) {
        var selected = request.SelectedChildIds.ToHashSet(StringComparer.Ordinal);
        var seasons = new JsonArray();
        foreach (var child in detail.Children) {
            var seasonNumber = child.Number ?? (int.TryParse(child.Id, out var parsed) ? parsed : (int?)null);
            if (seasonNumber is null) {
                continue;
            }

            seasons.Add(new JsonObject {
                ["seasonNumber"] = seasonNumber.Value,
                ["monitored"] = selected.Contains(child.Id)
            });
        }

        var payload = new JsonObject {
            ["title"] = detail.Title,
            ["tvdbId"] = int.TryParse(detail.ExternalId, out var tvdbId) ? tvdbId : null,
            ["qualityProfileId"] = request.QualityProfileId ?? instance.DefaultQualityProfileId,
            ["rootFolderPath"] = request.RootFolderPath ?? instance.DefaultRootFolderPath,
            ["monitored"] = request.Monitored,
            ["seriesType"] = "standard",
            ["seasons"] = seasons,
            ["addOptions"] = new JsonObject {
                ["searchForMissingEpisodes"] = request.SearchNow
            }
        };

        var response = await SendJsonAsync(instance, HttpMethod.Post, $"{ApiPath}/series", payload, cancellationToken);
        return Submitted(response);
    }

    private static string SeasonTitle(int? number) => number == 0 ? "Specials" : $"Season {number}";
}

public sealed class LidarrRequestProviderClient(HttpClient http) : ArrRequestProviderClient(http, RequestProviderKind.Lidarr, RequestProviderHttp.LidarrApiPath) {
    public override async Task<IReadOnlyList<RequestSearchResult>> SearchAsync(RequestServiceInstanceDetail instance, string query, CancellationToken cancellationToken) {
        var items = await GetArrayAsync(instance, $"{ApiPath}/artist/lookup?term={Uri.EscapeDataString(query)}", cancellationToken);
        return items.Select(item => MapArtist(instance.Id, item)).ToArray();
    }

    public override async Task<RequestDetailResponse> GetDetailAsync(RequestServiceInstanceDetail instance, RequestMediaKind kind, string externalId, CancellationToken cancellationToken) {
        var items = await GetArrayAsync(instance, $"{ApiPath}/artist/lookup?term={Uri.EscapeDataString(externalId)}", cancellationToken);
        var item = SelectDetail(items, candidate => Text(candidate, "foreignArtistId") == externalId, "Lidarr did not return an artist detail.");
        return DetailFromSearch(MapArtist(instance.Id, item), item, []);
    }

    public override async Task<RequestSubmitResponse> SubmitAsync(RequestServiceInstanceDetail instance, RequestDetailResponse detail, RequestSubmitRequest request, CancellationToken cancellationToken) {
        if (request.Kind != RequestMediaKind.Artist) {
            throw new NotSupportedException("Lidarr v1 request support is limited to artists with optional album monitoring.");
        }

        var payload = new JsonObject {
            ["artistName"] = detail.Title,
            ["foreignArtistId"] = detail.ExternalId,
            ["qualityProfileId"] = request.QualityProfileId ?? instance.DefaultQualityProfileId,
            ["metadataProfileId"] = request.MetadataProfileId ?? instance.DefaultMetadataProfileId,
            ["rootFolderPath"] = request.RootFolderPath ?? instance.DefaultRootFolderPath,
            ["monitored"] = request.Monitored,
            ["addOptions"] = new JsonObject {
                ["searchForMissingAlbums"] = request.SearchNow
            }
        };

        await SendJsonAsync(instance, HttpMethod.Post, $"{ApiPath}/artist", payload, cancellationToken);

        var albumIds = request.SelectedChildIds
            .Select(id => int.TryParse(id, out var albumId) ? albumId : (int?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToArray();
        if (albumIds.Length > 0) {
            await SendJsonAsync(instance, HttpMethod.Put, $"{ApiPath}/album/monitor", new JsonObject {
                ["albumIds"] = new JsonArray(albumIds.Select(id => (JsonNode)id).ToArray()),
                ["monitored"] = true
            }, cancellationToken);
        }

        return new RequestSubmitResponse(true, null, null);
    }
}

public abstract class ArrRequestProviderClient(HttpClient http, RequestProviderKind kind, string apiPath) : IRequestProviderClient {
    protected string ApiPath { get; } = apiPath;
    public RequestProviderKind Kind { get; } = kind;

    public abstract Task<IReadOnlyList<RequestSearchResult>> SearchAsync(RequestServiceInstanceDetail instance, string query, CancellationToken cancellationToken);
    public abstract Task<RequestDetailResponse> GetDetailAsync(RequestServiceInstanceDetail instance, RequestMediaKind kind, string externalId, CancellationToken cancellationToken);
    public abstract Task<RequestSubmitResponse> SubmitAsync(RequestServiceInstanceDetail instance, RequestDetailResponse detail, RequestSubmitRequest request, CancellationToken cancellationToken);

    public async Task<IReadOnlyList<RequestServiceOption>> GetOptionsAsync(RequestServiceInstanceDetail instance, CancellationToken cancellationToken) {
        var profiles = await GetArrayAsync(instance, $"{ApiPath}/qualityprofile", cancellationToken);
        var roots = await GetArrayAsync(instance, $"{ApiPath}/rootfolder", cancellationToken);
        return profiles.Select(profile => new RequestServiceOption(Text(profile, "id") ?? string.Empty, Text(profile, "name") ?? string.Empty, null))
            .Concat(roots.Select(root => new RequestServiceOption(Text(root, "path") ?? string.Empty, Text(root, "path") ?? string.Empty, Text(root, "path"))))
            .Where(option => !string.IsNullOrWhiteSpace(option.Id))
            .ToArray();
    }

    protected async Task<IReadOnlyList<JsonElement>> GetArrayAsync(RequestServiceInstanceDetail instance, string path, CancellationToken cancellationToken) {
        using var request = BuildRequest(instance, HttpMethod.Get, path, null);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray()
            : [];
    }

    protected async Task<JsonElement> SendJsonAsync(RequestServiceInstanceDetail instance, HttpMethod method, string path, JsonObject payload, CancellationToken cancellationToken) {
        using var request = BuildRequest(instance, method, path, JsonContent.Create(payload));
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        if (stream.Length == 0) {
            return default;
        }

        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    protected HttpRequestMessage BuildRequest(RequestServiceInstanceDetail instance, HttpMethod method, string path, HttpContent? content) {
        var request = new HttpRequestMessage(method, new Uri(new Uri(instance.BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/'))) {
            Content = content
        };
        if (!string.IsNullOrWhiteSpace(instance.ApiKey)) {
            request.Headers.Add(RequestProviderHttp.ApiKeyHeader, instance.ApiKey);
        }

        return request;
    }

    protected static RequestSubmitResponse Submitted(JsonElement response) =>
        new(true, Text(response, "id"), null);

    protected static RequestSearchResult MapMovie(Guid serviceId, JsonElement item) =>
        new(serviceId, RequestProviderKind.Radarr, RequestMediaKind.Movie, Text(item, "tmdbId") ?? string.Empty,
            Text(item, "title") ?? string.Empty, Int(item, "year"), Text(item, "overview"), Image(item, "poster"),
            Image(item, "fanart") ?? Image(item, "backdrop"), Rating(item), Int(item, "runtime"), Text(item, "certification"),
            StringArray(item, "genres"), false, true);

    protected static RequestSearchResult MapSeries(Guid serviceId, JsonElement item) =>
        new(serviceId, RequestProviderKind.Sonarr, RequestMediaKind.Series, Text(item, "tvdbId") ?? string.Empty,
            Text(item, "title") ?? string.Empty, YearFromDate(Text(item, "firstAired")), Text(item, "overview"), Image(item, "poster"),
            Image(item, "fanart") ?? Image(item, "backdrop"), Rating(item), RuntimeFromMinutesArray(item), Text(item, "certification"),
            StringArray(item, "genres"), false, true);

    protected static RequestSearchResult MapArtist(Guid serviceId, JsonElement item) =>
        new(serviceId, RequestProviderKind.Lidarr, RequestMediaKind.Artist, Text(item, "foreignArtistId") ?? string.Empty,
            Text(item, "artistName") ?? Text(item, "name") ?? string.Empty, null, Text(item, "overview"), Image(item, "poster"),
            Image(item, "fanart") ?? Image(item, "banner"), Rating(item), null, Text(item, "status"), StringArray(item, "genres"), false, true);

    protected static RequestDetailResponse DetailFromSearch(RequestSearchResult result, JsonElement item, IReadOnlyList<RequestChildOption> children) =>
        new(result.Source, result.Kind, result.ExternalId, result.Title, result.Year, result.Overview, result.PosterUrl,
            result.BackdropUrl, result.Rating, result.RuntimeMinutes, result.Certification, result.Tags,
            StringArray(item, "studios").Concat(StringArray(item, "networks")).ToArray(),
            Credits(item), children, []);

    protected static JsonElement SelectDetail(IReadOnlyList<JsonElement> items, Func<JsonElement, bool> predicate, string failureMessage) {
        foreach (var item in items) {
            if (predicate(item)) {
                return item;
            }
        }

        if (items.Count > 0) {
            return items[0];
        }

        throw new InvalidOperationException(failureMessage);
    }

    protected static IReadOnlyList<JsonElement> Array(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : [];

    protected static IReadOnlyList<string> StringArray(JsonElement item, string name) =>
        Array(item, name).Select(element => element.ValueKind == JsonValueKind.String ? element.GetString() : Text(element, "name"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

    protected static IReadOnlyList<string> Credits(JsonElement item) =>
        StringArray(item, "actors").Concat(StringArray(item, "crew")).Concat(StringArray(item, "members")).ToArray();

    protected static string? Text(JsonElement item, string name) {
        if (!item.TryGetProperty(name, out var value)) {
            return null;
        }

        return value.ValueKind switch {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    protected static int? Int(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    protected static decimal? Rating(JsonElement item) {
        if (!item.TryGetProperty("ratings", out var ratings)) {
            return null;
        }

        if (ratings.ValueKind == JsonValueKind.Object &&
            ratings.TryGetProperty("value", out var value) &&
            value.TryGetDecimal(out var rating)) {
            return rating;
        }

        return null;
    }

    protected static string? Image(JsonElement item, string coverType) =>
        Array(item, "images")
            .Where(image => string.Equals(Text(image, "coverType"), coverType, StringComparison.OrdinalIgnoreCase))
            .Select(image => Text(image, "remoteUrl") ?? Text(image, "url"))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static int? YearFromDate(string? value) =>
        DateTimeOffset.TryParse(value, out var date) ? date.Year : null;

    private static int? RuntimeFromMinutesArray(JsonElement item) =>
        Array(item, "runtime").Select(runtime => runtime.TryGetInt32(out var minutes) ? minutes : 0).FirstOrDefault() is var value && value > 0
            ? value
            : null;
}
