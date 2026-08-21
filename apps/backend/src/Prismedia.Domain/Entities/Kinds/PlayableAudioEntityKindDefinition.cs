namespace Prismedia.Domain.Entities;

/// <summary>
/// Generic queue behavior advertised by an Entity kind that can supply audio items to the shared
/// player. It deliberately says nothing about albums, books, podcasts, or any other product label.
/// </summary>
/// <param name="ItemKind">Entity kind that owns each concrete audio source.</param>
/// <param name="PreservesQueueOrder">Whether item order carries meaning and cannot be shuffled.</param>
/// <param name="SupportsPlaybackRate">Whether variable-rate playback is valid for this queue.</param>
public sealed record AudioPlaybackPolicy(
    EntityKind ItemKind,
    bool PreservesQueueOrder,
    bool SupportsPlaybackRate);

/// <summary>Opt-in facet for definitions whose Entities own a shared-player audio queue.</summary>
public interface IAudioPlaybackOwnerKindDefinition {
    /// <summary>Queue-item and transport semantics projected to every client.</summary>
    AudioPlaybackPolicy AudioPlaybackPolicy { get; }
}

/// <summary>
/// Opt-in facet for definitions whose Entities directly own one playable audio source. Consumers
/// use the projected playable-audio capability rather than inspecting the concrete Entity kind.
/// </summary>
public interface IPlayableAudioKindDefinition : IAudioPlaybackOwnerKindDefinition {
    /// <summary>Typed identity of the directly playable audio definition.</summary>
    EntityKind Kind { get; }
}
