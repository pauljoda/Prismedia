using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using BookMetadataDocumentCapability = Prismedia.Contracts.Entities.BookMetadataCapability;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;
using CoverSelectionDocumentCapability = Prismedia.Contracts.Entities.CoverSelectionCapability;
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
    defaultCapabilities: static () => [new CapabilityProgress(), new CapabilityPlayback()],
    identification: new(AutoIdentifySelectorKind.Book, enumeratesChildren: true),
    supportsFileDeletion: true,
    engagement: new(EntityEngagementMode.Reading)) {
    /// <inheritdoc />
    public override bool OwnsMetadataRelationships => true;

    /// <inheritdoc />
    public override IReadOnlyList<EntityStructuralCountDefinition> StructuralThumbnailCounts =>
    [
        new(EntityKind.BookVolume, 1, ThumbnailMetaIcons.Volume),
        new(EntityKind.BookChapter, 2, ThumbnailMetaIcons.Chapter),
        new(EntityKind.BookPage, 3, ThumbnailMetaIcons.Page)
    ];

    /// <inheritdoc />
    public override IReadOnlyList<Type> ProjectedCapabilityTypes =>
        [typeof(BookMetadataDocumentCapability), typeof(CoverSelectionDocumentCapability)];

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
            AcquisitionKind: EntityKind.Book, BookRendition: BookRendition.Audiobook)
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
        AcquisitionNamingFamily.Book);

    /// <inheritdoc />
    protected override IReadOnlyList<ContractCapability> ProjectCapabilities(
        Book entity,
        EntityKindProjectionContext context) =>
        [
            new BookMetadataDocumentCapability(entity.BookType, entity.Format),
            new CoverSelectionDocumentCapability(entity.CoverPageId)
        ];
}

/// <summary>
/// Domain model for a book, comic, manga, or other page-based media item.
/// </summary>
public sealed class Book : Entity<BookEntityKindDefinition> {
    public Book(
        Guid id,
        string title,
        BookType bookType,
        Guid? coverPageId,
        BookFormat format = BookFormat.ImageArchive,
        IEnumerable<EntityCapability>? capabilities = null,
        Guid? parentEntityId = null,
        int? sortOrder = null)
        : base(id, title, capabilities, parentEntityId: parentEntityId, sortOrder: sortOrder) {
        BookType = bookType;
        CoverPageId = coverPageId;
        Format = format;
    }

    public BookType BookType { get; private set; }
    public Guid? CoverPageId { get; private set; }

    /// <summary>
    /// Physical format of the book, which selects the reader and detail presentation.
    /// </summary>
    public BookFormat Format { get; private set; }

    /// <summary>
    /// Moves the reading cursor to a chapter and page.
    /// </summary>
    public void MoveReaderToChapter(Guid chapterId, int pageIndex, int pageCount, ReaderMode readerMode) {
        var progress = RequireCapability<CapabilityProgress>();
        var normalizedPageCount = Math.Max(0, pageCount);
        var normalizedPageIndex = normalizedPageCount == 0
            ? 0
            : Math.Clamp(pageIndex, 0, normalizedPageCount - 1);

        progress.MoveTo(
            chapterId,
            ProgressUnit.Page,
            normalizedPageIndex,
            normalizedPageCount,
            readerMode,
            DateTimeOffset.UtcNow);
    }

    /// <summary>Marks the book as completed at the supplied time.</summary>
    public void MarkCompleted(DateTimeOffset completedAt) {
        var progress = RequireCapability<CapabilityProgress>();
        progress.MarkCompleted(completedAt);
    }
}
