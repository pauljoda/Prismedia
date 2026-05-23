using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class VideoSeriesDetailRow {
    public Guid EntityId { get; set; }
    public string? Status { get; set; }
}

public sealed class GalleryDetailRow {
    public Guid EntityId { get; set; }
    public GalleryType GalleryType { get; set; } = GalleryType.Virtual;
    public Guid? CoverImageEntityId { get; set; }
    public Guid? LibraryRootId { get; set; }
}

public sealed class BookDetailRow {
    public Guid EntityId { get; set; }
    public BookType BookType { get; set; } = BookType.Book;
    public Guid? CoverPageEntityId { get; set; }
    public Guid? LibraryRootId { get; set; }
}

public sealed class BookChapterDetailRow {
    public Guid EntityId { get; set; }
    public Guid? CoverPageEntityId { get; set; }
}

public sealed class AudioLibraryDetailRow {
    public Guid EntityId { get; set; }
    public Guid? LibraryRootId { get; set; }
}

public sealed class AudioTrackDetailRow {
    public Guid EntityId { get; set; }
    public string? EmbeddedArtist { get; set; }
    public string? EmbeddedAlbum { get; set; }
}
