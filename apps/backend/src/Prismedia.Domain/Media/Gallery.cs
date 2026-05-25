using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>
/// Domain model for an image gallery.
/// </summary>
public sealed class Gallery : Entity {
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

    public override EntityKind Kind => EntityKind.Gallery;
    public GalleryType GalleryType { get; private set; }
    public Guid? CoverImageId { get; private set; }

    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() => [];
}
