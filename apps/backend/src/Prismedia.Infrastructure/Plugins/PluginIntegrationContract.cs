using Prismedia.Contracts.Plugins;
using Prismedia.Application.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>Rejects malformed declarations before an installed package receives connection authority.</summary>
internal static class PluginIntegrationContract {
    internal static bool IsValid(PluginIntegrationDefinition definition) {
        if (definition.ProtocolVersion != IntegrationProtocol.CurrentVersion || definition.Capabilities is not { Count: > 0 and <= 8 }
            || definition.Settings is null || definition.Settings.Count > 32
            || !IntegrationDeliveryOriginPolicy.HasValidDeclaration(definition)) return false;
        var kinds = new HashSet<PluginCapability>();
        foreach (var capability in definition.Capabilities) {
            if (capability is null || !kinds.Add(capability.Kind) || !Enum.IsDefined(capability.Kind)
                || capability.Operations is not { Count: > 0 }
                || capability.EntityKinds is not { Count: > 0 }
                || capability.Operations.Distinct().Count() != capability.Operations.Count
                || capability.EntityKinds.Distinct().Count() != capability.EntityKinds.Count
                || capability.Operations.Any(operation => !PluginCapabilityPolicy.Allows(capability.Kind, operation))
                || capability.EntityKinds.Any(kind => !Enum.IsDefined(kind))) return false;
        }
        return definition.Settings.Count == 0 || PluginManifestContract.IsUsableSearch(new PluginSearchDefinition(definition.Settings));
    }
}
