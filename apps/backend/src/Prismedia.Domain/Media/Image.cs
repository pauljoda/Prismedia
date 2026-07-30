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
    static root => new Image(root.Id, root.Title),
    supportsFileDeletion: true,
    autoIdentifySelector: AutoIdentifySelectorKind.Image);

/// <summary>
/// Domain model for a single image entity.
/// </summary>
public sealed class Image : Entity<ImageEntityKindDefinition> {
    public Image(Guid id, string title, IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
    }
}
