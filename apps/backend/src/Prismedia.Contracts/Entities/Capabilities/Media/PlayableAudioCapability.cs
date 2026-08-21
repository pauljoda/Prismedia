namespace Prismedia.Contracts.Entities;

/// <summary>
/// Document marker proving this Entity directly owns a playable audio source. Queue ownership,
/// playback preferences, and consumption state remain separate generic contracts.
/// </summary>
[CapabilityKind("playable-audio")]
public sealed record PlayableAudioCapability : EntityCapability;
