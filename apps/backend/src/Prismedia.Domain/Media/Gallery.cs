using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;
using CoverSelectionDocumentCapability = Prismedia.Contracts.Entities.CoverSelectionCapability;
using GalleryMetadataDocumentCapability = Prismedia.Contracts.Entities.GalleryMetadataCapability;
using ThumbnailMetaIcons = Prismedia.Contracts.Entities.EntityThumbnailMetaIcons;

namespace Prismedia.Domain.Media;

/// <summary>Defines the gallery kind and its default credits capability.</summary>
public sealed class GalleryEntityKindDefinition() : EntityKindDefinition<Gallery>(
    EntityKind.Gallery,
    "gallery",
    "Gallery",
    "Galleries",
    EntityKindCategory.Media,
    EntityStorageShape.Folder,
    new EntityKindPresentation(
        EntityKindIcon.Gallery,
        EntityKindIcon.Gallery,
        1,
        1,
        EntityAccentHue.Green,
        EntityAccentHue.Cyan,
        EntityArtworkFit.Cover,
        usesRepresentativeChildArtwork: true),
    new EntityKindNavigation(EntityKind.Gallery, "galleries", "/galleries", "/galleries/{id}"),
    new EntityKindSearch(6),
    defaultCapabilities: static () => [new CapabilityCredits()],
    identification: new(AutoIdentifySelectorKind.Gallery),
    supportsFileDeletion: true) {
    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;

    /// <inheritdoc />
    public override IReadOnlyList<EntityStructuralCountDefinition> StructuralThumbnailCounts =>
        [new(EntityKind.Image, 1, ThumbnailMetaIcons.Image)];

    /// <inheritdoc />
    public override IReadOnlyList<Type> ProjectedCapabilityTypes =>
        [typeof(GalleryMetadataDocumentCapability), typeof(CoverSelectionDocumentCapability)];

    /// <inheritdoc />
    protected override IReadOnlyList<ContractCapability> ProjectCapabilities(
        Gallery entity,
        EntityKindProjectionContext context) =>
        [
            new GalleryMetadataDocumentCapability(entity.GalleryType),
            new CoverSelectionDocumentCapability(entity.CoverImageId)
        ];
}

/// <summary>
/// Domain model for an image gallery.
/// </summary>
public sealed class Gallery : Entity<GalleryEntityKindDefinition> {
    public Gallery(
        Guid id,
        string title,
        GalleryType galleryType,
        Guid? coverImageId,
        IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
        GalleryType = galleryType;
        CoverImageId = coverImageId;
    }

    public GalleryType GalleryType { get; private set; }
    public Guid? CoverImageId { get; private set; }
}
