using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>Defines the book-author grouping kind and its shared-root behavior.</summary>
public sealed class BookAuthorEntityKindDefinition() : RootEntityKindDefinition<BookAuthor>(
    EntityKind.BookAuthor,
    "book-author",
    "Book Author",
    "Authors",
    EntityKindCategory.Media,
    EntityStorageShape.Folder,
    new EntityKindPresentation(
        EntityKindIcon.Author,
        EntityKindIcon.Book,
        2,
        3,
        EntityAccentHue.Cyan,
        EntityAccentHue.Blue),
    static root => new BookAuthor(root.Id, root.Title),
    defaultCapabilities: static () => [new CapabilityCredits()],
    enumeratesIdentifyChildren: true,
    supportsFileDeletion: true) {
    /// <inheritdoc />
    public override IReadOnlyList<RequestKindDescriptor> RequestKinds =>
    [
        new(RequestMediaKind.Author, "Author", "Authors", "book", EntityKind.Person, EntityKind.BookAuthor,
            ProfileEntityKind: EntityKind.Book, LibraryRootMediaCapability: LibraryRootMediaCapability.ScanBooks,
            ReviewSelection: RequestReviewSelection.DirectChildren,
            IsContainer: true, ChildKind: RequestMediaKind.Book, Committable: true,
            AcquisitionKind: EntityKind.Book)
    ];
}

/// <summary>
/// Domain model for a book author: a folder-backed grouping that gathers an author's
/// books (<see cref="Book"/> children) under one heading, mirroring how a
/// <see cref="MusicArtist"/> groups albums. Carries its own metadata; the books
/// themselves are parented to the author.
/// </summary>
public sealed class BookAuthor : Entity<BookAuthorEntityKindDefinition> {
    /// <summary>
    /// Creates a book author grouping.
    /// </summary>
    /// <param name="id">Stable entity identity.</param>
    /// <param name="title">Display name of the author.</param>
    /// <param name="capabilities">Optional initial capability set.</param>
    public BookAuthor(Guid id, string title, IEnumerable<EntityCapability>? capabilities = null)
        : base(id, title, capabilities) {
    }

}
