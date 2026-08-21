namespace Prismedia.Contracts.Entities;

/// <summary>Provider-facing lifecycle metadata shared by serialized video and comic series.</summary>
/// <param name="Status">Optional series lifecycle status supplied by metadata.</param>
[CapabilityKind("series-metadata")]
public sealed record SeriesMetadataCapability(string? Status) : EntityCapability;
