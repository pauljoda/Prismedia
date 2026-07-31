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
    manualAcquisition: EntityManualAcquisitionPolicy.None,
    processing: new EntityProcessingPolicy(
        fingerprintJobType: JobType.FingerprintImage,
        previewJobType: JobType.GenerateImageThumbnail,
        generatedFileRoles: [EntityFileRole.Thumbnail, EntityFileRole.Preview]),
    identification: new(AutoIdentifySelectorKind.Image),
    supportsFileDeletion: true) {
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
