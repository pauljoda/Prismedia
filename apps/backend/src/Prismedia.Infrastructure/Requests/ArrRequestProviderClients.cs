using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Requests;

public static class ArrJsonFields {
    public const string Actors = "actors";
    public const string AddOptions = "addOptions";
    public const string AlbumIds = "albumIds";
    public const string AlbumType = "albumType";
    public const string Certification = "certification";
    public const string CoverType = "coverType";
    public const string Crew = "crew";
    public const string Disambiguation = "disambiguation";
    public const string Duration = "duration";
    public const string Fanart = "fanart";
    public const string Genres = "genres";
    public const string Id = "id";
    public const string Images = "images";
    public const string Label = "label";
    public const string Members = "members";
    public const string Monitored = "monitored";
    public const string Name = "name";
    public const string Networks = "networks";
    public const string Overview = "overview";
    public const string Path = "path";
    public const string Poster = "poster";
    public const string QualityProfileId = "qualityProfileId";
    public const string Ratings = "ratings";
    public const string RemoteUrl = "remoteUrl";
    public const string RootFolderPath = "rootFolderPath";
    public const string Runtime = "runtime";
    public const string Seasons = "seasons";
    public const string SeasonFolder = "seasonFolder";
    public const string SeasonNumber = "seasonNumber";
    public const string SeriesType = "seriesType";
    public const string Studio = "studio";
    public const string Studios = "studios";
    public const string Tags = "tags";
    public const string Title = "title";
    public const string Url = "url";
    public const string Value = "value";
    public const string Year = "year";
}

public static class RadarrProtocol {
    public const string TmdbId = "tmdbId";
    public const string MinimumAvailability = "minimumAvailability";
    public const string Monitor = "monitor";
    public const string MonitorMovieOnly = "movieOnly";
    public const string MonitorNone = "none";
    public const string SearchForMovie = "searchForMovie";
    public const string MovieEndpoint = "movie";
    public const string MovieLookupEndpoint = "movie/lookup";
}

public static class SonarrProtocol {
    public const string TvdbId = "tvdbId";
    public const string EpisodeCount = "episodeCount";
    public const string FirstAired = "firstAired";
    public const string Network = "network";
    public const string Statistics = "statistics";
    public const string SearchForMissingEpisodes = "searchForMissingEpisodes";
    public const string SeriesTypeStandard = "standard";
    public const string SeriesEndpoint = "series";
    public const string SeriesLookupEndpoint = "series/lookup";
}

public static class LidarrProtocol {
    public const string ArtistEndpoint = "artist";
    public const string ArtistLookupEndpoint = "artist/lookup";
    public const string AlbumLookupEndpoint = "album/lookup";
    /// <summary>Lookup term prefix that makes Lidarr resolve a MusicBrainz id instead of text-searching it.</summary>
    public const string MbidSearchPrefix = "lidarr:";
    public const string Artist = "artist";
    public const string ArtistType = "artistType";
    public const string ForeignAlbumId = "foreignAlbumId";
    public const string ForeignArtistId = "foreignArtistId";
    public const string ArtistName = "artistName";
    public const string Releases = "releases";
    public const string SecondaryTypes = "secondaryTypes";
    public const string TrackCount = "trackCount";
    public const string MetadataProfileId = "metadataProfileId";
    public const string Monitor = "monitor";
    public const string MonitorAll = "all";
    public const string MonitorNone = "none";
    public const string ReleaseDate = "releaseDate";
    public const string SearchForMissingAlbums = "searchForMissingAlbums";
    public const string Status = "status";
}

/// <summary>
/// MusicBrainz / Cover Art Archive wire vocabulary. Lidarr cannot list an artist's albums or an
/// album's tracks before they are added to its library, so the request detail pages enrich Lidarr
/// results straight from MusicBrainz using the same MBIDs Lidarr exposes.
/// </summary>
public static class MusicBrainzProtocol {
    public const string ApiBase = "https://musicbrainz.org/ws/2/";
    public const string CoverArtReleaseGroupBase = "https://coverartarchive.org/release-group/";
    public const string CoverArtFrontThumb = "/front-250";
    /// <summary>MusicBrainz requires an identifying User-Agent on every request.</summary>
    public const string UserAgent = "Prismedia/1.0 (+https://github.com/pauljoda/prismedia)";

    public const string ReleaseGroupEndpoint = "release-group";
    public const string ReleaseEndpoint = "release";
    public const string ArtistQuery = "artist";
    public const string ReleaseGroupQuery = "release-group";
    public const string TypeQuery = "type";
    public const string AlbumAndEpTypes = "album|ep";
    public const string StatusOfficial = "official";
    public const string IncludeRecordings = "recordings";

    public const string ReleaseGroups = "release-groups";
    public const string Releases = "releases";
    public const string PrimaryType = "primary-type";
    public const string SecondaryTypes = "secondary-types";
    public const string FirstReleaseDate = "first-release-date";
    public const string Media = "media";
    public const string Tracks = "tracks";
    public const string Position = "position";
    public const string Length = "length";
    public const string TrackCount = "track-count";
}

public static class ArrOptionEndpoints {
    public const string QualityProfile = "qualityprofile";
    public const string MetadataProfile = "metadataprofile";
    public const string RootFolder = "rootfolder";
    public const string SystemStatus = "system/status";
    public const string Tag = "tag";
}

public static class ArrImageTypes {
    public const string Backdrop = "backdrop";
    public const string Banner = "banner";
    public const string Cover = "cover";
    public const string Fanart = "fanart";
    public const string Poster = "poster";
}

public sealed class RequestProviderClientFactory(IEnumerable<IRequestProviderClient> clients) : IRequestProviderClientFactory {
    private readonly Dictionary<RequestProviderKind, IRequestProviderClient> _clients = clients.ToDictionary(client => client.Kind);

    public IRequestProviderClient Get(RequestProviderKind kind) =>
        _clients.TryGetValue(kind, out var client)
            ? client
            : throw new NotSupportedException($"Request provider '{kind.ToCode()}' is not registered.");
}

public sealed class RadarrRequestProviderClient(HttpClient http) : ArrRequestProviderClient(http, RequestProviderKind.Radarr, RequestProviderHttp.RadarrApiPath) {
    public override async Task<IReadOnlyList<RequestSearchResult>> SearchAsync(RequestServiceInstanceDetail instance, string query, CancellationToken cancellationToken) {
        var items = await GetArrayAsync(instance, $"{ApiPath}/{RadarrProtocol.MovieLookupEndpoint}?term={Uri.EscapeDataString(query)}", cancellationToken);
        return items.Select(item => MapMovie(instance.Id, item)).ToArray();
    }

    public override async Task<RequestDetailResponse> GetDetailAsync(RequestServiceInstanceDetail instance, RequestMediaKind kind, string externalId, CancellationToken cancellationToken) {
        var item = await LookupMovieAsync(instance, externalId, cancellationToken);
        return DetailFromSearch(MapMovie(instance.Id, item), item, []);
    }

    public override async Task<RequestSubmitResponse> SubmitAsync(RequestServiceInstanceDetail instance, RequestDetailResponse detail, RequestSubmitRequest request, CancellationToken cancellationToken) {
        var payload = ToObject(await LookupMovieAsync(instance, detail.ExternalId, cancellationToken));
        payload[ArrJsonFields.QualityProfileId] = request.QualityProfileId ?? instance.DefaultQualityProfileId;
        payload[ArrJsonFields.RootFolderPath] = request.RootFolderPath ?? instance.DefaultRootFolderPath;
        payload[ArrJsonFields.Monitored] = request.Monitored;
        payload[RadarrProtocol.MinimumAvailability] = instance.MinimumAvailability.ToCode();
        ApplyDefaultTags(payload, instance);
        payload[ArrJsonFields.AddOptions] = new JsonObject {
            [RadarrProtocol.Monitor] = request.Monitored ? RadarrProtocol.MonitorMovieOnly : RadarrProtocol.MonitorNone,
            [RadarrProtocol.SearchForMovie] = request.SearchNow
        };

        var response = await SendJsonAsync(instance, HttpMethod.Post, $"{ApiPath}/{RadarrProtocol.MovieEndpoint}", payload, cancellationToken);
        return Submitted(response);
    }

    private async Task<JsonElement> LookupMovieAsync(RequestServiceInstanceDetail instance, string externalId, CancellationToken cancellationToken) {
        var items = await GetArrayAsync(instance, $"{ApiPath}/{RadarrProtocol.MovieLookupEndpoint}?term=tmdb:{Uri.EscapeDataString(externalId)}", cancellationToken);
        return SelectDetail(items, candidate => Text(candidate, RadarrProtocol.TmdbId) == externalId, "Radarr did not return a movie detail.");
    }
}

public sealed class SonarrRequestProviderClient(HttpClient http) : ArrRequestProviderClient(http, RequestProviderKind.Sonarr, RequestProviderHttp.SonarrApiPath) {
    public override async Task<IReadOnlyList<RequestSearchResult>> SearchAsync(RequestServiceInstanceDetail instance, string query, CancellationToken cancellationToken) {
        var items = await GetArrayAsync(instance, $"{ApiPath}/{SonarrProtocol.SeriesLookupEndpoint}?term={Uri.EscapeDataString(query)}", cancellationToken);
        return items.Select(item => MapSeries(instance.Id, item)).ToArray();
    }

    public override async Task<RequestDetailResponse> GetDetailAsync(RequestServiceInstanceDetail instance, RequestMediaKind kind, string externalId, CancellationToken cancellationToken) {
        var item = await LookupSeriesAsync(instance, externalId, cancellationToken);
        var children = Array(item, ArrJsonFields.Seasons)
            .Select(season => new RequestChildOption(
                Text(season, ArrJsonFields.SeasonNumber) ?? string.Empty,
                SeasonTitle(Int(season, ArrJsonFields.SeasonNumber)),
                RequestMediaKind.Series,
                true,
                Int(season, ArrJsonFields.SeasonNumber),
                null,
                Int(Prop(season, SonarrProtocol.Statistics), SonarrProtocol.EpisodeCount),
                null,
                null))
            .Where(child => !string.IsNullOrWhiteSpace(child.Id))
            .OrderBy(child => child.Number)
            .ToArray();
        return DetailFromSearch(MapSeries(instance.Id, item), item, children);
    }

    public override async Task<RequestSubmitResponse> SubmitAsync(RequestServiceInstanceDetail instance, RequestDetailResponse detail, RequestSubmitRequest request, CancellationToken cancellationToken) {
        var payload = ToObject(await LookupSeriesAsync(instance, detail.ExternalId, cancellationToken));
        var selected = request.SelectedChildIds.ToHashSet(StringComparer.Ordinal);
        var seasons = new JsonArray();
        foreach (var child in detail.Children) {
            var seasonNumber = child.Number ?? (int.TryParse(child.Id, out var parsed) ? parsed : (int?)null);
            if (seasonNumber is null) {
                continue;
            }

            seasons.Add(new JsonObject {
                [ArrJsonFields.SeasonNumber] = seasonNumber.Value,
                [ArrJsonFields.Monitored] = selected.Contains(child.Id)
            });
        }

        payload[ArrJsonFields.QualityProfileId] = request.QualityProfileId ?? instance.DefaultQualityProfileId;
        payload[ArrJsonFields.RootFolderPath] = request.RootFolderPath ?? instance.DefaultRootFolderPath;
        payload[ArrJsonFields.Monitored] = request.Monitored;
        if (!payload.ContainsKey(ArrJsonFields.SeriesType)) {
            payload[ArrJsonFields.SeriesType] = SonarrProtocol.SeriesTypeStandard;
        }
        payload[ArrJsonFields.SeasonFolder] = true;
        payload[ArrJsonFields.Seasons] = seasons;
        ApplyDefaultTags(payload, instance);
        payload[ArrJsonFields.AddOptions] = new JsonObject {
            [SonarrProtocol.SearchForMissingEpisodes] = request.SearchNow
        };

        var response = await SendJsonAsync(instance, HttpMethod.Post, $"{ApiPath}/{SonarrProtocol.SeriesEndpoint}", payload, cancellationToken);
        return Submitted(response);
    }

    private static string SeasonTitle(int? number) => number == 0 ? "Specials" : $"Season {number}";

    private async Task<JsonElement> LookupSeriesAsync(RequestServiceInstanceDetail instance, string externalId, CancellationToken cancellationToken) {
        var items = await GetArrayAsync(instance, $"{ApiPath}/{SonarrProtocol.SeriesLookupEndpoint}?term=tvdb:{Uri.EscapeDataString(externalId)}", cancellationToken);
        return SelectDetail(items, candidate => Text(candidate, SonarrProtocol.TvdbId) == externalId, "Sonarr did not return a series detail.");
    }
}

public sealed class LidarrRequestProviderClient(HttpClient http) : ArrRequestProviderClient(http, RequestProviderKind.Lidarr, RequestProviderHttp.LidarrApiPath) {
    public override async Task<IReadOnlyList<RequestSearchResult>> SearchAsync(RequestServiceInstanceDetail instance, string query, CancellationToken cancellationToken) {
        var artists = await GetArrayAsync(instance, $"{ApiPath}/{LidarrProtocol.ArtistLookupEndpoint}?term={Uri.EscapeDataString(query)}", cancellationToken);
        var albums = await GetArrayAsync(instance, $"{ApiPath}/{LidarrProtocol.AlbumLookupEndpoint}?term={Uri.EscapeDataString(query)}", cancellationToken);
        return artists.Select(item => MapArtist(instance.Id, item))
            .Concat(albums.Select(item => MapAlbum(instance.Id, item)))
            .ToArray();
    }

    public override async Task<RequestDetailResponse> GetDetailAsync(RequestServiceInstanceDetail instance, RequestMediaKind kind, string externalId, CancellationToken cancellationToken) {
        if (kind == RequestMediaKind.Album) {
            var album = await LookupAlbumAsync(instance, externalId, cancellationToken);
            var tracks = await GetAlbumTracksAsync(externalId, cancellationToken);
            return DetailFromSearch(MapAlbum(instance.Id, album), album, [], tracks);
        }

        var item = await LookupArtistAsync(instance, externalId, cancellationToken);
        var children = await GetArtistDiscographyAsync(externalId, cancellationToken);
        if (children.Count == 0) {
            var artistName = Text(item, LidarrProtocol.ArtistName) ?? Text(item, ArrJsonFields.Name);
            if (!string.IsNullOrWhiteSpace(artistName)) {
                children = await GetArtistAlbumsAsync(instance, externalId, artistName, cancellationToken);
            }
        }

        return DetailFromSearch(MapArtist(instance.Id, item), item, children);
    }

    public override async Task<RequestSubmitResponse> SubmitAsync(RequestServiceInstanceDetail instance, RequestDetailResponse detail, RequestSubmitRequest request, CancellationToken cancellationToken) {
        if (request.Kind != RequestMediaKind.Artist) {
            throw new NotSupportedException("Lidarr standalone album requests require an existing artist context; submit is disabled unless the request is for an artist with optional album monitoring.");
        }

        var payload = ToObject(await LookupArtistAsync(instance, detail.ExternalId, cancellationToken));
        payload[ArrJsonFields.QualityProfileId] = request.QualityProfileId ?? instance.DefaultQualityProfileId;
        payload[LidarrProtocol.MetadataProfileId] = request.MetadataProfileId ?? instance.DefaultMetadataProfileId;
        payload[ArrJsonFields.RootFolderPath] = request.RootFolderPath ?? instance.DefaultRootFolderPath;
        payload[ArrJsonFields.Monitored] = request.Monitored;
        ApplyDefaultTags(payload, instance);
        payload[ArrJsonFields.AddOptions] = new JsonObject {
            [LidarrProtocol.Monitor] = request.Monitored ? LidarrProtocol.MonitorAll : LidarrProtocol.MonitorNone,
            [LidarrProtocol.SearchForMissingAlbums] = request.SearchNow
        };

        await SendJsonAsync(instance, HttpMethod.Post, $"{ApiPath}/{LidarrProtocol.ArtistEndpoint}", payload, cancellationToken);

        return new RequestSubmitResponse(true, null, null);
    }

    /// <summary>Resolves an artist by MusicBrainz id using Lidarr's <c>lidarr:</c> lookup prefix; plain text terms never match MBIDs.</summary>
    private async Task<JsonElement> LookupArtistAsync(RequestServiceInstanceDetail instance, string externalId, CancellationToken cancellationToken) {
        var items = await GetArrayAsync(instance, $"{ApiPath}/{LidarrProtocol.ArtistLookupEndpoint}?term={Uri.EscapeDataString(LidarrProtocol.MbidSearchPrefix + externalId)}", cancellationToken);
        return SelectDetail(items, candidate => Text(candidate, LidarrProtocol.ForeignArtistId) == externalId, "Lidarr did not return an artist detail.");
    }

    private async Task<JsonElement> LookupAlbumAsync(RequestServiceInstanceDetail instance, string externalId, CancellationToken cancellationToken) {
        var items = await GetArrayAsync(instance, $"{ApiPath}/{LidarrProtocol.AlbumLookupEndpoint}?term={Uri.EscapeDataString(LidarrProtocol.MbidSearchPrefix + externalId)}", cancellationToken);
        return SelectDetail(items, candidate => Text(candidate, LidarrProtocol.ForeignAlbumId) == externalId || Text(candidate, ArrJsonFields.Id) == externalId, "Lidarr did not return an album detail.");
    }

    /// <summary>
    /// Lists an artist's full discography (albums and EPs) from MusicBrainz, newest first, with
    /// Cover Art Archive thumbnails. Returns an empty list on any failure so callers can fall back.
    /// </summary>
    private async Task<IReadOnlyList<RequestChildOption>> GetArtistDiscographyAsync(string artistMbid, CancellationToken cancellationToken) {
        try {
            var url = new Uri(
                $"{MusicBrainzProtocol.ApiBase}{MusicBrainzProtocol.ReleaseGroupEndpoint}" +
                $"?{MusicBrainzProtocol.ArtistQuery}={Uri.EscapeDataString(artistMbid)}" +
                $"&{MusicBrainzProtocol.TypeQuery}={Uri.EscapeDataString(MusicBrainzProtocol.AlbumAndEpTypes)}" +
                "&fmt=json&limit=100");
            var root = await GetExternalJsonAsync(url, cancellationToken);
            return Array(root, MusicBrainzProtocol.ReleaseGroups)
                .Select(group => new RequestChildOption(
                    Text(group, ArrJsonFields.Id) ?? string.Empty,
                    Text(group, ArrJsonFields.Title) ?? string.Empty,
                    RequestMediaKind.Album,
                    false,
                    null,
                    YearFromDate(Text(group, MusicBrainzProtocol.FirstReleaseDate)),
                    null,
                    ReleaseGroupTypeLabel(group),
                    $"{MusicBrainzProtocol.CoverArtReleaseGroupBase}{Text(group, ArrJsonFields.Id)}{MusicBrainzProtocol.CoverArtFrontThumb}"))
                .Where(child => !string.IsNullOrWhiteSpace(child.Id) && !string.IsNullOrWhiteSpace(child.Title))
                .OrderByDescending(child => child.Year ?? 0)
                .ThenBy(child => child.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return [];
        }
    }

    /// <summary>
    /// Track listing for an album from MusicBrainz: takes the official release with the fewest
    /// tracks (usually the standard edition) and numbers tracks sequentially across discs.
    /// Returns an empty list on any failure; tracks are review aids, never load-bearing.
    /// </summary>
    private async Task<IReadOnlyList<RequestTrack>> GetAlbumTracksAsync(string albumMbid, CancellationToken cancellationToken) {
        try {
            var url = new Uri(
                $"{MusicBrainzProtocol.ApiBase}{MusicBrainzProtocol.ReleaseEndpoint}" +
                $"?{MusicBrainzProtocol.ReleaseGroupQuery}={Uri.EscapeDataString(albumMbid)}" +
                $"&inc={MusicBrainzProtocol.IncludeRecordings}&status={MusicBrainzProtocol.StatusOfficial}&fmt=json&limit=25");
            var root = await GetExternalJsonAsync(url, cancellationToken);
            var release = Array(root, MusicBrainzProtocol.Releases)
                .Select(candidate => (Item: candidate, TrackTotal: Array(candidate, MusicBrainzProtocol.Media).Sum(medium => Int(medium, MusicBrainzProtocol.TrackCount) ?? 0)))
                .Where(candidate => candidate.TrackTotal > 0)
                .OrderBy(candidate => candidate.TrackTotal)
                .Select(candidate => candidate.Item)
                .FirstOrDefault();
            if (release.ValueKind != JsonValueKind.Object) {
                return [];
            }

            return Array(release, MusicBrainzProtocol.Media)
                .SelectMany(medium => Array(medium, MusicBrainzProtocol.Tracks))
                .Select((track, index) => new RequestTrack(
                    index + 1,
                    Text(track, ArrJsonFields.Title) ?? string.Empty,
                    DurationSeconds(track)))
                .Where(track => !string.IsNullOrWhiteSpace(track.Title))
                .ToArray();
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return [];
        }
    }

    private static string? ReleaseGroupTypeLabel(JsonElement group) {
        var label = string.Join(" · ", new[] { Text(group, MusicBrainzProtocol.PrimaryType) }
            .Concat(StringArray(group, MusicBrainzProtocol.SecondaryTypes))
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(label) ? null : label;
    }

    private static int? DurationSeconds(JsonElement track) =>
        track.ValueKind == JsonValueKind.Object &&
        track.TryGetProperty(MusicBrainzProtocol.Length, out var length) &&
        length.TryGetInt64(out var milliseconds) && milliseconds > 0
            ? (int)Math.Round(milliseconds / 1000d)
            : null;

    /// <summary>
    /// Fallback album listing via Lidarr's own lookup when MusicBrainz is unreachable: text-searches
    /// by artist name and keeps results whose embedded artist matches the requested MusicBrainz id.
    /// </summary>
    private async Task<IReadOnlyList<RequestChildOption>> GetArtistAlbumsAsync(RequestServiceInstanceDetail instance, string externalId, string artistName, CancellationToken cancellationToken) {
        var albums = await GetArrayAsync(instance, $"{ApiPath}/{LidarrProtocol.AlbumLookupEndpoint}?term={Uri.EscapeDataString(artistName)}", cancellationToken);
        return albums
            .Where(album => Text(Prop(album, LidarrProtocol.Artist), LidarrProtocol.ForeignArtistId) == externalId)
            .Select(album => new RequestChildOption(
                Text(album, LidarrProtocol.ForeignAlbumId) ?? Text(album, ArrJsonFields.Id) ?? string.Empty,
                Text(album, ArrJsonFields.Title) ?? string.Empty,
                RequestMediaKind.Album,
                false,
                null,
                YearFromDate(Text(album, LidarrProtocol.ReleaseDate)),
                null,
                Text(album, ArrJsonFields.AlbumType),
                Image(album, ArrImageTypes.Cover)))
            .Where(album => !string.IsNullOrWhiteSpace(album.Id) && !string.IsNullOrWhiteSpace(album.Title))
            .ToArray();
    }
}

public abstract class ArrRequestProviderClient(HttpClient http, RequestProviderKind kind, string apiPath) : IRequestProviderClient {
    protected string ApiPath { get; } = apiPath;
    public RequestProviderKind Kind { get; } = kind;

    public abstract Task<IReadOnlyList<RequestSearchResult>> SearchAsync(RequestServiceInstanceDetail instance, string query, CancellationToken cancellationToken);
    public abstract Task<RequestDetailResponse> GetDetailAsync(RequestServiceInstanceDetail instance, RequestMediaKind kind, string externalId, CancellationToken cancellationToken);
    public abstract Task<RequestSubmitResponse> SubmitAsync(RequestServiceInstanceDetail instance, RequestDetailResponse detail, RequestSubmitRequest request, CancellationToken cancellationToken);

    public async Task<RequestConnectionTestResponse> TestAsync(RequestServiceInstanceDetail instance, CancellationToken cancellationToken) {
        try {
            using var request = BuildRequest(instance, HttpMethod.Get, $"{ApiPath}/{ArrOptionEndpoints.SystemStatus}", null);
            using var response = await http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? new RequestConnectionTestResponse(true, "Connected")
                : new RequestConnectionTestResponse(false, $"Request service returned {(int)response.StatusCode}.");
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return new RequestConnectionTestResponse(false, ex.Message);
        }
    }

    public async Task<RequestServiceOptionsResponse> GetOptionsAsync(RequestServiceInstanceDetail instance, CancellationToken cancellationToken) {
        var profiles = await GetArrayAsync(instance, $"{ApiPath}/{ArrOptionEndpoints.QualityProfile}", cancellationToken);
        var roots = await GetArrayAsync(instance, $"{ApiPath}/{ArrOptionEndpoints.RootFolder}", cancellationToken);
        var tags = await GetArrayAsync(instance, $"{ApiPath}/{ArrOptionEndpoints.Tag}", cancellationToken);
        var metadataProfiles = Kind == RequestProviderKind.Lidarr
            ? await GetArrayAsync(instance, $"{ApiPath}/{ArrOptionEndpoints.MetadataProfile}", cancellationToken)
            : [];
        return new RequestServiceOptionsResponse(
            profiles.Select(profile => new RequestServiceOption(Text(profile, ArrJsonFields.Id) ?? string.Empty, Text(profile, ArrJsonFields.Name) ?? string.Empty, null))
                .Where(option => !string.IsNullOrWhiteSpace(option.Id))
                .ToArray(),
            roots.Select(root => new RequestServiceOption(Text(root, ArrJsonFields.Path) ?? string.Empty, Text(root, ArrJsonFields.Path) ?? string.Empty, Text(root, ArrJsonFields.Path)))
                .Where(option => !string.IsNullOrWhiteSpace(option.Id))
                .ToArray(),
            metadataProfiles.Select(profile => new RequestServiceOption(Text(profile, ArrJsonFields.Id) ?? string.Empty, Text(profile, ArrJsonFields.Name) ?? string.Empty, null))
                .Where(option => !string.IsNullOrWhiteSpace(option.Id))
                .ToArray(),
            tags.Select(tag => new RequestServiceOption(Text(tag, ArrJsonFields.Id) ?? string.Empty, Text(tag, ArrJsonFields.Label) ?? string.Empty, null))
                .Where(option => !string.IsNullOrWhiteSpace(option.Id) && !string.IsNullOrWhiteSpace(option.Name))
                .ToArray());
    }

    /// <summary>Fetches JSON from an external metadata service (MusicBrainz) with the required User-Agent.</summary>
    protected async Task<JsonElement> GetExternalJsonAsync(Uri url, CancellationToken cancellationToken) {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", MusicBrainzProtocol.UserAgent);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
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
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content)) {
            return default;
        }

        using var document = JsonDocument.Parse(content);
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
        new(true, Text(response, ArrJsonFields.Id), null);

    /// <summary>Applies the service's default Arr tag ids to an add payload, merging with any tags the lookup resource already carries.</summary>
    protected static void ApplyDefaultTags(JsonObject payload, RequestServiceInstanceDetail instance) {
        if (instance.DefaultTagIds.Count == 0) {
            return;
        }

        var existing = payload[ArrJsonFields.Tags] is JsonArray current
            ? current.Select(node => node?.GetValue<int>() ?? 0).Where(value => value > 0).ToHashSet()
            : [];
        existing.UnionWith(instance.DefaultTagIds);
        payload[ArrJsonFields.Tags] = new JsonArray(existing.Order().Select(value => (JsonNode)value).ToArray());
    }

    protected static RequestSearchResult MapMovie(Guid serviceId, JsonElement item) =>
        new(serviceId, RequestProviderKind.Radarr, RequestMediaKind.Movie, Text(item, RadarrProtocol.TmdbId) ?? string.Empty,
            Text(item, ArrJsonFields.Title) ?? string.Empty, Text(item, ArrJsonFields.Studio), Int(item, ArrJsonFields.Year), Text(item, ArrJsonFields.Overview), Image(item, ArrImageTypes.Poster),
            Image(item, ArrImageTypes.Fanart) ?? Image(item, ArrImageTypes.Backdrop), Rating(item), Int(item, ArrJsonFields.Runtime), Text(item, ArrJsonFields.Certification),
            null, StringArray(item, ArrJsonFields.Genres), false, true);

    protected static RequestSearchResult MapSeries(Guid serviceId, JsonElement item) =>
        new(serviceId, RequestProviderKind.Sonarr, RequestMediaKind.Series, Text(item, SonarrProtocol.TvdbId) ?? string.Empty,
            Text(item, ArrJsonFields.Title) ?? string.Empty, Text(item, SonarrProtocol.Network), YearFromDate(Text(item, SonarrProtocol.FirstAired)), Text(item, ArrJsonFields.Overview), Image(item, ArrImageTypes.Poster),
            Image(item, ArrImageTypes.Fanart) ?? Image(item, ArrImageTypes.Backdrop), Rating(item), RuntimeFromMinutesArray(item), Text(item, ArrJsonFields.Certification),
            null, StringArray(item, ArrJsonFields.Genres), false, true);

    protected static RequestSearchResult MapArtist(Guid serviceId, JsonElement item) {
        var subtitle = Text(item, ArrJsonFields.Disambiguation);
        return new(serviceId, RequestProviderKind.Lidarr, RequestMediaKind.Artist, Text(item, LidarrProtocol.ForeignArtistId) ?? string.Empty,
            Text(item, LidarrProtocol.ArtistName) ?? Text(item, ArrJsonFields.Name) ?? string.Empty,
            string.IsNullOrWhiteSpace(subtitle) ? Text(item, LidarrProtocol.ArtistType) : subtitle,
            null, Text(item, ArrJsonFields.Overview), Image(item, ArrImageTypes.Poster),
            Image(item, ArrImageTypes.Fanart) ?? Image(item, ArrImageTypes.Banner), Rating(item), null,
            Text(item, LidarrProtocol.ArtistType), null, StringArray(item, ArrJsonFields.Genres), false, true);
    }

    protected static RequestSearchResult MapAlbum(Guid serviceId, JsonElement item) {
        var albumType = Text(item, ArrJsonFields.AlbumType);
        var secondaryTypes = StringArray(item, LidarrProtocol.SecondaryTypes);
        var typeLabel = string.Join(" · ", new[] { albumType }.Concat(secondaryTypes).Where(value => !string.IsNullOrWhiteSpace(value)));
        return new(serviceId, RequestProviderKind.Lidarr, RequestMediaKind.Album, Text(item, LidarrProtocol.ForeignAlbumId) ?? Text(item, ArrJsonFields.Id) ?? string.Empty,
            Text(item, ArrJsonFields.Title) ?? string.Empty,
            Text(Prop(item, LidarrProtocol.Artist), LidarrProtocol.ArtistName),
            YearFromDate(Text(item, LidarrProtocol.ReleaseDate)), Text(item, ArrJsonFields.Overview), Image(item, ArrImageTypes.Cover) ?? Image(item, ArrImageTypes.Poster),
            Image(item, ArrImageTypes.Fanart), Rating(item), AlbumRuntimeMinutes(item),
            string.IsNullOrWhiteSpace(typeLabel) ? null : typeLabel,
            AlbumTrackCount(item), StringArray(item, ArrJsonFields.Genres), false, true);
    }

    protected static RequestDetailResponse DetailFromSearch(RequestSearchResult result, JsonElement item, IReadOnlyList<RequestChildOption> children, IReadOnlyList<RequestTrack>? tracks = null) =>
        new(result.Source, result.Kind, result.ExternalId, result.Title, result.Subtitle, result.Year, result.Overview, result.PosterUrl,
            result.BackdropUrl, result.Rating, result.RuntimeMinutes, result.Certification, tracks?.Count > 0 ? tracks.Count : result.TrackCount, result.Tags,
            StringArray(item, ArrJsonFields.Studios).Concat(StringArray(item, ArrJsonFields.Networks)).ToArray(),
            Credits(item), children, tracks ?? [], EmptyOptions);

    /// <summary>Album runtime from Lidarr's millisecond duration field, rounded to whole minutes.</summary>
    private static int? AlbumRuntimeMinutes(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty(ArrJsonFields.Duration, out var duration) &&
        duration.TryGetInt64(out var milliseconds) && milliseconds > 0
            ? (int)Math.Round(milliseconds / 60000d)
            : null;

    /// <summary>
    /// Track count estimate for an album: the smallest positive release track count, which is
    /// usually the standard edition rather than a deluxe or anniversary reissue.
    /// </summary>
    private static int? AlbumTrackCount(JsonElement item) {
        var counts = Array(item, LidarrProtocol.Releases)
            .Select(release => Int(release, LidarrProtocol.TrackCount) ?? 0)
            .Where(count => count > 0)
            .ToArray();
        return counts.Length > 0 ? counts.Min() : null;
    }

    protected static RequestServiceOptionsResponse EmptyOptions { get; } = new([], [], [], []);

    protected static JsonObject ToObject(JsonElement item) =>
        JsonNode.Parse(item.GetRawText())?.AsObject() ?? [];

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
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : [];

    /// <summary>Reads a nested object property, returning an Undefined element when absent.</summary>
    protected static JsonElement Prop(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    protected static IReadOnlyList<string> StringArray(JsonElement item, string name) =>
        Array(item, name).Select(element => element.ValueKind == JsonValueKind.String ? element.GetString() : Text(element, ArrJsonFields.Name))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

    protected static IReadOnlyList<string> Credits(JsonElement item) =>
        StringArray(item, ArrJsonFields.Actors).Concat(StringArray(item, ArrJsonFields.Crew)).Concat(StringArray(item, ArrJsonFields.Members)).ToArray();

    protected static string? Text(JsonElement item, string name) {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value)) {
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
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    protected static decimal? Rating(JsonElement item) {
        if (!item.TryGetProperty(ArrJsonFields.Ratings, out var ratings)) {
            return null;
        }

        if (ratings.ValueKind == JsonValueKind.Object &&
            ratings.TryGetProperty(ArrJsonFields.Value, out var value) &&
            value.TryGetDecimal(out var rating)) {
            return rating;
        }

        return null;
    }

    protected static string? Image(JsonElement item, string coverType) =>
        Array(item, ArrJsonFields.Images)
            .Where(image => string.Equals(Text(image, ArrJsonFields.CoverType), coverType, StringComparison.OrdinalIgnoreCase))
            .Select(image => Text(image, ArrJsonFields.RemoteUrl) ?? Text(image, ArrJsonFields.Url))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    protected static int? YearFromDate(string? value) =>
        DateTimeOffset.TryParse(value, out var date) ? date.Year : null;

    private static int? RuntimeFromMinutesArray(JsonElement item) =>
        Array(item, ArrJsonFields.Runtime).Select(runtime => runtime.TryGetInt32(out var minutes) ? minutes : 0).FirstOrDefault() is var value && value > 0
            ? value
            : null;
}
