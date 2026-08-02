namespace Prismedia.Domain.Entities;

/// <summary>
/// Definition-owned engagement behavior shared by list filtering and clients. The policy says
/// whether the kind exposes completion state and whether playback progress may derive completion
/// from runtime.
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
        if (mode == EntityEngagementMode.None && DefaultActivityKind is not null) {
            throw new ArgumentException("Entities without engagement cannot declare an activity kind.", nameof(defaultActivityKind));
        }
    }

    /// <summary>Vocabulary and state family exposed for the kind.</summary>
    public EntityEngagementMode Mode { get; }

    /// <summary>Whether position/runtime progress may infer completion for this kind.</summary>
    public bool DerivesCompletionFromPlaybackFraction { get; }

    /// <summary>Activity mode used when a client reports duration without an explicit mode.</summary>
    public ConsumptionActivityKind? DefaultActivityKind { get; }
}
