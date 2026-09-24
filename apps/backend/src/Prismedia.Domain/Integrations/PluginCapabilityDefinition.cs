using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>
/// The responsibility boundary of one <see cref="PluginCapability"/>: which integration operations belong to
/// it and whether a connection may enable it. Probe is connection-wide; metadata uses identify v2 instead
/// of connection operations.
/// </summary>
public sealed class PluginCapabilityDefinition {
    #region Static Variables

    /// <summary>Identifies works and proposes descriptive metadata through the identify contract.</summary>
    public static readonly PluginCapabilityDefinition Metadata = new(PluginCapability.Metadata,
        enabledPerConnection: false, operations: []);

    /// <summary>Finds source items and selectable editions or releases.</summary>
    public static readonly PluginCapabilityDefinition CatalogDiscovery = new(PluginCapability.CatalogDiscovery,
        enabledPerConnection: true,
        operations: [IntegrationOperation.Search, IntegrationOperation.Browse, IntegrationOperation.Inspect]);

    /// <summary>Resolves a selected source item into an acquisition offer.</summary>
    public static readonly PluginCapabilityDefinition AcquisitionSource = new(PluginCapability.AcquisitionSource,
        enabledPerConnection: true,
        operations: [IntegrationOperation.Resolve, IntegrationOperation.RequestSource, IntegrationOperation.ObserveSource]);

    /// <summary>Executes an acquisition and retains verifiable outputs.</summary>
    public static readonly PluginCapabilityDefinition TransferExecutor = new(PluginCapability.TransferExecutor,
        enabledPerConnection: true,
        operations: [
            IntegrationOperation.Submit,
            IntegrationOperation.FindSubmission,
            IntegrationOperation.CancelSubmission,
            IntegrationOperation.GetJob,
            IntegrationOperation.Cancel,
            IntegrationOperation.ListArtifacts,
            IntegrationOperation.AuthorizeArtifact,
            IntegrationOperation.RenewRetention,
            IntegrationOperation.Acknowledge
        ]);

    /// <summary>Delegates monitoring, selection, downloading, and organization to another application.</summary>
    public static readonly PluginCapabilityDefinition ExternalManager = new(PluginCapability.ExternalManager,
        enabledPerConnection: true,
        operations: [
            IntegrationOperation.DiscoverManaged,
            IntegrationOperation.LookupManaged,
            IntegrationOperation.ManagerOptions,
            IntegrationOperation.EnsureManaged,
            IntegrationOperation.RequestManaged,
            IntegrationOperation.ConfigureManaged,
            IntegrationOperation.ReconcileManaged,
            IntegrationOperation.InspectManagedRelease
        ]);

    /// <summary>Reads holdings owned by another application.</summary>
    public static readonly PluginCapabilityDefinition ConnectedLibrary = new(PluginCapability.ConnectedLibrary,
        enabledPerConnection: true,
        operations: [IntegrationOperation.SearchLibrary, IntegrationOperation.GetLibraryItem, IntegrationOperation.ListLibraries]);

    /// <summary>Every capability definition, one per <see cref="PluginCapability"/> member.</summary>
    public static IReadOnlyList<PluginCapabilityDefinition> All { get; } = [
        Metadata,
        CatalogDiscovery,
        AcquisitionSource,
        TransferExecutor,
        ExternalManager,
        ConnectedLibrary
    ];

    #endregion

    #region Variables

    /// <summary>Persisted and generated identity of this capability.</summary>
    public PluginCapability Capability { get; }

    /// <summary>Whether a connection may enable this capability; metadata is configured on the plugin itself.</summary>
    public bool EnabledPerConnection { get; }

    /// <summary>Operations that belong to this responsibility family.</summary>
    public IReadOnlyList<IntegrationOperation> Operations { get; }

    #endregion

    #region Constructors

    private PluginCapabilityDefinition(PluginCapability capability, bool enabledPerConnection, IReadOnlyList<IntegrationOperation> operations) {
        Capability = capability;
        EnabledPerConnection = enabledPerConnection;
        Operations = operations;
    }

    #endregion

    #region Actions - Lookup

    /// <summary>Returns the definition of a capability.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined capability.</exception>
    public static PluginCapabilityDefinition For(PluginCapability capability) =>
        All.FirstOrDefault(definition => definition.Capability == capability)
        ?? throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unknown plugin capability.");

    /// <summary>Whether <paramref name="operation"/> belongs to this capability.</summary>
    public bool Allows(IntegrationOperation operation) => Operations.Contains(operation);

    #endregion
}
