using EntitySource = Prismedia.Domain.Capabilities.CapabilitySource.Item;

namespace Prismedia.Contracts.Entities;

/// <summary>API-facing source provenance capability.</summary>
[CapabilityKind("source")]
public sealed record SourceCapability(IReadOnlyList<EntitySource> Items) : EntityCapability;
