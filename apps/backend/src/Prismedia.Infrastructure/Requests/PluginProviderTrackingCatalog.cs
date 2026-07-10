using Prismedia.Application.Requests;
using Prismedia.Application.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Requests;

/// <summary>
/// Validates an authoritative provider identity. A legacy Entity without a persisted binding is
/// trackable only when its complete identity set resolves to one unambiguous plugin route.
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
        // Legacy Entities have no persisted plugin binding. They are safe to monitor only when the
        // manifest router resolves the complete identity set to one exact route; otherwise a later
        // maintenance pass could not know which plugin owns the durable identity. Match the batched
        // child-monitoring path and surface the stable plugin id, never the identity namespace.
        var candidates = routes.Distinct().ToArray();
        return candidates.Length == 1 ? [candidates[0].PluginId] : [];
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> TrackableProvidersBatchAsync(
        IReadOnlyList<ProviderTrackingQuery> queries,
        CancellationToken cancellationToken) {
        var result = queries
            .Select(query => query.EntityId)
            .Distinct()
            .ToDictionary(entityId => entityId, _ => (IReadOnlyList<string>)[]);

        foreach (var kindGroup in queries.GroupBy(query => query.PluginKindCode, StringComparer.Ordinal)) {
            var identities = kindGroup
                .SelectMany(query => query.Identities)
                .Distinct()
                .ToArray();
            var routes = identities.Length == 0
                ? []
                : await router.ResolveAsync(
                    kindGroup.Key,
                    IdentifyAction.LookupId,
                    identities,
                    cancellationToken);

            foreach (var query in kindGroup) {
                if (query.ProviderIdentity is { } binding) {
                    var exact = query.Identities.Contains(binding.Identity)
                        && routes.Any(route =>
                            route.Identity == binding.Identity
                            && string.Equals(route.PluginId, binding.PluginId, StringComparison.OrdinalIgnoreCase));
                    result[query.EntityId] = exact ? [binding.PluginId] : [];
                    continue;
                }

                // Match single-Entity eligibility: an unbound legacy Entity is trackable only when its
                // identities resolve to one unambiguous plugin route. The stable plugin id is surfaced.
                var candidates = routes
                    .Where(route => query.Identities.Contains(route.Identity))
                    .Distinct()
                    .ToArray();
                result[query.EntityId] = candidates.Length == 1 ? [candidates[0].PluginId] : [];
            }
        }

        return result;
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
