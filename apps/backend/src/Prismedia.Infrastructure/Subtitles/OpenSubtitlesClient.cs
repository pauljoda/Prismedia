using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Prismedia.Application.Subtitles;
using Prismedia.Contracts.Media;

namespace Prismedia.Infrastructure.Subtitles;

internal sealed record OpenSubtitlesConnection(
    string ApiKey,
    string Username,
    string Password,
    bool IncludeAiTranslated,
    bool IncludeMachineTranslated);

internal sealed record OpenSubtitlesSearchContext(
    string Title,
    string FileName,
    string? MovieHash,
    string? ImdbId,
    string? ParentImdbId,
    int? TmdbId,
    int? Year,
    int? SeasonNumber,
    int? EpisodeNumber,
    IReadOnlyList<string> Languages);

internal sealed record OpenSubtitlesDownloadArtifact(
    string FileName,
    string Format,
    string Language,
    string? ReleaseName,
    byte[] Content);

/// <summary>Direct OpenSubtitles.com REST v1 adapter with cached login and bounded redirects.</summary>
internal sealed class OpenSubtitlesClient(HttpClient http) {
    private static readonly TimeSpan CandidateLifetime = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, CachedOpenSubtitlesCandidate> _candidates = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private string? _tokenCredentialFingerprint;
    private Uri _apiBase = new(OpenSubtitlesProtocol.ApiBaseUrl);
    private DateTimeOffset _tokenExpiresAt;

    public async Task<SubtitleProviderTestResult> TestAsync(
        OpenSubtitlesConnection connection,
        CancellationToken cancellationToken) {
        try {
            await EnsureLoggedInAsync(connection, cancellationToken);
            using var request = CreateRequest(
                HttpMethod.Get,
                OpenSubtitlesProtocol.InfosUserPath,
                connection,
                authenticated: true);
            using var response = await http.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            return new SubtitleProviderTestResult(true, "Connected to OpenSubtitles.");
        } catch (OpenSubtitlesException exception) {
            return new SubtitleProviderTestResult(false, exception.Message);
        }
    }

    public async Task<IReadOnlyList<SubtitleSearchResult>> SearchAsync(
        OpenSubtitlesConnection connection,
        OpenSubtitlesSearchContext context,
        CancellationToken cancellationToken) {
        var query = BuildSearchQuery(context, connection);
        using var request = CreateRequest(
            HttpMethod.Get,
            OpenSubtitlesProtocol.SubtitlesPath + "?" + query,
            connection);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<OpenSubtitlesSearchResponse>(Json, cancellationToken)
            ?? throw new OpenSubtitlesException("OpenSubtitles returned an empty search response.");

        PurgeExpiredCandidates();
        return payload.Data
            .SelectMany(item => MapCandidates(item, context))
            .OrderByDescending(candidate => candidate.MatchConfidence)
            .ThenByDescending(candidate => candidate.QualityScore)
            .ThenByDescending(candidate => candidate.DownloadCount)
            .ToArray();
    }

    public async Task<OpenSubtitlesDownloadArtifact> DownloadAsync(
        OpenSubtitlesConnection connection,
        string candidateId,
        CancellationToken cancellationToken) {
        if (!_candidates.TryGetValue(candidateId, out var candidate) ||
            candidate.ExpiresAt <= DateTimeOffset.UtcNow) {
            _candidates.TryRemove(candidateId, out _);
            throw new OpenSubtitlesException("The selected OpenSubtitles candidate expired. Search again.");
        }

        await _gate.WaitAsync(cancellationToken);
        try {
            await EnsureLoggedInCoreAsync(connection, cancellationToken);
            using var request = CreateRequest(HttpMethod.Post, OpenSubtitlesProtocol.DownloadPath, connection, authenticated: true);
            request.Content = JsonContent.Create(new OpenSubtitlesDownloadRequest(
                candidate.FileId,
                OpenSubtitlesProtocol.OutputFormat), options: Json);
            using var response = await http.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var download = await response.Content.ReadFromJsonAsync<OpenSubtitlesDownloadResponse>(Json, cancellationToken)
                ?? throw new OpenSubtitlesException("OpenSubtitles returned an empty download response.");
            if (!Uri.TryCreate(download.Link, UriKind.Absolute, out var link) || link.Scheme != Uri.UriSchemeHttps) {
                throw new OpenSubtitlesException("OpenSubtitles returned an unsafe download link.");
            }

            using var fileRequest = new HttpRequestMessage(HttpMethod.Get, link);
            using var fileResponse = await http.SendAsync(fileRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await EnsureSuccessAsync(fileResponse, cancellationToken);
            if (fileResponse.Content.Headers.ContentLength is > 20 * 1024 * 1024) {
                throw new OpenSubtitlesException("The subtitle download exceeded the 20 MB safety limit.");
            }

            var content = await fileResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            if (content.Length == 0 || content.Length > 20 * 1024 * 1024) {
                throw new OpenSubtitlesException("OpenSubtitles returned an empty or oversized subtitle file.");
            }

            var fileName = string.IsNullOrWhiteSpace(download.FileName)
                ? $"opensubtitles-{candidate.FileId.ToString(CultureInfo.InvariantCulture)}.srt"
                : Path.GetFileName(download.FileName);
            return new OpenSubtitlesDownloadArtifact(
                fileName,
                SubtitleFormats.Srt,
                candidate.Language,
                candidate.ReleaseName,
                content);
        } finally {
            _gate.Release();
        }
    }

    private async Task EnsureLoggedInAsync(OpenSubtitlesConnection connection, CancellationToken cancellationToken) {
        await _gate.WaitAsync(cancellationToken);
        try {
            await EnsureLoggedInCoreAsync(connection, cancellationToken);
        } finally {
            _gate.Release();
        }
    }

    private async Task EnsureLoggedInCoreAsync(OpenSubtitlesConnection connection, CancellationToken cancellationToken) {
        var credentialFingerprint = CredentialFingerprint(connection);
        if (!string.IsNullOrWhiteSpace(_token) &&
            string.Equals(_tokenCredentialFingerprint, credentialFingerprint, StringComparison.Ordinal) &&
            _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) {
            return;
        }

        using var request = CreateRequest(HttpMethod.Post, OpenSubtitlesProtocol.LoginPath, connection);
        request.Content = JsonContent.Create(
            new OpenSubtitlesLoginRequest(connection.Username, connection.Password), options: Json);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var login = await response.Content.ReadFromJsonAsync<OpenSubtitlesLoginResponse>(Json, cancellationToken)
            ?? throw new OpenSubtitlesException("OpenSubtitles returned an empty login response.");
        if (string.IsNullOrWhiteSpace(login.Token)) {
            throw new OpenSubtitlesException("OpenSubtitles did not return an authentication token.");
        }

        _token = login.Token;
        _tokenCredentialFingerprint = credentialFingerprint;
        _tokenExpiresAt = DateTimeOffset.UtcNow.AddHours(11);
        if (Uri.TryCreate(login.BaseUrl, UriKind.Absolute, out var baseUri) && OpenSubtitlesProtocol.IsAllowedApiBase(baseUri)) {
            _apiBase = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/api/v1/");
        }
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string relativePath,
        OpenSubtitlesConnection connection,
        bool authenticated = false) {
        var request = new HttpRequestMessage(method, new Uri(_apiBase, relativePath));
        request.Headers.TryAddWithoutValidation(OpenSubtitlesProtocol.ApiKeyHeader, connection.ApiKey);
        request.Headers.TryAddWithoutValidation(OpenSubtitlesProtocol.UserAgentHeader, OpenSubtitlesProtocol.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (authenticated && !string.IsNullOrWhiteSpace(_token)) {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                OpenSubtitlesProtocol.AuthorizationScheme,
                _token);
        }
        return request;
    }

    private static string BuildSearchQuery(
        OpenSubtitlesSearchContext context,
        OpenSubtitlesConnection connection) {
        var values = new List<KeyValuePair<string, string>> {
            new("languages", string.Join(',', context.Languages)),
            new("query", context.FileName),
            new("ai_translated", connection.IncludeAiTranslated ? "include" : "exclude"),
            new("machine_translated", connection.IncludeMachineTranslated ? "include" : "exclude"),
        };
        Add(values, "moviehash", context.MovieHash);
        Add(values, "imdb_id", NormalizeImdbId(context.ImdbId));
        Add(values, "parent_imdb_id", NormalizeImdbId(context.ParentImdbId));
        Add(values, "tmdb_id", context.TmdbId);
        Add(values, "season_number", context.SeasonNumber);
        Add(values, "episode_number", context.EpisodeNumber);
        return string.Join('&', values
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => $"{Uri.EscapeDataString(value.Key)}={Uri.EscapeDataString(value.Value)}"));
    }

    private IEnumerable<SubtitleSearchResult> MapCandidates(
        OpenSubtitlesSearchItem item,
        OpenSubtitlesSearchContext context) {
        var attributes = item.Attributes;
        foreach (var file in attributes.Files) {
            // OpenSubtitles' v1 download contract only guarantees its requested output
            // format. Do not advertise an upload's original extension when the acquired
            // artifact is normalized to SRT.
            const string format = SubtitleFormats.Srt;

            var details = attributes.FeatureDetails;
            var externalIdMatched = MatchesExternalId(details, context);
            var episodeMatched = context.SeasonNumber is not null && context.EpisodeNumber is not null &&
                details?.SeasonNumber == context.SeasonNumber && details?.EpisodeNumber == context.EpisodeNumber;
            var yearMatched = context.Year is not null && details?.Year == context.Year;
            var releaseMatched = ReleaseLooksRelated(attributes.Release, context.FileName, context.Title);
            var assessment = SubtitleMatchPolicy.Assess(new SubtitleMatchEvidence(
                attributes.MovieHashMatch,
                externalIdMatched,
                episodeMatched,
                yearMatched,
                releaseMatched,
                attributes.Uploader?.Trusted == true,
                attributes.AiTranslated,
                attributes.MachineTranslated,
                attributes.Ratings,
                IdentityConflict: HasIdentityConflict(details, context),
                MultiFile: attributes.Files.Count > 1));
            var reasons = assessment.AutomaticEligible
                ? assessment.Reasons
                : assessment.Reasons.Concat(["Manual review"]).Distinct().ToArray();

            var candidateId = BuildCandidateId(item.Id, file.FileId);
            _candidates[candidateId] = new CachedOpenSubtitlesCandidate(
                file.FileId,
                attributes.Language,
                attributes.Release,
                DateTimeOffset.UtcNow.Add(CandidateLifetime));
            yield return new SubtitleSearchResult(
                SubtitleProviderCodes.OpenSubtitles,
                candidateId,
                attributes.Language,
                attributes.Release,
                format,
                attributes.HearingImpaired,
                attributes.ForeignPartsOnly,
                attributes.AiTranslated,
                attributes.MachineTranslated,
                attributes.MovieHashMatch,
                attributes.DownloadCount,
                attributes.Ratings,
                assessment.MatchConfidence,
                assessment.QualityScore,
                assessment.AutomaticEligible,
                reasons,
                attributes.Url);
        }
    }

    private static bool MatchesExternalId(OpenSubtitlesFeatureDetails? details, OpenSubtitlesSearchContext context) {
        if (details is null) return false;
        var imdb = NormalizeImdbId(context.ImdbId);
        var parentImdb = NormalizeImdbId(context.ParentImdbId);
        return imdb is not null && string.Equals(details.ImdbId?.ToString(CultureInfo.InvariantCulture), imdb, StringComparison.Ordinal) ||
            parentImdb is not null && string.Equals(details.ParentImdbId?.ToString(CultureInfo.InvariantCulture), parentImdb, StringComparison.Ordinal) ||
            context.TmdbId is not null && details.TmdbId == context.TmdbId;
    }

    private static bool HasIdentityConflict(OpenSubtitlesFeatureDetails? details, OpenSubtitlesSearchContext context) {
        if (details is null) return false;
        if (context.SeasonNumber is not null && details.SeasonNumber is not null && context.SeasonNumber != details.SeasonNumber) return true;
        if (context.EpisodeNumber is not null && details.EpisodeNumber is not null && context.EpisodeNumber != details.EpisodeNumber) return true;
        return context.Year is not null && details.Year is not null && context.Year != details.Year;
    }

    private static bool ReleaseLooksRelated(string? release, string fileName, string title) {
        if (string.IsNullOrWhiteSpace(release)) return false;
        var normalizedRelease = NormalizeTitle(release);
        var normalizedFile = NormalizeTitle(Path.GetFileNameWithoutExtension(fileName));
        var normalizedTitle = NormalizeTitle(title);
        return normalizedTitle.Length >= 3 && normalizedRelease.Contains(normalizedTitle, StringComparison.Ordinal) ||
            normalizedFile.Length >= 3 &&
            (normalizedRelease.Contains(normalizedFile, StringComparison.Ordinal) || normalizedFile.Contains(normalizedRelease, StringComparison.Ordinal));
    }

    private static string NormalizeTitle(string value) =>
        new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string BuildCandidateId(string subtitleId, long fileId) =>
        $"{subtitleId}:{fileId.ToString(CultureInfo.InvariantCulture)}";

    private void PurgeExpiredCandidates() {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _candidates) {
            if (pair.Value.ExpiresAt <= now) {
                _candidates.TryRemove(pair.Key, out _);
            }
        }
    }

    private static string? NormalizeImdbId(string? value) {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (normalized.StartsWith("tt", StringComparison.OrdinalIgnoreCase)) normalized = normalized[2..];
        normalized = normalized.TrimStart('0');
        return normalized.Length == 0 ? "0" : normalized;
    }

    private static string CredentialFingerprint(OpenSubtitlesConnection connection) {
        var bytes = Encoding.UTF8.GetBytes(
            $"{connection.ApiKey}\u001f{connection.Username}\u001f{connection.Password}");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static void Add(ICollection<KeyValuePair<string, string>> values, string key, object? value) {
        if (value is not null && !string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture))) {
            values.Add(new KeyValuePair<string, string>(key, Convert.ToString(value, CultureInfo.InvariantCulture)!));
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
        if (response.IsSuccessStatusCode) return;
        var providerMessage = await TryReadProviderMessageAsync(response, cancellationToken);
        var message = response.StatusCode switch {
            HttpStatusCode.Unauthorized => "OpenSubtitles rejected the configured username, password, or token.",
            HttpStatusCode.Forbidden => "OpenSubtitles rejected the configured API key.",
            HttpStatusCode.NotAcceptable => "The OpenSubtitles daily download quota is exhausted.",
            HttpStatusCode.Gone => "The OpenSubtitles download link expired.",
            HttpStatusCode.TooManyRequests => "OpenSubtitles is rate limiting requests. Try again after its reset window.",
            _ when (int)response.StatusCode >= 500 => "OpenSubtitles is temporarily unavailable.",
            _ => providerMessage ?? $"OpenSubtitles request failed ({(int)response.StatusCode}).",
        };
        throw new OpenSubtitlesException(message, response.StatusCode);
    }

    private static async Task<string?> TryReadProviderMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken) {
        try {
            var error = await response.Content.ReadFromJsonAsync<OpenSubtitlesErrorResponse>(Json, cancellationToken);
            return string.IsNullOrWhiteSpace(error?.Message) ? null : error.Message;
        } catch (JsonException) {
            return null;
        }
    }

    private sealed record OpenSubtitlesLoginRequest(string Username, string Password);
    private sealed record OpenSubtitlesLoginResponse(string Token, [property: JsonPropertyName("base_url")] string? BaseUrl);
    private sealed record OpenSubtitlesDownloadRequest(
        [property: JsonPropertyName("file_id")] long FileId,
        [property: JsonPropertyName("sub_format")] string SubFormat);
    private sealed record OpenSubtitlesDownloadResponse(string Link, [property: JsonPropertyName("file_name")] string? FileName);
    private sealed record OpenSubtitlesErrorResponse(string? Message);
    private sealed record OpenSubtitlesSearchResponse(IReadOnlyList<OpenSubtitlesSearchItem> Data);
    private sealed record OpenSubtitlesSearchItem(string Id, OpenSubtitlesAttributes Attributes);
    private sealed record OpenSubtitlesAttributes(
        string Language,
        string? Release,
        string? Url,
        [property: JsonPropertyName("hearing_impaired")] bool HearingImpaired,
        [property: JsonPropertyName("foreign_parts_only")] bool ForeignPartsOnly,
        [property: JsonPropertyName("ai_translated")] bool AiTranslated,
        [property: JsonPropertyName("machine_translated")] bool MachineTranslated,
        [property: JsonPropertyName("moviehash_match")] bool MovieHashMatch,
        [property: JsonPropertyName("download_count")] int DownloadCount,
        decimal? Ratings,
        OpenSubtitlesUploader? Uploader,
        [property: JsonPropertyName("feature_details")] OpenSubtitlesFeatureDetails? FeatureDetails,
        IReadOnlyList<OpenSubtitlesFile> Files);
    private sealed record OpenSubtitlesUploader(bool Trusted);
    private sealed record OpenSubtitlesFeatureDetails(
        int? Year,
        [property: JsonPropertyName("imdb_id")] int? ImdbId,
        [property: JsonPropertyName("parent_imdb_id")] int? ParentImdbId,
        [property: JsonPropertyName("tmdb_id")] int? TmdbId,
        [property: JsonPropertyName("season_number")] int? SeasonNumber,
        [property: JsonPropertyName("episode_number")] int? EpisodeNumber);
    private sealed record OpenSubtitlesFile(
        [property: JsonPropertyName("file_id")] long FileId,
        [property: JsonPropertyName("file_name")] string FileName);
    private sealed record CachedOpenSubtitlesCandidate(
        long FileId,
        string Language,
        string? ReleaseName,
        DateTimeOffset ExpiresAt);
}

internal sealed class OpenSubtitlesException : Exception {
    public OpenSubtitlesException(string message, HttpStatusCode? statusCode = null) : base(message) {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
