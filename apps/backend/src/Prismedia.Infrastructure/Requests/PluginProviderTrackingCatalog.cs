using Prismedia.Application.Requests;
using Prismedia.Application.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Requests;

/// <summary>
/// Projects central identity routes into the namespace list used by monitor eligibility.
/// </summary>
public sealed class PluginProviderTrackingCatalog(IPluginIdentityRouter router) : IProviderTrackingCatalog {
    public async Task<IReadOnlyList<string>> TrackableProvidersAsync(
        string pluginKindCode, IReadOnlyList<ExternalIdentity> identities, CancellationToken cancellationToken) {
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
}
