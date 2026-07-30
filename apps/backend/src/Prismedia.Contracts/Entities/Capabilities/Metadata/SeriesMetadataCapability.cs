namespace Prismedia.Contracts.Entities;

/// <summary>Provider-facing lifecycle metadata that applies only to a Video Series.</summary>
/// <param name="Status">Optional series lifecycle status supplied by metadata.</param>
[CapabilityKind("series-metadata")]
public sealed record SeriesMetadataCapability(string? Status) : EntityCapability;
