using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;

namespace Prismedia.Api.Endpoints;

internal static class IdentifyProviderEndpoints {
    internal static RouteGroupBuilder MapIdentifyProviderEndpoints(this RouteGroupBuilder group) {
        group.MapGet("/providers", async (
            string? kind,
            IIdentifyProviderService identify,
            CancellationToken cancellationToken) =>
            Results.Ok(await identify.ListProvidersAsync(kind, cancellationToken)))
            .WithName("ListIdentifyProviders")
            .WithSummary("Lists compatible providers that can identify the requested entity kind.")
            .Produces<IReadOnlyList<PluginProvider>>();

        return group;
    }
}
