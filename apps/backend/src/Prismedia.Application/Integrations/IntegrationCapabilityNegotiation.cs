using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Remote applications can restrict declared support; they cannot grant a plugin new authority.</summary>
public static class IntegrationCapabilityNegotiation {
    /// <summary>Intersects package support, remote support, host operations, and user-enabled capabilities.</summary>
    /// <param name="declared">Validated package declarations.</param>
    /// <param name="remote">Support reported by the configured application during its probe.</param>
    /// <param name="enabled">Capabilities the user enabled for this particular connection.</param>
    /// <returns>Only usable capability/operation/entity-kind combinations, without empty success declarations.</returns>
    public static IReadOnlyList<PluginIntegrationCapability> Intersect(
        IReadOnlyList<PluginIntegrationCapability> declared,
        IReadOnlyList<PluginIntegrationCapability> remote,
        IReadOnlyCollection<PluginCapability> enabled) {
        var result = new List<PluginIntegrationCapability>();
        foreach (var capability in declared.Where(item => enabled.Contains(item.Kind))) {
            var advertised = remote.Where(item => item.Kind == capability.Kind).ToArray();
            // Duplicate families are malformed; do not accidentally combine operations from unrelated scopes.
            if (advertised.Length != 1) continue;
            var peer = advertised[0];
            var operations = capability.Operations.Intersect(peer.Operations ?? [])
                .Where(operation => PluginCapabilityPolicy.Allows(capability.Kind, operation)).ToArray();
            var kinds = capability.EntityKinds.Intersect(peer.EntityKinds ?? []).ToArray();
            if (operations.Length > 0 && kinds.Length > 0) result.Add(new(capability.Kind, operations, kinds));
        }
        return result;
    }
}
