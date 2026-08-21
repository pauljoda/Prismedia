namespace Prismedia.Domain.Entities;

/// <summary>
/// Opt-in facet for definitions whose Entities directly own one playable audio source. Consumers
/// use the projected playable-audio capability rather than inspecting the concrete Entity kind.
/// </summary>
public interface IPlayableAudioKindDefinition {
    /// <summary>Typed identity of the directly playable audio definition.</summary>
    EntityKind Kind { get; }
}
