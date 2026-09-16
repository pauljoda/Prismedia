using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Keeps responsibility boundaries explicit when validating and negotiating plugin operations.</summary>
public static class PluginCapabilityPolicy {
    /// <summary>Whether an operation belongs to this capability. Probe is connection-wide; metadata uses identify v2.</summary>
    public static bool Allows(PluginCapability capability, IntegrationOperation operation) => capability switch {
        PluginCapability.CatalogDiscovery => operation is IntegrationOperation.Search or IntegrationOperation.Browse or IntegrationOperation.Inspect,
        PluginCapability.AcquisitionSource => operation is IntegrationOperation.Resolve,
        PluginCapability.TransferExecutor => operation is IntegrationOperation.Submit or IntegrationOperation.FindSubmission
            or IntegrationOperation.GetJob or IntegrationOperation.Cancel or IntegrationOperation.ListArtifacts
            or IntegrationOperation.AuthorizeArtifact or IntegrationOperation.Acknowledge,
        PluginCapability.ExternalManager => operation is IntegrationOperation.LookupManaged or IntegrationOperation.EnsureManaged
            or IntegrationOperation.RequestManaged or IntegrationOperation.ConfigureManaged or IntegrationOperation.ReconcileManaged,
        PluginCapability.ConnectedLibrary => operation is IntegrationOperation.SearchLibrary or IntegrationOperation.GetLibraryItem,
        _ => false
    };
}
