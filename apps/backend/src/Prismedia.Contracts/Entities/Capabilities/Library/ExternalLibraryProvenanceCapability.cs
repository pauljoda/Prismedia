using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>An exact saved holding associated with this Entity inside an externally managed library.</summary>
/// <param name="HoldingId">Stable Prismedia tracking identity.</param>
/// <param name="Item">Connection-scoped remote identity pin used to address the linked holding.</param>
/// <param name="Status">Last saved tracking state, including released historical associations.</param>
public sealed record ExternalManagedHoldingReference(
    Guid HoldingId,
    ManagedItemInput Item,
    ManagedTrackingStatus Status);

/// <summary>Durable provider fulfillment progress shown before and after files become available.</summary>
/// <param name="RequestId">Stable request identity, including while remote creation is pending.</param>
/// <param name="Phase">Saved fulfillment phase; completion requires locally readable files.</param>
/// <param name="UpdatedAt">Time the saved fulfillment state was last changed.</param>
/// <param name="Problem">Current user-facing problem or waiting explanation, if any.</param>
public sealed record ExternalLibraryRequestReference(
    Guid RequestId,
    ManagedRequestPhase Phase,
    DateTimeOffset UpdatedAt,
    string? Problem);

/// <summary>Saved provenance for an Entity whose effective library root is an external read-only mapping.</summary>
/// <param name="ConnectionId">Prismedia connection that owns the external-library mapping.</param>
/// <param name="ConnectionName">User-facing saved connection name.</param>
/// <param name="PluginId">Installed provider identity; no connection address or credentials are exposed.</param>
/// <param name="LibraryRootId">Mapped Prismedia library identity governing this Entity.</param>
/// <param name="LibraryLabel">User-facing mapped library name.</param>
/// <param name="Holding">Exact linked holding when an explicit entity association was retained.</param>
/// <param name="Request">An accepted external request, also present for fileless wanted entities.</param>
[CapabilityKind("external-library-provenance")]
public sealed record ExternalLibraryProvenanceCapability(
    Guid ConnectionId,
    string ConnectionName,
    string PluginId,
    Guid LibraryRootId,
    string LibraryLabel,
    ExternalManagedHoldingReference? Holding = null,
    ExternalLibraryRequestReference? Request = null) : EntityCapability;
