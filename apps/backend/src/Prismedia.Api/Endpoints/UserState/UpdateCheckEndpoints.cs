using System.Reflection;

namespace Prismedia.Api.Endpoints;

internal static class UpdateCheckEndpoints {
    internal static IEndpointRouteBuilder MapUpdateCheckEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapGet("/api/update-check", () =>
            Results.Ok(new {
                status = "unknown",
                localVersion = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
                latestVersion = (string?)null,
                latestUrl = (string?)null,
                updateAvailable = false,
                checkedAt = DateTimeOffset.UtcNow,
                fromCache = false,
                error = "Update checks are not available in the .NET API host yet."
            }))
            .WithName("GetUpdateCheck")
            .WithTags("User State")
            .WithSummary("Returns a non-blocking update-check status for the Svelte shell.");

        return routes;
    }
}
