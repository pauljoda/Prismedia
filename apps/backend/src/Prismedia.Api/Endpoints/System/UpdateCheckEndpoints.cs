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
                    "changelog_not_found",
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

internal sealed class GitHubReleaseUpdateCheckService : IUpdateCheckService {
    internal const string HttpClientName = "PrismediaUpdateCheck";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubReleaseUpdateCheckService> _logger;
    private readonly object _cacheGate = new();
    private UpdateCheckResponse? _cached;

    public GitHubReleaseUpdateCheckService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<GitHubReleaseUpdateCheckService> logger) {
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

        var result = await FetchLatestReleaseAsync(now, cancellationToken);
        lock (_cacheGate) {
            _cached = result with { FromCache = false };
        }

        return result;
    }

    private async Task<UpdateCheckResponse> FetchLatestReleaseAsync(
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken) {
        var localVersion = ResolveCurrentVersion(_configuration, _environment.ContentRootPath);
        var repository = ResolveRepository(_configuration);
        var requestUri = ResolveReleaseApiUri(_configuration, repository);

        try {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) {
                return Unknown(
                    localVersion,
                    checkedAt,
                    $"No published GitHub release was found for {repository}.");
            }

            if (!response.IsSuccessStatusCode) {
                return Unknown(
                    localVersion,
                    checkedAt,
                    $"GitHub release check failed with HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var tagName = root.TryGetProperty("tag_name", out var tagProperty)
                ? tagProperty.GetString()
                : null;
            var latestUrl = root.TryGetProperty("html_url", out var urlProperty)
                ? urlProperty.GetString()
                : null;
            var latestVersion = NormalizeVersionLabel(tagName);

            if (string.IsNullOrWhiteSpace(latestVersion)) {
                return Unknown(localVersion, checkedAt, "GitHub release response did not include a tag name.");
            }

            var comparison = CompareVersions(latestVersion, localVersion);
            if (comparison is null) {
                return Unknown(
                    localVersion,
                    checkedAt,
                    $"Could not compare local version {localVersion} with latest release {latestVersion}.",
                    latestVersion,
                    latestUrl);
            }

            var updateAvailable = comparison.Value > 0;
            return new UpdateCheckResponse(
                updateAvailable ? "available" : "current",
                localVersion,
                latestVersion,
                latestUrl,
                updateAvailable,
                checkedAt,
                false,
                null);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return Unknown(localVersion, checkedAt, "GitHub release check timed out.");
        } catch (HttpRequestException ex) {
            _logger.LogDebug(ex, "Prismedia update check request failed.");
            return Unknown(localVersion, checkedAt, "GitHub release check could not be reached.");
        } catch (JsonException ex) {
            _logger.LogDebug(ex, "Prismedia update check response could not be parsed.");
            return Unknown(localVersion, checkedAt, "GitHub release response could not be parsed.");
        }
    }

    private static UpdateCheckResponse Unknown(
        string localVersion,
        DateTimeOffset checkedAt,
        string error,
        string? latestVersion = null,
        string? latestUrl = null) =>
        new(
            "unknown",
            localVersion,
            latestVersion,
            latestUrl,
            false,
            checkedAt,
            false,
            error);

    private static string ResolveRepository(IConfiguration configuration) =>
        configuration["PRISMEDIA_UPDATE_REPOSITORY"] ??
        configuration["Prismedia:UpdateCheck:Repository"] ??
        "pauljoda/Prismedia";

    private static string ResolveReleaseApiUri(IConfiguration configuration, string repository) =>
        configuration["PRISMEDIA_UPDATE_CHECK_URL"] ??
        configuration["Prismedia:UpdateCheck:Url"] ??
        $"https://api.github.com/repos/{repository}/releases/latest";

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
