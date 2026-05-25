using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Taxonomy;

/// <summary>
/// Domain model for studio, publisher, label, or production-group taxonomy entities.
/// </summary>
public sealed class Studio : Entity {
    public Studio(Guid id, string title, IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
    }

    public override EntityKind Kind => EntityKind.Studio;

    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() => [];
}
