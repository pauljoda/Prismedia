namespace Prismedia.Domain.Entities;

/// <summary>
/// Opt-in facet for kinds a connected manager can fulfill. Services ask the requested kind's definition for
/// its managed-fulfillment rules instead of naming particular kinds, so a new kind joins by declaring a policy.
/// </summary>
public interface IManagedFulfillmentKindDefinition {
    /// <summary>How a connected manager requests, scopes, monitors, and searches this kind.</summary>
    ManagedFulfillmentPolicy ManagedFulfillment { get; }
}
