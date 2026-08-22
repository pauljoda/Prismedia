using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ThumbnailMetaIcons = Prismedia.Contracts.Entities.EntityThumbnailMetaIcons;

namespace Prismedia.Domain.Media;

/// <summary>Defines an optional structural volume within one published book.</summary>
public sealed class BookVolumeEntityKindDefinition() : RootEntityKindDefinition<BookVolume>(
    EntityKind.BookVolume,
    "book-volume",
    "Book Volume",
    "Volumes",
    EntityKindCategory.Media,
    EntityStorageShape.None,
    new EntityKindPresentation(
        EntityKindIcon.Volume,
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
        "/books/{parentId}/volumes/{id}",
        EntityKind.Book),
    search: null,
    static root => new BookVolume(
        root.Id,
        root.Title,
        parentEntityId: root.ParentEntityId,
        sortOrder: root.SortOrder),
    behavior: new EntityKindBehavior(
        identification: new(enumeratesChildren: true),
        libraryVisibility: EntityLibraryVisibilityPolicy.AncestorRoot,
        supportsFileDeletion: true),
    defaultCapabilities: static () =>
    [
        new CapabilityStats(),
        new CapabilitySource(),
        new CapabilityPosition()
    ]) {
    /// <inheritdoc />
    public override EntityProgressTopology ProgressTopology => EntityProgressTopology.None;

    /// <inheritdoc />
    public override EntityStructurePolicy StructurePolicy { get; } = EntityStructurePolicy.ChildOf(EntityKind.Book);

    /// <inheritdoc />
    public override IReadOnlyList<EntityStructuralCountDefinition> StructuralThumbnailCounts =>
        [new(EntityKind.BookChapter, 1, ThumbnailMetaIcons.Chapter)];
}

/// <summary>Optional structural volume within a published book.</summary>
public sealed class BookVolume : Entity<BookVolumeEntityKindDefinition> {
    /// <summary>Creates a volume under its book parent.</summary>
    public BookVolume(
        Guid id,
        string title,
        IEnumerable<EntityCapability>? capabilities = null,
        Guid? parentEntityId = null,
        int? sortOrder = null)
        : base(id, title, capabilities, parentEntityId: parentEntityId, sortOrder: sortOrder) {
    }
}
