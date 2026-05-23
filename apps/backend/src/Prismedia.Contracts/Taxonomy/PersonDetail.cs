using Prismedia.Contracts.Entities;

namespace Prismedia.Contracts.Taxonomy;

/// <summary>
/// API-facing detail shape for a person taxonomy entity.
/// </summary>
public sealed record PersonDetail : EntityDetail {
    /// <summary>Optional text used to distinguish people with the same name.</summary>
    public required string? Disambiguation { get; init; }

    /// <summary>Optional gender value from imported or edited metadata.</summary>
    public required string? Gender { get; init; }

    /// <summary>Optional country value.</summary>
    public required string? Country { get; init; }

    /// <summary>Optional ethnicity value.</summary>
    public required string? Ethnicity { get; init; }

    /// <summary>Optional eye color value.</summary>
    public required string? EyeColor { get; init; }

    /// <summary>Optional hair color value.</summary>
    public required string? HairColor { get; init; }

    /// <summary>Optional height value in centimeters when known.</summary>
    public required int? Height { get; init; }

    /// <summary>Optional weight value in kilograms when known.</summary>
    public required int? Weight { get; init; }

    /// <summary>Optional measurements text.</summary>
    public required string? Measurements { get; init; }

    /// <summary>Optional tattoo description.</summary>
    public required string? Tattoos { get; init; }

    /// <summary>Optional piercing description.</summary>
    public required string? Piercings { get; init; }
}
