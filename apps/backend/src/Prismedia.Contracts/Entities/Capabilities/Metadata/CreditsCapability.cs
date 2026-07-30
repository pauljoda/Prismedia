namespace Prismedia.Contracts.Entities;

/// <summary>People credited within an Entity, including their scoped roles and labels.</summary>
/// <param name="Items">Ordered credit metadata for the referenced people.</param>
[CapabilityKind("credits")]
public sealed record CreditsCapability(IReadOnlyList<EntityCreditMetadata> Items) : EntityCapability;
