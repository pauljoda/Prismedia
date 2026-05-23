using EntityPosition = Prismedia.Domain.Capabilities.CapabilityPosition.Item;

namespace Prismedia.Contracts.Entities;

/// <summary>API-facing structural position capability.</summary>
[CapabilityKind("position")]
public sealed record PositionCapability(IReadOnlyList<EntityPosition> Items) : EntityCapability;
