using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>
/// Describes an Entity that can supply a queue to the shared audio player. The queue items remain
/// ordinary source-backed Entities; this capability only defines how every client must interpret
/// that queue, independent of the owning Entity kind.
/// </summary>
/// <param name="ItemKind">Entity kind streamed by each queue item.</param>
/// <param name="PreservesQueueOrder">Whether source order is semantic and shuffle must stay disabled.</param>
/// <param name="SupportsPlaybackRate">Whether the shared player may expose variable-rate playback.</param>
[CapabilityKind("playable-audio")]
public sealed record PlayableAudioCapability(
    EntityKind ItemKind,
    bool PreservesQueueOrder,
    bool SupportsPlaybackRate) : EntityCapability;
