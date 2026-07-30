using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Taxonomy;

/// <summary>Defines the tag taxonomy kind.</summary>
public sealed class TagEntityKindDefinition() : EntityKindDefinition<Tag>(
    EntityKind.Tag,
    "tag",
    "Tag",
    "Tags",
    EntityKindCategory.Taxonomy,
    EntityStorageShape.None);

/// <summary>
/// Domain model for a tag taxonomy entity.
/// </summary>
public sealed class Tag : Entity<TagEntityKindDefinition> {
    public Tag(Guid id, string title, bool ignoreAutoTag = false, IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
        IgnoreAutoTag = ignoreAutoTag;
    }

    public bool IgnoreAutoTag { get; private set; }
}
