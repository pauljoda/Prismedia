using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;
using CoverSelectionDocumentCapability = Prismedia.Contracts.Entities.CoverSelectionCapability;
using GalleryMetadataDocumentCapability = Prismedia.Contracts.Entities.GalleryMetadataCapability;

namespace Prismedia.Domain.Media;

/// <summary>Defines the gallery kind and its default credits capability.</summary>
public sealed class GalleryEntityKindDefinition() : EntityKindDefinition<Gallery>(
    EntityKind.Gallery,
    "gallery",
    "Gallery",
    "Galleries",
    EntityKindCategory.Media,
    EntityStorageShape.Folder,
    defaultCapabilities: static () => [new CapabilityCredits()],
    supportsFileDeletion: true,
    autoIdentifySelector: AutoIdentifySelectorKind.Gallery) {
    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;

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
