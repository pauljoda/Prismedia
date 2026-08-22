using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using BookMetadataDocumentCapability = Prismedia.Contracts.Entities.BookMetadataCapability;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;
using ThumbnailMetaIcons = Prismedia.Contracts.Entities.EntityThumbnailMetaIcons;

namespace Prismedia.Domain.Media;

/// <summary>Defines the book kind, storage behavior, and default reading capabilities.</summary>
public sealed class BookEntityKindDefinition() : EntityKindDefinition<Book>(
    EntityKind.Book,
    "book",
    "Book",
    "Books",
    EntityKindCategory.Media,
    EntityStorageShape.Archive,
    new EntityKindPresentation(
        EntityKindIcon.Book,
        EntityKindIcon.Book,
        2,
        3,
        EntityAccentHue.Cyan,
        EntityAccentHue.Blue,
        EntityArtworkFit.Cover,
        usesRepresentativeChildArtwork: true),
    new EntityKindNavigation(EntityKind.Book, "books", "/books", "/books/{id}"),
    new EntityKindSearch(7),
    new EntityKindBehavior(
        identification: new(AutoIdentifySelectorKind.Book, enumeratesChildren: true),
        manualAcquisition: EntityManualAcquisitionPolicy.UploadAndReplacement,
        engagement: new(EntityEngagementMode.Reading),
        libraryVisibility: EntityLibraryVisibilityPolicy.DirectRoot,
        supportsFileDeletion: true,
        upgradeMode: EntityUpgradeMode.AtomicBookFile),
    defaultCapabilities: static () =>
    [
        new CapabilityStats(),
        new CapabilityProgress(),
        new CapabilityConsumption()
    ]),
    IAudioPlaybackOwnerKindDefinition {
    /// <inheritdoc />
    public AudioPlaybackPolicy AudioPlaybackPolicy { get; } = new(
        EntityKind.AudioTrack,
        PreservesQueueOrder: true,
        SupportsPlaybackRate: true);

    /// <inheritdoc />
    public override EntityProgressTopology ProgressTopology => EntityProgressTopology.Work(EntityKind.Book);

    /// <inheritdoc />
    public override EntityStructurePolicy StructurePolicy { get; } =
        EntityStructurePolicy.RootOrChildOf(EntityKind.BookAuthor);

    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;

    /// <inheritdoc />
    public override IReadOnlyList<EntityStructuralCountDefinition> StructuralThumbnailCounts =>
    [
        new(EntityKind.BookVolume, 1, ThumbnailMetaIcons.Volume),
        new(EntityKind.BookChapter, 2, ThumbnailMetaIcons.Chapter)
    ];

    /// <inheritdoc />
    public override IReadOnlyList<Type> ProjectedCapabilityTypes =>
        [typeof(BookMetadataDocumentCapability)];

    /// <inheritdoc />
    public override IReadOnlyList<RequestKindDescriptor> RequestKinds =>
    [
        new(RequestMediaKind.Book, "Book", "Books", "volume", EntityKind.Book, EntityKind.Book,
            ProfileEntityKind: EntityKind.Book,
            ReviewSelection: RequestReviewSelection.DirectChildrenWhenPresent,
            IsContainer: false, ChildKind: RequestMediaKind.Book, Committable: true,
            AcquisitionKind: EntityKind.Book, BookRendition: BookRendition.Ebook),
        new(RequestMediaKind.Audiobook, "Audiobook", "Audiobooks", null, EntityKind.Book, EntityKind.Book,
            ProfileEntityKind: EntityKind.Book,
            ReviewSelection: RequestReviewSelection.Root,
            IsContainer: false, ChildKind: null, Committable: true,
            AcquisitionKind: EntityKind.Book, BookRendition: BookRendition.Audiobook,
            IsDefaultEntityRequest: false)
    ];

    /// <inheritdoc />
    public override AcquisitionProfileDefinition AcquisitionProfile { get; } = new(
        "Books",
        0,
        LibraryRootMediaCapability.ScanBooks,
        [
            EntityDateType.Publication,
            EntityDateType.DigitalRelease,
            EntityDateType.PhysicalRelease,
            EntityDateType.Release
        ],
        "{Author}/{Title} ({Year})/{Title}{ - Volume}.{ext}",
        "{Author} {Title} {Year} {ext} — folder/file layout for the book payload",
        AcquisitionNamingFamily.Book,
        AcquisitionCheckpointProtocol.Placement,
        JobType.ScanBook);

    /// <inheritdoc />
    protected override IReadOnlyList<ContractCapability> ProjectCapabilities(
        Book entity,
        EntityKindProjectionContext context) =>
        [new BookMetadataDocumentCapability(entity.BookType, entity.Format)];
}

/// <summary>
/// Domain model for a prose book published as one work. Chapters are navigation metadata within
/// that work; serialized comics use their separate series/volume/installment aggregate.
/// </summary>
public sealed class Book : Entity<BookEntityKindDefinition> {
    public Book(
        Guid id,
        string title,
        BookType bookType,
        BookFormat format = BookFormat.Epub,
        IEnumerable<EntityCapability>? capabilities = null,
        Guid? parentEntityId = null,
        int? sortOrder = null)
        : base(id, title, capabilities, parentEntityId: parentEntityId, sortOrder: sortOrder) {
        BookType = bookType;
        Format = format;
    }

    public BookType BookType { get; private set; }

    /// <summary>
    /// Physical format of the book, which selects the reader and detail presentation.
    /// </summary>
    public BookFormat Format { get; private set; }

}
