using Prismedia.Application.Settings;
using Prismedia.Contracts.Settings;

namespace Prismedia.Api.Endpoints;

public static class SettingsEndpoints {
    public static RouteGroupBuilder MapSettingsEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/settings")
            .WithTags("Settings");

        group.MapGet("/", (
            SettingsService settings,
            CancellationToken cancellationToken) =>
            settings.GetAsync(cancellationToken))
            .WithName("GetSettings")
            .WithSummary("Gets application settings.");

        group.MapPatch("/", (
            SettingsUpdateRequest request,
            SettingsService settings,
            CancellationToken cancellationToken) =>
            settings.UpdateAsync(request, cancellationToken))
            .WithName("UpdateSettings")
            .WithSummary("Updates application settings.");

        group.MapGet("/library", (
            SettingsService settings,
            CancellationToken cancellationToken) =>
            settings.GetLibraryConfigAsync(cancellationToken))
            .WithName("GetLibraryConfig")
            .WithSummary("Gets settings and watched roots for the migrated settings page.");

        group.MapPut("/library", (
            LibrarySettingsUpdateRequest request,
            SettingsService settings,
            CancellationToken cancellationToken) =>
            settings.UpdateLibrarySettingsAsync(request, cancellationToken))
            .WithName("UpdateLibrarySettings")
            .WithSummary("Updates settings from the migrated settings page.");

        return group;
    }
}
