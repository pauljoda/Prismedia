using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

public static class PluginEndpoints {
    public static RouteGroupBuilder MapPluginEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/plugins")
            .WithTags("Plugins");

        group.MapGet("/", async (
            IPluginCatalogService plugins,
            CancellationToken cancellationToken) =>
            Results.Ok(await plugins.ListProvidersAsync(cancellationToken)))
            .WithName("ListPlugins")
            .WithSummary("Lists compatible community plugins discovered from installed and local development sources.")
            .Produces<IReadOnlyList<PluginProvider>>();

        group.MapPost("/{provider}", async (
            string provider,
            IPluginCatalogService plugins,
            CancellationToken cancellationToken) => {
                var result = await plugins.InstallAsync(provider, cancellationToken);
                return result is null
                    ? Results.NotFound(new ApiProblem("plugin_not_found", $"Plugin provider '{provider}' was not found or is not compatible."))
                    : Results.Ok(result);
            })
            .WithName("InstallPlugin")
            .WithSummary("Marks a compatible community plugin as installed and enabled.")
            .Produces<PluginProvider>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/{provider}/update", async (
            string provider,
            IPluginCatalogService plugins,
            CancellationToken cancellationToken) => {
                var result = await plugins.UpdateAsync(provider, cancellationToken);
                return result is null
                    ? Results.NotFound(new ApiProblem("plugin_update_not_found", $"No compatible update was found for plugin provider '{provider}'."))
                    : Results.Ok(result);
            })
            .WithName("UpdatePlugin")
            .WithSummary("Downloads and enables the newest compatible community plugin artifact.")
            .Produces<PluginProvider>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapDelete("/{provider}", async (
            string provider,
            IPluginCatalogService plugins,
            CancellationToken cancellationToken) =>
            await plugins.RemoveAsync(provider, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new ApiProblem("plugin_not_found", $"Plugin provider '{provider}' is not installed.")))
            .WithName("RemovePlugin")
            .WithSummary("Removes local installed state for a community plugin.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPut("/{provider}/auth", async (
            string provider,
            PluginAuthUpdateRequest request,
            IPluginCatalogService plugins,
            CancellationToken cancellationToken) =>
            await plugins.SaveAuthAsync(provider, request.Values, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new ApiProblem("plugin_not_found", $"Plugin provider '{provider}' was not found or is not compatible.")))
            .WithName("UpdatePluginAuth")
            .WithSummary("Stores credential values for a community plugin provider.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }
}
