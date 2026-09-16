namespace Prismedia.Domain.Entities;

/// <summary>Canonical plugin responsibility families, independent of Entity metadata fields.</summary>
public enum PluginCapability {
    /// <summary>Identify works and propose descriptive metadata.</summary>
    [Code("metadata")]
    Metadata,

    /// <summary>Find source items and selectable editions or releases.</summary>
    [Code("catalog-discovery")]
    CatalogDiscovery,

    /// <summary>Resolve a selected source item into an acquisition offer.</summary>
    [Code("acquisition-source")]
    AcquisitionSource,

    /// <summary>Execute an acquisition and retain verifiable outputs.</summary>
    [Code("transfer-executor")]
    TransferExecutor,

    /// <summary>Delegate monitoring, selection, downloading, and organization.</summary>
    [Code("external-manager")]
    ExternalManager,

    /// <summary>Read holdings owned by another application.</summary>
    [Code("connected-library")]
    ConnectedLibrary,
}
