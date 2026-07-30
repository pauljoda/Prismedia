namespace Prismedia.Contracts.Entities;

/// <summary>Fallback labels embedded in an Audio Track when no linked Entity supplies them.</summary>
/// <param name="Artist">Embedded artist label.</param>
/// <param name="Album">Embedded album label.</param>
[CapabilityKind("embedded-audio-metadata")]
public sealed record EmbeddedAudioMetadataCapability(string? Artist, string? Album) : EntityCapability;
