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
    public BookFormat Format { get; set; } = BookFormat.ImageArchive;

    /// <summary>
    /// Provenance tier of the owned payload (web/retail/unknown), captured at acquisition import. The format
    /// half of the owned quality is derived from <see cref="Format"/> (never stored), so the live owned quality
    /// is <c>(SourceTier, BookQualityRank.TierFor(Format))</c>. Unknown for scanned-in books with no acquisition.
    /// </summary>
    public BookSourceTier SourceTier { get; set; } = BookSourceTier.Unknown;

    public Guid? CoverPageEntityId { get; set; }
    public Guid? LibraryRootId { get; set; }
}

public sealed class BookChapterDetailRow {
    public Guid EntityId { get; set; }
    public Guid? CoverPageEntityId { get; set; }
}

public sealed class MusicArtistDetailRow {
    public Guid EntityId { get; set; }
    public Guid? LibraryRootId { get; set; }
}

public sealed class AudioLibraryDetailRow {
    public Guid EntityId { get; set; }
    public Guid? LibraryRootId { get; set; }
}

public sealed class AudioTrackDetailRow {
    public Guid EntityId { get; set; }
    public string? EmbeddedArtist { get; set; }
    public string? EmbeddedAlbum { get; set; }

    /// <summary>
    /// Label of the album section (disc) this track belongs to, e.g. "Disc 1". Null when
    /// the album has no sections. Track ordering within a section restarts per section.
    /// </summary>
    public string? SectionLabel { get; set; }

    /// <summary>Zero-based ordinal of the track's section within the album.</summary>
    public int SectionOrder { get; set; }
}
