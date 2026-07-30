namespace Prismedia.Contracts.Entities;

/// <summary>Optional profile metadata that applies only to Person Entities.</summary>
[CapabilityKind("person-profile")]
public sealed record PersonProfileCapability(
    string? Disambiguation,
    string? Gender,
    string? Country,
    string? Ethnicity,
    string? EyeColor,
    string? HairColor,
    int? Height,
    int? Weight,
    string? Measurements,
    string? Tattoos,
    string? Piercings) : EntityCapability;
