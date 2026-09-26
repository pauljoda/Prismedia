namespace Prismedia.Domain.Entities;

/// <summary>The external responsibility that reserves a local acquisition scope.</summary>
public enum FulfillmentOwnerKind {
    /// <summary>A connected application owns future acquisition and organization.</summary>
    [Code("external-manager")] ExternalManager,
    /// <summary>An established connected holding owns its sources and replacements.</summary>
    [Code("connected-library")] ConnectedLibrary
}
