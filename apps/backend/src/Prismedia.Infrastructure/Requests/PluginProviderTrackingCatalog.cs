using Prismedia.Application.Requests;
using Prismedia.Application.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Requests;

/// <summary>
/// Validates an authoritative provider identity, with the prior namespace projection retained only
/// for legacy Entities that do not yet have a binding.
/// </summary>
public sealed class PluginProviderTrackingCatalog(IPluginIdentityRouter router) : IProviderTrackingCatalog {
    public async Task<IReadOnlyList<string>> TrackableProvidersAsync(
        string pluginKindCode,
        IReadOnlyList<ExternalIdentity> identities,
        PluginIdentityRoute? providerIdentity,
        CancellationToken cancellationToken) {
        if (providerIdentity is not null) {
            return await ResolveBoundProviderAsync(
                pluginKindCode,
                identities,
                providerIdentity,
                cancellationToken);
        }

        var routes = await router.ResolveAsync(
            pluginKindCode,
            IdentifyAction.LookupId,
            identities,
            cancellationToken);
        return routes
            .Select(route => route.Identity.Namespace)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<string>> ResolveBoundProviderAsync(
        string pluginKindCode,
        IReadOnlyList<ExternalIdentity> identities,
        PluginIdentityRoute providerIdentity,
        CancellationToken cancellationToken) {
        if (!identities.Contains(providerIdentity.Identity)) {
            return [];
        }

        var routes = await router.ResolveAsync(
            pluginKindCode,
            IdentifyAction.LookupId,
            [providerIdentity.Identity],
            cancellationToken);
        var exactRoute = routes.FirstOrDefault(route =>
            route.Identity == providerIdentity.Identity
            && string.Equals(route.PluginId, providerIdentity.PluginId, StringComparison.OrdinalIgnoreCase));
        return exactRoute is null ? [] : [exactRoute.PluginId];
    }
}
