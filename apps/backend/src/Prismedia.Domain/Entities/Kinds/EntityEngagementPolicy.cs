using Prismedia.Domain.Capabilities;

namespace Prismedia.Domain.Entities;

/// <summary>
/// Definition-owned consumption behavior shared by application use cases and clients. The policy
/// separates position/completion vocabulary from the activity kind used for accumulated time, and
/// declares the consumption modalities that keep their own exact resume positions.
/// </summary>
public sealed record EntityEngagementPolicy {
    #region Static Variables

    /// <summary>Policy for kinds that do not expose engagement state.</summary>
    public static EntityEngagementPolicy None { get; } = new(EntityEngagementMode.None);

    #endregion

    #region Variables

    /// <summary>
    /// Consumption modalities that each keep an exact checkpoint for this kind, in declaration order.
    /// Empty for kinds whose single progress cursor is the only position.
    /// </summary>
    public IReadOnlyList<ConsumptionModalityDefinition> Modalities { get; }

    /// <summary>Vocabulary and state family exposed for the kind.</summary>
    public EntityEngagementMode Mode { get; }

    /// <summary>Whether position/runtime progress may infer completion for this kind.</summary>
    public bool DerivesCompletionFromPlaybackFraction { get; }

    /// <summary>
    /// Activity mode used when a client reports elapsed time. It may be present for view-only
    /// entities that deliberately expose no position or completion state.
    /// </summary>
    public ConsumptionActivityKind? DefaultActivityKind { get; }

    #endregion

    #region Constructors

    /// <summary>Creates one validated engagement policy.</summary>
    /// <param name="mode">Vocabulary and state family exposed for the kind.</param>
    /// <param name="derivesCompletionFromPlaybackFraction">
    /// Whether ordinary playback progress may infer completion from the current position and runtime.
    /// </param>
    /// <param name="defaultActivityKind">Activity bucket used when a report names none.</param>
    /// <param name="modalities">
    /// Consumption modalities that keep independent exact checkpoints. Declaring any requires an
    /// engagement mode other than <see cref="EntityEngagementMode.None"/>, and each modality may be
    /// declared once.
    /// </param>
    /// <exception cref="ArgumentException">The combination of options is not coherent.</exception>
    public EntityEngagementPolicy(
        EntityEngagementMode mode,
        bool derivesCompletionFromPlaybackFraction = false,
        ConsumptionActivityKind? defaultActivityKind = null,
        IReadOnlyList<ConsumptionModalityDefinition>? modalities = null) {
        if (derivesCompletionFromPlaybackFraction && mode != EntityEngagementMode.Playback) {
            throw new ArgumentException(
                "Playback-fraction completion requires playback engagement mode.",
                nameof(derivesCompletionFromPlaybackFraction));
        }

        var declaredModalities = modalities ?? [];
        if (declaredModalities.Count > 0 && mode == EntityEngagementMode.None) {
            throw new ArgumentException(
                "Consumption modalities require an engagement mode other than none.",
                nameof(modalities));
        }
        if (declaredModalities.DistinctBy(modality => modality.Modality).Count() != declaredModalities.Count) {
            throw new ArgumentException(
                "Each consumption modality may be declared only once.",
                nameof(modalities));
        }

        Mode = mode;
        DerivesCompletionFromPlaybackFraction = derivesCompletionFromPlaybackFraction;
        DefaultActivityKind = defaultActivityKind ?? mode switch {
            EntityEngagementMode.Playback => ConsumptionActivityKind.Viewing,
            EntityEngagementMode.Reading => ConsumptionActivityKind.Reading,
            _ => null
        };
        Modalities = declaredModalities;
    }

    #endregion

    #region Actions - Modalities

    /// <summary>Returns the declared definition of <paramref name="modality"/>, or null when undeclared.</summary>
    public ConsumptionModalityDefinition? ModalityFor(ConsumptionModality modality) =>
        Modalities.FirstOrDefault(definition => definition.Modality == modality);

    /// <summary>Returns the declared modality whose time lands in <paramref name="activityKind"/>, if any.</summary>
    public ConsumptionModalityDefinition? ModalityFor(ConsumptionActivityKind? activityKind) =>
        activityKind is { } kind
            ? Modalities.FirstOrDefault(definition => definition.ActivityKind == kind)
            : null;

    #endregion
}
