using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>Defines the image kind and its shared-root construction.</summary>
public sealed class ImageEntityKindDefinition() : RootEntityKindDefinition<Image>(
    EntityKind.Image,
    "image",
    "Image",
    "Images",
    EntityKindCategory.Media,
    EntityStorageShape.File,
    new EntityKindPresentation(
        EntityKindIcon.Image,
        EntityKindIcon.Image,
        1,
        1,
        EntityAccentHue.Blue,
        EntityAccentHue.Violet,
        EntityArtworkFit.Cover),
    new EntityKindNavigation(EntityKind.Image, "images", "/images", "/images/{id}"),
    new EntityKindSearch(8),
    static root => new Image(root.Id, root.Title),
    behavior: new EntityKindBehavior(
        identification: new(AutoIdentifySelectorKind.Image),
        processing: new EntityProcessingPolicy(
            assetFamily: GeneratedAssetFamily.Image,
            fingerprintJobType: JobType.FingerprintImage,
            previewJobType: JobType.GenerateImageThumbnail,
            previewRequiresAutomaticGeneration: true,
            generatedFileRoles: [EntityFileRole.Thumbnail, EntityFileRole.Preview]),
        libraryVisibility: EntityLibraryVisibilityPolicy.AncestorRoot,
        supportsFileDeletion: true),
    defaultCapabilities: static () => [new CapabilityConsumption()]) {
    /// <inheritdoc />
    public override EntityProgressTopology ProgressTopology => EntityProgressTopology.None;

    /// <inheritdoc />
    public override EntityStructurePolicy StructurePolicy { get; } = EntityStructurePolicy.RootOrChildOf(EntityKind.Gallery);

    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;
}

/// <summary>
/// Domain model for a single image entity.
/// </summary>
public sealed class Image : Entity<ImageEntityKindDefinition> {
    public Image(Guid id, string title, IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
    }
}
