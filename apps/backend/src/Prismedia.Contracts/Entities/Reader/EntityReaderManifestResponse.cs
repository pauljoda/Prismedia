using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>Complete ordered image-page manifest for one readable Entity.</summary>
/// <param name="EntityId">Owning readable Entity.</param>
/// <param name="Direction">Canonical page progression direction.</param>
/// <param name="DefaultMode">Preferred initial layout.</param>
/// <param name="CoverOrdinal">Cover page ordinal when known.</param>
/// <param name="Pages">Pages in exact display order.</param>
public sealed record EntityReaderManifestResponse(
    Guid EntityId,
    PageReadingDirection Direction,
    ReaderMode DefaultMode,
    int? CoverOrdinal,
    IReadOnlyList<EntityReaderManifestPage> Pages);

/// <summary>Client-visible facts for one addressable reader page.</summary>
/// <param name="Ordinal">Zero-based page route value and display order.</param>
/// <param name="MimeType">Content type returned by the page route.</param>
/// <param name="Width">Decoded pixel width when known.</param>
/// <param name="Height">Decoded pixel height when known.</param>
/// <param name="PageType">Semantic page role.</param>
/// <param name="IsDoublePage">Whether the image is an intentional two-page spread.</param>
/// <param name="Checksum">Optional exact-byte checksum for client cache identity.</param>
public sealed record EntityReaderManifestPage(
    int Ordinal,
    string MimeType,
    int? Width,
    int? Height,
    PageType PageType,
    bool IsDoublePage,
    string? Checksum);
