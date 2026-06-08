using System.Reflection;
using System.Text.Json;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

internal static class UpdateCheckEndpoints {
    internal static IEndpointRouteBuilder MapUpdateCheckEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapGet("/api/update-check", async Task<Microsoft.AspNetCore.Http.HttpResults.Ok<UpdateCheckResponse>> (
                string? force,
                IUpdateCheckService updateCheck,
                CancellationToken cancellationToken) =>
            TypedResults.Ok(await updateCheck.CheckAsync(IsForceRequested(force), cancellationToken)))
            .WithName("GetUpdateCheck")
            .WithTags("System")
            .WithSummary("Returns a non-blocking update-check status for the Svelte shell.");

        routes.MapGet("/api/changelog", async (
                IConfiguration configuration,
                IWebHostEnvironment environment,
                CancellationToken cancellationToken) => {
            var path = ResolveChangelogPath(configuration, environment.ContentRootPath);
            if (path is null) {
                return Results.NotFound(new ApiProblem(
                    ApiProblemCodes.ChangelogNotFound,
                    "The Prismedia changelog could not be found on this host."));
            }

            var content = await File.ReadAllTextAsync(path, cancellationToken);
            return Results.Text(content, "text/markdown; charset=utf-8");
        })
            .WithName("GetChangelog")
            .WithTags("System")
            .WithSummary("Returns the bundled Prismedia changelog markdown.");

        return routes;
    }

    private static bool IsForceRequested(string? force) =>
        string.Equals(force, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(force, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(force, "yes", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveChangelogPath(IConfiguration configuration, string contentRootPath) {
        var configured = configuration["CHANGELOG_PATH"] ??
            configuration["Prismedia:ChangelogPath"];
        foreach (var candidate in ResolveConfiguredCandidates(configured, contentRootPath)) {
            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        var directory = new DirectoryInfo(contentRootPath);
        while (directory is not null) {
            var candidate = Path.Combine(directory.FullName, "CHANGELOG.md");
            if (File.Exists(candidate)) {
                return candidate;
            }

            directory = directory.Parent;
        }

        var dockerCandidate = "/app/CHANGELOG.md";
        return File.Exists(dockerCandidate) ? dockerCandidate : null;
    }

    private static IEnumerable<string> ResolveConfiguredCandidates(string? configured, string contentRootPath) {
        if (string.IsNullOrWhiteSpace(configured)) {
            yield break;
        }

        if (Path.IsPathRooted(configured)) {
            yield return configured;
            yield break;
        }

        yield return Path.GetFullPath(Path.Combine(contentRootPath, configured));
        yield return Path.GetFullPath(configured);
    }
}

internal interface IUpdateCheckService {
    Task<UpdateCheckResponse> CheckAsync(bool force, CancellationToken cancellationToken);
}

/// <summary>
/// Resolves the running build's release channel and asks the GitHub Container Registry whether a
/// newer image has been published on that same channel.
/// </summary>
/// <remarks>
/// Versioned channels (alpha/beta/release) are compared by the newest <c>{channel}-X.Y.Z</c> tag;
/// the <c>dev</c> channel keeps a single version per cycle, so it is compared by image digest
/// instead. Local builds without a baked commit short-circuit to a non-networked development status.
/// </remarks>
internal sealed class GhcrUpdateCheckService : IUpdateCheckService {
    internal const string HttpClientName = "PrismediaUpdateCheck";

    private const string ManifestAcceptHeader =
        "application/vnd.oci.image.index.v1+json," +
        "application/vnd.docker.distribution.manifest.list.v2+json," +
        "application/vnd.docker.distribution.manifest.v2+json," +
        "application/vnd.oci.image.manifest.v1+json";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GhcrUpdateCheckService> _logger;
    private readonly object _cacheGate = new();
    private UpdateCheckResponse? _cached;

    public GhcrUpdateCheckService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<GhcrUpdateCheckService> logger) {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task<UpdateCheckResponse> CheckAsync(bool force, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        if (!force) {
            lock (_cacheGate) {
                if (_cached is not null && now - _cached.CheckedAt < CacheDuration) {
                    return _cached with { FromCache = true };
                }
            }
        }

        var result = await FetchChannelStatusAsync(now, cancellationToken);
        lock (_cacheGate) {
            _cached = result with { FromCache = false };
        }

        return result;
    }

    private async Task<UpdateCheckResponse> FetchChannelStatusAsync(
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken) {
        var channel = ResolveChannel(_configuration);
        var localVersion = ResolveCurrentVersion(_configuration, _environment.ContentRootPath);
        var commit = ResolveCommit(_configuration);
        var host = ResolveRegistryHost(_configuration);
        var repository = ResolveImageRepository(_configuration);
        var pageUrl = ResolvePageUrl(_configuration);

        // A locally built/run host has no published image to compare against. Report a calm
        // development status instead of hammering the registry or showing a false update.
        if (string.IsNullOrWhiteSpace(commit) ||
            string.Equals(commit, "unknown", StringComparison.OrdinalIgnoreCase)) {
            return new UpdateCheckResponse(
                "development", channel, localVersion, null, null, false, checkedAt, false, null);
        }

        try {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var token = await GetPullTokenAsync(client, host, repository, cancellationToken);
            if (token is null) {
                return Unknown(channel, localVersion, checkedAt, "Could not authenticate with the container registry.");
            }

            return channel == "dev"
                ? await CheckDevByDigestAsync(client, host, repository, channel, localVersion, commit, token, pageUrl, checkedAt, cancellationToken)
                : await CheckVersionedChannelAsync(client, host, repository, channel, localVersion, token, pageUrl, checkedAt, cancellationToken);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return Unknown(channel, localVersion, checkedAt, "The container registry update check timed out.");
        } catch (HttpRequestException ex) {
            _logger.LogDebug(ex, "Prismedia update check request failed.");
            return Unknown(channel, localVersion, checkedAt, "The container registry could not be reached.");
        } catch (JsonException ex) {
            _logger.LogDebug(ex, "Prismedia update check response could not be parsed.");
            return Unknown(channel, localVersion, checkedAt, "The container registry response could not be parsed.");
        }
    }

    private async Task<UpdateCheckResponse> CheckVersionedChannelAsync(
        HttpClient client,
        string host,
        string repository,
        string channel,
        string localVersion,
        string token,
        string pageUrl,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken) {
        var tags = await ListTagsAsync(client, host, repository, token, cancellationToken);
        var prefix = channel + "-";
        string? latestVersion = null;
        foreach (var tag in tags) {
            if (!tag.StartsWith(prefix, StringComparison.Ordinal)) {
                continue;
            }

            var candidate = tag[prefix.Length..];
            if (TryParseVersion(candidate) is null) {
                continue;
            }

            if (latestVersion is null || CompareVersions(candidate, latestVersion) > 0) {
                latestVersion = candidate;
            }
        }

        if (latestVersion is null) {
            return Unknown(channel, localVersion, checkedAt, $"No {channel} images have been published yet.");
        }

        var comparison = CompareVersions(latestVersion, localVersion);
        if (comparison is null) {
            return Unknown(
                channel, localVersion, checkedAt,
                $"Could not compare local version {localVersion} with latest {channel} image {latestVersion}.",
                latestVersion, pageUrl);
        }

        var updateAvailable = comparison.Value > 0;
        return new UpdateCheckResponse(
            updateAvailable ? "available" : "current",
            channel, localVersion, latestVersion, pageUrl, updateAvailable, checkedAt, false, null);
    }

    private async Task<UpdateCheckResponse> CheckDevByDigestAsync(
        HttpClient client,
        string host,
        string repository,
        string channel,
        string localVersion,
        string commit,
        string token,
        string pageUrl,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken) {
        var channelDigest = await GetManifestDigestAsync(client, host, repository, channel, token, cancellationToken);
        var selfDigest = await GetManifestDigestAsync(client, host, repository, $"{localVersion}-{commit}", token, cancellationToken);
        if (channelDigest is null || selfDigest is null) {
            return Unknown(channel, localVersion, checkedAt, "Could not resolve the current dev image digest.");
        }

        var updateAvailable = !string.Equals(channelDigest, selfDigest, StringComparison.Ordinal);
        return new UpdateCheckResponse(
            updateAvailable ? "available" : "current",
            channel, localVersion, null, updateAvailable ? pageUrl : null, updateAvailable, checkedAt, false, null);
    }

    private static async Task<string?> GetPullTokenAsync(
        HttpClient client,
        string host,
        string repository,
        CancellationToken cancellationToken) {
        var uri = $"https://{host}/token?scope=repository:{repository}:pull&service={host}";
        using var response = await client.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode) {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("token", out var token) ? token.GetString() : null;
    }

    private static async Task<string?> GetManifestDigestAsync(
        HttpClient client,
        string host,
        string repository,
        string tag,
        string token,
        CancellationToken cancellationToken) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{host}/v2/{repository}/manifests/{tag}");
        request.Headers.TryAddWithoutValidation("Accept", ManifestAcceptHeader);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) {
            return null;
        }

        return response.Headers.TryGetValues("Docker-Content-Digest", out var values)
            ? values.FirstOrDefault()
            : null;
    }

    private static async Task<IReadOnlyList<string>> ListTagsAsync(
        HttpClient client,
        string host,
        string repository,
        string token,
        CancellationToken cancellationToken) {
        var tags = new List<string>();
        var next = $"/v2/{repository}/tags/list?n=200";
        // Follow registry Link pagination, but cap iterations so a misbehaving registry can't loop.
        for (var page = 0; next is not null && page < 25; page++) {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{host}{next}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) {
                break;
            }

            await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken)) {
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (document.RootElement.TryGetProperty("tags", out var tagArray) &&
                    tagArray.ValueKind == JsonValueKind.Array) {
                    foreach (var tag in tagArray.EnumerateArray()) {
                        var value = tag.GetString();
                        if (!string.IsNullOrWhiteSpace(value)) {
                            tags.Add(value);
                        }
                    }
                }
            }

            next = ParseNextLink(response);
        }

        return tags;
    }

    private static string? ParseNextLink(HttpResponseMessage response) {
        if (!response.Headers.TryGetValues("Link", out var links)) {
            return null;
        }

        foreach (var link in links) {
            // Format: </v2/owner/repo/tags/list?last=foo&n=200>; rel="next"
            if (!link.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var start = link.IndexOf('<');
            var end = link.IndexOf('>');
            if (start >= 0 && end > start) {
                return link[(start + 1)..end];
            }
        }

        return null;
    }

    private static UpdateCheckResponse Unknown(
        string channel,
        string localVersion,
        DateTimeOffset checkedAt,
        string error,
        string? latestVersion = null,
        string? latestUrl = null) =>
        new(
            "unknown",
            channel,
            localVersion,
            latestVersion,
            latestUrl,
            false,
            checkedAt,
            false,
            error);

    private static string ResolveChannel(IConfiguration configuration) {
        var channel = configuration["PRISMEDIA_CHANNEL"] ?? configuration["Prismedia:Channel"];
        return string.IsNullOrWhiteSpace(channel) ? "dev" : channel.Trim().ToLowerInvariant();
    }

    private static string? ResolveCommit(IConfiguration configuration) =>
        configuration["PRISMEDIA_COMMIT"] ?? configuration["Prismedia:Commit"];

    private static string ResolveRegistryHost(IConfiguration configuration) =>
        configuration["PRISMEDIA_GHCR_HOST"] ??
        configuration["Prismedia:UpdateCheck:RegistryHost"] ??
        "ghcr.io";

    private static string ResolveImageRepository(IConfiguration configuration) =>
        configuration["PRISMEDIA_IMAGE_REPOSITORY"] ??
        configuration["Prismedia:UpdateCheck:ImageRepository"] ??
        "pauljoda/prismedia";

    private static string ResolvePageUrl(IConfiguration configuration) =>
        configuration["PRISMEDIA_UPDATE_PAGE_URL"] ??
        configuration["Prismedia:UpdateCheck:PageUrl"] ??
        "https://github.com/pauljoda/Prismedia/pkgs/container/prismedia";

    private static string ResolveCurrentVersion(IConfiguration configuration, string contentRootPath) {
        var configured = configuration["PRISMEDIA_VERSION"] ??
            configuration["Prismedia:Version"];
        if (!string.IsNullOrWhiteSpace(configured)) {
            return NormalizeVersionLabel(configured) ?? configured;
        }

        foreach (var candidate in ResolvePackageJsonCandidates(contentRootPath)) {
            if (!File.Exists(candidate)) {
                continue;
            }

            try {
                using var document = JsonDocument.Parse(File.ReadAllText(candidate));
                if (document.RootElement.TryGetProperty("version", out var version)) {
                    var value = version.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) {
                        return NormalizeVersionLabel(value) ?? value;
                    }
                }
            } catch (JsonException) {
                // Ignore malformed package metadata and fall back to the assembly.
            } catch (IOException) {
                // Ignore unreadable package metadata and fall back to the assembly.
            }
        }

        return Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    }

    private static IEnumerable<string> ResolvePackageJsonCandidates(string contentRootPath) {
        var directory = new DirectoryInfo(contentRootPath);
        while (directory is not null) {
            yield return Path.Combine(directory.FullName, "package.json");
            directory = directory.Parent;
        }

        yield return "/app/package.json";
    }

    private static string? NormalizeVersionLabel(string? version) {
        if (string.IsNullOrWhiteSpace(version)) {
            return null;
        }

        return version.Trim().TrimStart('v', 'V');
    }

    internal static int? CompareVersions(string latestVersion, string localVersion) {
        var latest = TryParseVersion(latestVersion);
        var local = TryParseVersion(localVersion);
        if (latest is null || local is null) {
            return null;
        }

        var majorDifference = latest.Value.Major.CompareTo(local.Value.Major);
        if (majorDifference != 0) {
            return majorDifference;
        }

        var minorDifference = latest.Value.Minor.CompareTo(local.Value.Minor);
        if (minorDifference != 0) {
            return minorDifference;
        }

        return latest.Value.Patch.CompareTo(local.Value.Patch);
    }

    private static (int Major, int Minor, int Patch)? TryParseVersion(string version) {
        var normalized = NormalizeVersionLabel(version)?.Split(['-', '+'], 2)[0];
        if (string.IsNullOrWhiteSpace(normalized)) {
            return null;
        }

        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) {
            return null;
        }

        if (!int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor)) {
            return null;
        }

        var patch = 0;
        if (parts.Length >= 3 && !int.TryParse(parts[2], out patch)) {
            return null;
        }

        return (major, minor, patch);
    }
}
