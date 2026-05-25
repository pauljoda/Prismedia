using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Taxonomy;

/// <summary>
/// Domain model for a tag taxonomy entity.
/// </summary>
public sealed class Tag : Entity {
    public Tag(Guid id, string title, bool ignoreAutoTag = false, IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
        IgnoreAutoTag = ignoreAutoTag;
    }

    public override EntityKind Kind => EntityKind.Tag;
    public bool IgnoreAutoTag { get; private set; }

    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() => [];
}
