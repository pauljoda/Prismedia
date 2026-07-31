namespace Prismedia.Domain.Entities;

/// <summary>
/// Definition-owned engagement behavior shared by list filtering and clients. The policy says
/// whether the kind exposes completion state and whether playback recorded on a direct child
/// contributes to the container's state.
/// </summary>
public sealed record EntityEngagementPolicy {
    /// <summary>Policy for kinds that do not expose engagement state.</summary>
    public static EntityEngagementPolicy None { get; } = new(EntityEngagementMode.None);

    /// <summary>Creates one validated engagement policy.</summary>
    /// <param name="mode">Vocabulary and state family exposed for the kind.</param>
    /// <param name="aggregatesDirectChildPlayback">
    /// Whether direct-child playback also represents engagement with this container.
    /// </param>
    /// <param name="derivesCompletionFromPlaybackFraction">
    /// Whether ordinary playback progress may infer completion from the current position and runtime.
    /// </param>
    public EntityEngagementPolicy(
        EntityEngagementMode mode,
        bool aggregatesDirectChildPlayback = false,
        bool derivesCompletionFromPlaybackFraction = false) {
        if (aggregatesDirectChildPlayback && mode != EntityEngagementMode.Playback) {
            throw new ArgumentException(
                "Direct-child playback aggregation requires playback engagement mode.",
                nameof(aggregatesDirectChildPlayback));
        }

        if (derivesCompletionFromPlaybackFraction && mode != EntityEngagementMode.Playback) {
            throw new ArgumentException(
                "Playback-fraction completion requires playback engagement mode.",
                nameof(derivesCompletionFromPlaybackFraction));
        }

        Mode = mode;
        AggregatesDirectChildPlayback = aggregatesDirectChildPlayback;
        DerivesCompletionFromPlaybackFraction = derivesCompletionFromPlaybackFraction;
    }

    /// <summary>Vocabulary and state family exposed for the kind.</summary>
    public EntityEngagementMode Mode { get; }

    /// <summary>Whether playback on a direct child contributes to this container's state.</summary>
    public bool AggregatesDirectChildPlayback { get; }

    /// <summary>Whether position/runtime progress may infer completion for this kind.</summary>
    public bool DerivesCompletionFromPlaybackFraction { get; }
}
