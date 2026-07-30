namespace Prismedia.Domain.Entities;

/// <summary>
/// Protocol-level audio root. Concrete playable and grouping entities use the audio-track and
/// audio-library definitions; this kind remains available for generic media contracts.
/// </summary>
public sealed class AudioEntityKindDefinition() : EntityKindDefinition(
    EntityKind.Audio,
    "audio",
    "Audio",
    "Audio",
    EntityKindCategory.Media,
    EntityStorageShape.File,
    new EntityKindPresentation(
        EntityKindIcon.Audio,
        EntityKindIcon.Audio,
        1,
        1,
        EntityAccentHue.Violet,
        EntityAccentHue.Magenta),
    supportsFileDeletion: true) {
    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;
}
