using Prismedia.Application.Updates;
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
