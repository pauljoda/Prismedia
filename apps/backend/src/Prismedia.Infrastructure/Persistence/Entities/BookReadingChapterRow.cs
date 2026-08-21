namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>
/// One persisted readable-chapter entry for a single-file book, projected from the EPUB navigation
/// at scan time so the contents read path never has to open the source archive. Rows are replaced
/// wholesale by the chapter-mapping job whenever the source file's signature changes.
/// </summary>
public sealed class BookReadingChapterRow {
    public Guid BookId { get; set; }

    /// <summary>Stable chapter key, equal to the EPUB navigation target (content file URL plus anchor).</summary>
    public string ChapterKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Zero-based nesting depth in the source table of contents.</summary>
    public int Depth { get; set; }

    /// <summary>Zero-based display order after flattening nested navigation.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Zero-based reading-order section containing this entry, when resolvable.</summary>
    public int? SectionIndex { get; set; }

    /// <summary>Normalized whole-book start position, when section sizes are available.</summary>
    public double? StartFraction { get; set; }

    /// <summary>Normalized whole-book end position, when section sizes are available.</summary>
    public double? EndFraction { get; set; }
}

/// <summary>
/// Per-book freshness state for the persisted readable chapters and the automatic chapter map.
/// Signatures let both the scan-time staleness check and the mapping job no-op when inputs are
/// byte-for-byte unchanged.
/// </summary>
public sealed class BookContentStateRow {
    public Guid BookId { get; set; }

    /// <summary>Identity of the readable source file the persisted chapters were projected from.</summary>
    public string? SourceSignature { get; set; }

    /// <summary>Hash of every mapping input: chapter keys/titles, track ids/titles, manual pairs.</summary>
    public string? MappingSignature { get; set; }

    public DateTimeOffset RefreshedAt { get; set; }
}
