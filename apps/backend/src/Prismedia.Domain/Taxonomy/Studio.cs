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
    static root => new Studio(root.Id, root.Title));

/// <summary>
/// Domain model for studio, publisher, label, or production-group taxonomy entities.
/// </summary>
public sealed class Studio : Entity<StudioEntityKindDefinition> {
    public Studio(Guid id, string title, IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
    }
}
