namespace Prismedia.Contracts.Entities;

/// <summary>API-facing named date value attached to an entity.</summary>
/// <param name="Code">Stable date code, such as release, air, birth, or career-start.</param>
/// <param name="Value">Original display value from the user or provider.</param>
/// <param name="SortableValue">Optional normalized sortable date value.</param>
/// <param name="Precision">Optional precision code, such as year, month, day, or text.</param>
public sealed record EntityDate(
    string Code,
    string Value,
    DateOnly? SortableValue = null,
    string? Precision = null);

/// <summary>API-facing named date capability.</summary>
[CapabilityKind("dates")]
public sealed record DatesCapability(IReadOnlyList<EntityDate> Items) : EntityCapability;
