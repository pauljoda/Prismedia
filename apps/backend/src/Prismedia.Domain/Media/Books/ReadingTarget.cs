using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media.Books;

/// <summary>
/// A readable position a client can open: the exact recorded cursor, or one aligned from listening.
/// </summary>
/// <param name="PositionEntityId">Work (EPUB/PDF) or chapter Entity (paged) the position addresses.</param>
/// <param name="Unit">Unit counted by <paramref name="Index"/>.</param>
/// <param name="Index">Zero-based position within <paramref name="Total"/>.</param>
/// <param name="Total">Unit total; EPUB positions use the readable position total.</param>
/// <param name="Location">Exact opaque locator (CFI or Readium locator); null for aligned positions.</param>
/// <param name="ChapterKey">Readable chapter holding the position, when known.</param>
/// <param name="ChapterLocation">Navigation target of that chapter, when the rendition has one.</param>
/// <param name="ChapterFraction">Relative position inside that chapter (φ), when known.</param>
/// <param name="PageIndex">Zero-based page inside a paged chapter.</param>
/// <param name="Mode">Reader layout to reopen with, when known.</param>
public sealed record ReadingTarget(
    Guid PositionEntityId,
    ProgressUnit Unit,
    int Index,
    int Total,
    string? Location,
    string? ChapterKey,
    string? ChapterLocation,
    double? ChapterFraction,
    int? PageIndex,
    ReaderMode? Mode);
