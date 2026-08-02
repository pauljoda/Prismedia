namespace Prismedia.Contracts.Entities;

/// <summary>
/// Document marker proving this entity directly owns a playable video source. Playback state is
/// represented separately by <see cref="ConsumptionCapability"/> and remains user-state data.
/// </summary>
[CapabilityKind("playable-video")]
public sealed record PlayableVideoCapability : EntityCapability;
