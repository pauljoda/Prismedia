using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>
/// Structural book volume aggregate.
/// </summary>
public sealed class BookVolume : Entity {
    public BookVolume(
        Guid id,
        string title,
        IEnumerable<EntityCapability>? capabilities = null,
        Guid? parentEntityId = null,
        int? sortOrder = null)
        : base(id, title, capabilities, parentEntityId: parentEntityId, sortOrder: sortOrder) {
    }

    public override EntityKind Kind => EntityKind.BookVolume;

    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() =>
    [
        new CapabilityFiles(),
        new CapabilityStats(),
        new CapabilitySource(),
        new CapabilityPosition()
    ];
}

/// <summary>
/// Structural book chapter aggregate.
/// </summary>
public sealed class BookChapter : Entity {
    public BookChapter(
        Guid id,
        string title,
        Guid? coverPageId,
        IEnumerable<EntityCapability>? capabilities = null,
        Guid? parentEntityId = null,
        int? sortOrder = null)
        : base(id, title, capabilities, parentEntityId: parentEntityId, sortOrder: sortOrder) {
        CoverPageId = coverPageId;
    }

    public override EntityKind Kind => EntityKind.BookChapter;
    public Guid? CoverPageId { get; private set; }

    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() =>
    [
        new CapabilityFiles(),
        new CapabilityFingerprints(),
        new CapabilityStats(),
        new CapabilitySource(),
        new CapabilityPosition()
    ];
}

/// <summary>
/// Structural book page aggregate.
/// </summary>
public sealed class BookPage : Entity {
    public BookPage(
        Guid id,
        string title,
        IEnumerable<EntityCapability>? capabilities = null,
        Guid? parentEntityId = null,
        int? sortOrder = null)
        : base(id, title, capabilities, parentEntityId: parentEntityId, sortOrder: sortOrder) {
    }

    public override EntityKind Kind => EntityKind.BookPage;

    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() =>
    [
        new CapabilityFiles(),
        new CapabilityFingerprints(),
        new CapabilityTechnical(),
        new CapabilitySource(),
        new CapabilityPosition()
    ];
}
