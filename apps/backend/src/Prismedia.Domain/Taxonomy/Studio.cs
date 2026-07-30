using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Taxonomy;

/// <summary>Defines the studio taxonomy kind and shared-root construction.</summary>
public sealed class StudioEntityKindDefinition() : RootEntityKindDefinition<Studio>(
    EntityKind.Studio,
    "studio",
    "Studio",
    "Studios",
    EntityKindCategory.Taxonomy,
    EntityStorageShape.None,
    new EntityKindPresentation(
        EntityKindIcon.Studio,
        EntityKindIcon.Studio,
        21,
        9,
        EntityAccentHue.Orange,
        EntityAccentHue.Magenta,
        EntityArtworkFit.Contain),
    static root => new Studio(root.Id, root.Title),
    supportsManualManagement: true);

/// <summary>
/// Domain model for studio, publisher, label, or production-group taxonomy entities.
/// </summary>
public sealed class Studio : Entity<StudioEntityKindDefinition> {
    public Studio(Guid id, string title, IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
    }
}
