namespace Prismedia.Contracts.Entities;

/// <summary>Identifies the child or member Entity selected as an Entity's cover.</summary>
/// <param name="EntityId">Selected cover Entity identifier, or null when selection is automatic.</param>
[CapabilityKind("cover-selection")]
public sealed record CoverSelectionCapability(Guid? EntityId) : EntityCapability;
