using Prismedia.Application.Entities;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

public sealed partial class EntityMetadataApplyService {
    /// <summary>
    /// Applies provider identities while honoring only exact, reviewed retirement evidence that the
    /// accepted provider is registered to handle. Omitted retirement evidence never removes an ID,
    /// and IDs from unrelated namespaces remain attached to the entity.
    /// </summary>
    private async Task ReconcileProviderExternalIdsAsync(
        EntityRow entity,
        EntityMetadataProposal proposal,
        CancellationToken cancellationToken) {
        var incoming = BuildExternalIdentityAssociations(proposal.Patch.ExternalIds, proposal.Patch.Urls);
        var retirements = NormalizeIdentityRetirements(proposal.Patch.RetiredExternalIds);
        if (retirements.Count == 0) {
            await _externalIdentities.WriteAsync(
                entity.Id,
                incoming,
                ExternalIdentityWriteMode.Upsert,
                cancellationToken);
            return;
        }

        await EnsureProviderOwnsRetirementsAsync(
            entity.KindCode,
            proposal.Provider,
            retirements,
            cancellationToken);

        var retired = retirements.ToHashSet();
        var reconciled = (await _externalIdentities.ListAsync(entity.Id, cancellationToken))
            .Where(association => !retired.Contains(new ExternalIdentity(association.Provider, association.Value)))
            .ToDictionary(association => association.Provider, StringComparer.OrdinalIgnoreCase);
        foreach (var association in incoming) {
            reconciled[association.Provider] = association;
        }

        await _externalIdentities.WriteAsync(
            entity.Id,
            reconciled.Values,
            ExternalIdentityWriteMode.ReplaceAll,
            cancellationToken);
    }

    private static IReadOnlyList<ExternalIdentity> NormalizeIdentityRetirements(
        IReadOnlyList<ExternalIdentityRetirement>? retirements) {
        var normalized = new List<ExternalIdentity>();
        foreach (var retirement in retirements ?? []) {
            ExternalIdentity identity;
            try {
                identity = new ExternalIdentity(retirement.Namespace, retirement.Value);
            } catch (ArgumentException exception) {
                throw new ArgumentException("Provider identity retirements must contain valid external identities.", nameof(retirements), exception);
            }

            if (normalized.Contains(identity)) {
                throw new ArgumentException("Provider identity retirements must be unique.", nameof(retirements));
            }
            normalized.Add(identity);
        }
        return normalized;
    }

    private async Task EnsureProviderOwnsRetirementsAsync(
        string entityKindCode,
        string pluginId,
        IReadOnlyList<ExternalIdentity> retirements,
        CancellationToken cancellationToken) {
        if (_identityRouter is null) {
            throw new InvalidOperationException("Provider identity retirement requires an identity router.");
        }

        var routes = await _identityRouter.ResolveAsync(
            entityKindCode,
            IdentifyAction.LookupId,
            retirements,
            cancellationToken);
        foreach (var retirement in retirements) {
            if (!routes.Any(route =>
                    route.Identity == retirement
                    && string.Equals(route.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))) {
                throw new ArgumentException(
                    $"Plugin '{pluginId}' cannot retire external identity namespace '{retirement.Namespace}' for entity kind '{entityKindCode}'.");
            }
        }
    }

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
