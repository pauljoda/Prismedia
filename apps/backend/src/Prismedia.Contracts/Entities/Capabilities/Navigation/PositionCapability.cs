namespace Prismedia.Contracts.Entities;

/// <summary>API-facing structural ordering or label value.</summary>
/// <param name="Code">Stable position code, such as season, volume, chapter, page, track, or sort.</param>
/// <param name="Value">Numeric position value.</param>
/// <param name="Label">Optional display label when the numeric value is not enough for UI.</param>
public sealed record EntityPosition(string Code, int Value, string? Label = null);

/// <summary>API-facing structural position capability.</summary>
[CapabilityKind("position")]
public sealed record PositionCapability(IReadOnlyList<EntityPosition> Items) : EntityCapability;
