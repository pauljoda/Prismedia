using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>
/// Domain model for a book, comic, manga, or other page-based media item.
/// </summary>
public sealed class Book : Entity {
    public Book(
        Guid id,
        string title,
        BookType bookType,
        Guid? coverPageId,
        IEnumerable<EntityCapability>? capabilities = null,
        Guid? parentEntityId = null,
        int? sortOrder = null)
        : base(id, title, capabilities, parentEntityId: parentEntityId, sortOrder: sortOrder) {
        BookType = bookType;
        CoverPageId = coverPageId;
    }

    public override EntityKind Kind => EntityKind.Book;
    public BookType BookType { get; private set; }
    public Guid? CoverPageId { get; private set; }

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
            "page",
            normalizedPageIndex,
            normalizedPageCount,
            readerMode.ToCode(),
            DateTimeOffset.UtcNow);
    }

    /// <summary>Marks the book as completed at the supplied time.</summary>
    public void MarkCompleted(DateTimeOffset completedAt) {
        var progress = RequireCapability<CapabilityProgress>();
        progress.MarkCompleted(completedAt);
    }

    protected override IEnumerable<EntityCapability> CreateDefaultCapabilities() =>
    [
        new CapabilityRating(),
        new CapabilityLinks(),
        new CapabilityFlags(),
        new CapabilityFiles(),
        new CapabilityProgress()
    ];
}
