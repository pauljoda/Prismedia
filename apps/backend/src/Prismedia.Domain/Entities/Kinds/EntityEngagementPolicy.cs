namespace Prismedia.Domain.Entities;

/// <summary>
/// Definition-owned consumption behavior shared by application use cases and clients. The policy
/// separates position/completion vocabulary from the activity kind used for accumulated time.
/// </summary>
public sealed record EntityEngagementPolicy {
    /// <summary>Policy for kinds that do not expose engagement state.</summary>
    public static EntityEngagementPolicy None { get; } = new(EntityEngagementMode.None);

    /// <summary>Creates one validated engagement policy.</summary>
    /// <param name="mode">Vocabulary and state family exposed for the kind.</param>
    /// <param name="derivesCompletionFromPlaybackFraction">
    /// Whether ordinary playback progress may infer completion from the current position and runtime.
    /// </param>
    public EntityEngagementPolicy(
        EntityEngagementMode mode,
        bool derivesCompletionFromPlaybackFraction = false,
        ConsumptionActivityKind? defaultActivityKind = null) {
        if (derivesCompletionFromPlaybackFraction && mode != EntityEngagementMode.Playback) {
            throw new ArgumentException(
                "Playback-fraction completion requires playback engagement mode.",
                nameof(derivesCompletionFromPlaybackFraction));
        }

        Mode = mode;
        DerivesCompletionFromPlaybackFraction = derivesCompletionFromPlaybackFraction;
        DefaultActivityKind = defaultActivityKind ?? mode switch {
            EntityEngagementMode.Playback => ConsumptionActivityKind.Viewing,
            EntityEngagementMode.Reading => ConsumptionActivityKind.Reading,
            _ => null
        };
    }

    /// <summary>Vocabulary and state family exposed for the kind.</summary>
    public EntityEngagementMode Mode { get; }

    /// <summary>Whether position/runtime progress may infer completion for this kind.</summary>
    public bool DerivesCompletionFromPlaybackFraction { get; }

    /// <summary>
    /// Activity mode used when a client reports elapsed time. It may be present for view-only
    /// entities that deliberately expose no position or completion state.
    /// </summary>
    public ConsumptionActivityKind? DefaultActivityKind { get; }
}
