using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

public sealed partial class EntityMetadataApplyService {
    /// <summary>
    /// Persists only the accepted proposal provider's declared LookupId route. Incidental IDs returned
    /// by that provider remain useful raw identities but cannot become the authoritative source.
    /// </summary>
    private async Task BindProviderIdentityAsync(
        EntityRow entity,
        string pluginId,
        IReadOnlyDictionary<string, string> proposalExternalIds,
        CancellationToken cancellationToken) {
        if (_providerIdentities is null
            || _identityRouter is null
            || string.IsNullOrWhiteSpace(pluginId)
            || proposalExternalIds.Count == 0) {
            return;
        }

        var proposed = new List<ExternalIdentity>();
        foreach (var (identityNamespace, value) in proposalExternalIds) {
            try {
                proposed.Add(new ExternalIdentity(identityNamespace, value));
            } catch (ArgumentException) {
                // Invalid proposal locators are excluded at the canonical identity boundary.
            }
        }
        if (proposed.Count == 0) {
            return;
        }

        var persisted = (await _externalIdentities.ListAsync(entity.Id, cancellationToken))
            .Select(value => value.Identity)
            .ToHashSet();
        var eligible = proposed.Where(persisted.Contains).Distinct().ToArray();
        if (eligible.Length == 0) {
            return;
        }

        var routes = await _identityRouter.ResolveAsync(
            entity.KindCode,
            IdentifyAction.LookupId,
            eligible,
            cancellationToken);
        var acceptedProviderRoutes = routes
            .Where(value => string.Equals(value.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (acceptedProviderRoutes.Length != 1) {
            return;
        }

        var route = acceptedProviderRoutes[0];
        await _providerIdentities.SetAsync(
            entity.Id,
            route.PluginId,
            route.Identity,
            cancellationToken);
    }
}
