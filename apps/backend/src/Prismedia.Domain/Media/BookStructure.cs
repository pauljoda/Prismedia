using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;
using CoverSelectionDocumentCapability = Prismedia.Contracts.Entities.CoverSelectionCapability;
using ThumbnailMetaIcons = Prismedia.Contracts.Entities.EntityThumbnailMetaIcons;

namespace Prismedia.Domain.Media;

/// <summary>Defines the structural book-volume kind and shared-root construction.</summary>
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
    defaultCapabilities: static () =>
    [
        new CapabilityStats(),
        new CapabilitySource(),
        new CapabilityPosition()
    ],
    enumeratesIdentifyChildren: true,
    supportsFileDeletion: true) {
    /// <inheritdoc />
    public override IReadOnlyList<EntityStructuralCountDefinition> StructuralThumbnailCounts =>
    [
        new(EntityKind.BookChapter, 1, ThumbnailMetaIcons.Chapter),
        new(EntityKind.BookPage, 2, ThumbnailMetaIcons.Page)
    ];
}

/// <summary>Defines the structural book-chapter kind and default metadata capabilities.</summary>
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
    defaultCapabilities: static () =>
    [
        new CapabilityFingerprints(),
        new CapabilityStats(),
        new CapabilitySource(),
        new CapabilityPosition()
    ]) {
    /// <inheritdoc />
    public override IReadOnlyList<EntityStructuralCountDefinition> StructuralThumbnailCounts =>
        [new(EntityKind.BookPage, 1, ThumbnailMetaIcons.Page)];

    /// <inheritdoc />
    public override IReadOnlyList<Type> ProjectedCapabilityTypes => [typeof(CoverSelectionDocumentCapability)];

    /// <inheritdoc />
    protected override IReadOnlyList<ContractCapability> ProjectCapabilities(
        BookChapter entity,
        EntityKindProjectionContext context) =>
        [new CoverSelectionDocumentCapability(entity.CoverPageId)];
}

/// <summary>Defines the structural book-page kind and shared-root construction.</summary>
public sealed class BookPageEntityKindDefinition() : RootEntityKindDefinition<BookPage>(
    EntityKind.BookPage,
    "book-page",
    "Book Page",
    "Pages",
    EntityKindCategory.Media,
    EntityStorageShape.ArchiveEntry,
    new EntityKindPresentation(
        EntityKindIcon.Page,
        EntityKindIcon.Book,
        2,
        3,
        EntityAccentHue.Cyan,
        EntityAccentHue.Blue,
        EntityArtworkFit.Cover),
    new EntityKindNavigation(EntityKind.Book, "books", "/books", detailPathTemplate: null),
    search: null,
    static root => new BookPage(
        root.Id,
        root.Title,
        parentEntityId: root.ParentEntityId,
        sortOrder: root.SortOrder),
    defaultCapabilities: static () =>
    [
        new CapabilityFingerprints(),
        new CapabilityTechnical(),
        new CapabilitySource(),
        new CapabilityPosition()
    ]);

/// <summary>
/// Structural book volume aggregate.
/// </summary>
public sealed class BookVolume : Entity<BookVolumeEntityKindDefinition> {
    public BookVolume(
        Guid id,
        string title,
        IEnumerable<EntityCapability>? capabilities = null,
        Guid? parentEntityId = null,
        int? sortOrder = null)
        : base(id, title, capabilities, parentEntityId: parentEntityId, sortOrder: sortOrder) {
    }

}

/// <summary>
/// Structural book chapter aggregate.
/// </summary>
public sealed class BookChapter : Entity<BookChapterEntityKindDefinition> {
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

    public Guid? CoverPageId { get; private set; }
}

/// <summary>
/// Structural book page aggregate.
/// </summary>
public sealed class BookPage : Entity<BookPageEntityKindDefinition> {
    public BookPage(
        Guid id,
        string title,
        IEnumerable<EntityCapability>? capabilities = null,
        Guid? parentEntityId = null,
        int? sortOrder = null)
        : base(id, title, capabilities, parentEntityId: parentEntityId, sortOrder: sortOrder) {
    }
}
