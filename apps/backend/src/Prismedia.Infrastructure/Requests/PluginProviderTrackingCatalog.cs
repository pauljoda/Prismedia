using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Requests;

/// <summary>
/// Resolves provider trackability from the plugin catalog: a provider identity is trackable when it names
/// an enabled plugin whose manifest declares the lookup-id action for the kind — the same gate the request
/// detail and container-sync paths apply before querying a provider.
/// </summary>
public sealed class PluginProviderTrackingCatalog(PluginCatalogService catalog) : IProviderTrackingCatalog {
    private static readonly string LookupIdAction = IdentifyAction.LookupId.ToCode();

    public async Task<IReadOnlyList<string>> TrackableProvidersAsync(
        string pluginKindCode, IReadOnlyList<ProviderRef> providerIds, CancellationToken cancellationToken) {
        if (providerIds.Count == 0) {
            return [];
        }

        var providers = await catalog.ListProvidersAsync(cancellationToken);
        return providerIds
            .Select(reference => reference.Provider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(providerId => providers.Any(provider =>
                provider.Enabled
                && string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase)
                && provider.Supports.Any(support =>
                    PluginEntityKindCompatibility.SupportsKind(support, pluginKindCode)
                    && support.Actions.Contains(LookupIdAction))))
            .ToArray();
    }
}
