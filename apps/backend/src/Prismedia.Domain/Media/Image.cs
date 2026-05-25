using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>
/// Domain model for a single image entity.
/// </summary>
public sealed class Image : Entity {
    public Image(Guid id, string title, IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
    }

    public override EntityKind Kind => EntityKind.Image;

    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() => [];
}
