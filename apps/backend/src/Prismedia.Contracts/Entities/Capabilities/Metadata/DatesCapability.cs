using EntityDate = Prismedia.Domain.Capabilities.EntityDate;

namespace Prismedia.Contracts.Entities;

/// <summary>API-facing named date capability.</summary>
[CapabilityKind("dates")]
public sealed record DatesCapability(IReadOnlyList<EntityDate> Items) : EntityCapability;
