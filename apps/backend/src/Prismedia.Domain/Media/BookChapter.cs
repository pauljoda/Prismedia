using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>Defines a navigation chapter within a published book.</summary>
public sealed class BookChapterEntityKindDefinition() : EntityKindDefinition<BookChapter>(
    EntityKind.BookChapter,
    "book-chapter",
    "Book Chapter",
    "Chapters",
    EntityKindCategory.Media,
    EntityStorageShape.None,
    new EntityKindPresentation(
        EntityKindIcon.Chapter,
        EntityKindIcon.Book,
        2,
        3,
        EntityAccentHue.Cyan,
        EntityAccentHue.Blue,
        EntityArtworkFit.Cover,
        usesRepresentativeChildArtwork: true),
    new EntityKindNavigation(
        EntityKind.Book,
        "books",
        "/books",
        "/books/{parentId}/chapters/{id}",
        EntityKind.Book),
    search: null,
    behavior: new EntityKindBehavior(
        identification: new(allowsProviderMetadata: false),
        engagement: new(EntityEngagementMode.Reading),
        libraryVisibility: EntityLibraryVisibilityPolicy.AncestorRoot),
    defaultCapabilities: static () =>
    [
        new CapabilityFingerprints(),
        new CapabilityStats(),
        new CapabilitySource(),
        new CapabilityPosition(),
        new CapabilityConsumption()
    ]) {
    /// <inheritdoc />
    public override EntityProgressTopology ProgressTopology => EntityProgressTopology.Work(EntityKind.Book);

    /// <inheritdoc />
    public override EntityStructurePolicy StructurePolicy { get; } =
        EntityStructurePolicy.ChildOf(EntityKind.Book, EntityKind.BookVolume);
}

/// <summary>Navigation marker within a published book.</summary>
public sealed class BookChapter : Entity<BookChapterEntityKindDefinition> {
    /// <summary>Creates a chapter under a book or optional volume.</summary>
    public BookChapter(
        Guid id,
        string title,
        IEnumerable<EntityCapability>? capabilities = null,
        Guid? parentEntityId = null,
        int? sortOrder = null)
        : base(id, title, capabilities, parentEntityId: parentEntityId, sortOrder: sortOrder) {
    }
}
