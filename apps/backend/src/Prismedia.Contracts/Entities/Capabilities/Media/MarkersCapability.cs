using EntityMarker = Prismedia.Domain.Capabilities.CapabilityMarkers.Item;

namespace Prismedia.Contracts.Entities;

/// <summary>API-facing marker capability.</summary>
[CapabilityKind("markers")]
public sealed record MarkersCapability(IReadOnlyList<EntityMarker> Items) : EntityCapability;
