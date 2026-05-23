namespace Prismedia.Contracts.Entities;

/// <summary>API-facing shared description capability.</summary>
/// <param name="Value">User-facing description text.</param>
[CapabilityKind("description")]
public sealed record DescriptionCapability(string Value) : EntityCapability;
