using System.Collections.Frozen;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>
/// Entity kinds on which each collection-rule field has meaning. Evaluation and client choices
/// share this policy; an empty rule type selection means all supported kinds, not standalone video.
/// </summary>
public static class CollectionRuleFieldPolicy {
    private static readonly IReadOnlySet<EntityKind> CollectionKinds =
        EntityKindRegistry.Get<CollectionEntityKindDefinition>().ContainableKinds.ToFrozenSet();
    private static readonly IReadOnlySet<EntityKind> PlayableVideoKinds = EntityKindRegistry.All
        .Where(definition => definition is IPlayableVideoKindDefinition)
        .Select(definition => definition.Kind).ToFrozenSet();
    private static readonly IReadOnlySet<EntityKind> EpisodicKinds = EntityKindRegistry.All
        .OfType<IPlayableVideoKindDefinition>().Where(definition => definition.IsEpisodic)
        .Select(definition => definition.Kind).ToFrozenSet();

    private static readonly IReadOnlyDictionary<CollectionRuleField, IReadOnlySet<EntityKind>> Targets =
        new Dictionary<CollectionRuleField, IReadOnlySet<EntityKind>> {
            [CollectionRuleField.FileSize] = Kinds(PlayableVideoKinds, EntityKind.Image, EntityKind.AudioTrack),
            [CollectionRuleField.Duration] = Kinds(PlayableVideoKinds, EntityKind.AudioTrack),
            [CollectionRuleField.Height] = Kinds(EntityKind.Image),
            [CollectionRuleField.Width] = Kinds(EntityKind.Image),
            [CollectionRuleField.Codec] = PlayableVideoKinds,
            [CollectionRuleField.BitRate] = Kinds(EntityKind.AudioTrack),
            [CollectionRuleField.BitRateLegacy] = Kinds(EntityKind.AudioTrack),
            [CollectionRuleField.Channels] = Kinds(EntityKind.AudioTrack),
            [CollectionRuleField.SampleRate] = Kinds(EntityKind.AudioTrack),
            [CollectionRuleField.SampleRateLegacy] = Kinds(EntityKind.AudioTrack),
            [CollectionRuleField.AccessCount] = Kinds(PlayableVideoKinds, EntityKind.AudioTrack),
            [CollectionRuleField.SkipCount] = Kinds(PlayableVideoKinds, EntityKind.AudioTrack),
            [CollectionRuleField.Resolution] = PlayableVideoKinds,
            [CollectionRuleField.VideoSeriesId] = EpisodicKinds,
            [CollectionRuleField.LibraryRootId] = CollectionKinds
                .Where(kind => EntityKindRegistry.Describe(kind).LibraryVisibility.Mode != EntityLibraryVisibilityMode.Unscoped)
                .ToFrozenSet(),
            [CollectionRuleField.GalleryType] = Kinds(EntityKind.Gallery),
            [CollectionRuleField.ImageCount] = Kinds(EntityKind.Gallery),
            [CollectionRuleField.Format] = Kinds(EntityKind.Image),
            [CollectionRuleField.Interactive] = PlayableVideoKinds,
        }.ToFrozenDictionary();

    /// <summary>Returns the immutable kind set applicable to a known rule field.</summary>
    /// <param name="field">Canonical collection-rule field.</param>
    /// <returns>Specific media kinds, or all collection member kinds for shared Entity fields.</returns>
    public static IReadOnlySet<EntityKind> SupportedKinds(CollectionRuleField field) =>
        Targets.TryGetValue(field, out var kinds) ? kinds : CollectionKinds;

    private static IReadOnlySet<EntityKind> Kinds(params EntityKind[] kinds) => kinds.ToFrozenSet();
    private static IReadOnlySet<EntityKind> Kinds(IEnumerable<EntityKind> initial, params EntityKind[] kinds) =>
        initial.Concat(kinds).ToFrozenSet();
}
