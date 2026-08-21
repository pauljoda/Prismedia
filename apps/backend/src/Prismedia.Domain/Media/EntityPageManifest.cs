using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media;

/// <summary>
/// Validated ordered-page manifest owned by one readable Entity. Pages are resources inside the
/// Entity's source artifact; they deliberately do not become child Entities.
/// </summary>
public sealed class EntityPageManifest {
    private const int MaximumSourceSignatureLength = 256;

    /// <summary>Creates a complete, contiguous manifest ready for atomic persistence.</summary>
    public EntityPageManifest(
        Guid entityId,
        PageReadingDirection direction,
        ReaderMode defaultMode,
        int? coverOrdinal,
        string sourceSignature,
        IEnumerable<EntityPageEntry> pages) {
        if (entityId == Guid.Empty) {
            throw new ArgumentException("A page manifest requires an owning Entity.", nameof(entityId));
        }
        if (!Enum.IsDefined(direction)) {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }
        if (defaultMode is not (ReaderMode.Paged or ReaderMode.Webtoon)) {
            throw new ArgumentException("An image-page sequence must use paged or webtoon mode.", nameof(defaultMode));
        }
        if (string.IsNullOrWhiteSpace(sourceSignature) || sourceSignature.Length > MaximumSourceSignatureLength) {
            throw new ArgumentException(
                $"A page manifest source signature must contain 1-{MaximumSourceSignatureLength} characters.",
                nameof(sourceSignature));
        }

        var ordered = (pages ?? throw new ArgumentNullException(nameof(pages)))
            .OrderBy(page => page.Ordinal)
            .ToArray();
        if (ordered.Length == 0) {
            throw new ArgumentException("A page manifest must contain at least one page.", nameof(pages));
        }
        for (var ordinal = 0; ordinal < ordered.Length; ordinal++) {
            if (ordered[ordinal].Ordinal != ordinal) {
                throw new ArgumentException(
                    "Page ordinals must be unique, contiguous, and zero based.",
                    nameof(pages));
            }
        }
        if (ordered.Select(page => page.ArchiveMember).Distinct(StringComparer.Ordinal).Count() != ordered.Length) {
            throw new ArgumentException("Archive members must be unique within a page manifest.", nameof(pages));
        }
        if (coverOrdinal is < 0 || coverOrdinal >= ordered.Length) {
            throw new ArgumentOutOfRangeException(nameof(coverOrdinal));
        }

        EntityId = entityId;
        Direction = direction;
        DefaultMode = defaultMode;
        CoverOrdinal = coverOrdinal;
        SourceSignature = sourceSignature;
        Pages = Array.AsReadOnly(ordered);
    }

    /// <summary>Owning readable Entity identifier.</summary>
    public Guid EntityId { get; }

    /// <summary>Canonical page progression direction.</summary>
    public PageReadingDirection Direction { get; }

    /// <summary>Preferred initial layout for clients that have no saved user mode.</summary>
    public ReaderMode DefaultMode { get; }

    /// <summary>Ordinal used as the readable work's cover, when known.</summary>
    public int? CoverOrdinal { get; }

    /// <summary>Stable signature of the source and sidecar facts from which this manifest was built.</summary>
    public string SourceSignature { get; }

    /// <summary>Pages in exact zero-based display order.</summary>
    public IReadOnlyList<EntityPageEntry> Pages { get; }
}

/// <summary>One validated image resource inside an <see cref="EntityPageManifest"/>.</summary>
public sealed class EntityPageEntry {
    private const int MaximumArchiveMemberLength = 2048;
    private const int MaximumMimeTypeLength = 255;
    private const int MaximumChecksumLength = 128;
    private static readonly HashSet<string> SafeImageMimeTypes = new(StringComparer.Ordinal) {
        "image/jpeg",
        "image/png",
        "image/apng",
        "image/gif",
        "image/webp",
        "image/avif",
        "image/bmp",
        "image/tiff"
    };

    /// <summary>Creates one page while retaining its exact archive member spelling.</summary>
    public EntityPageEntry(
        int ordinal,
        string archiveMember,
        string mimeType,
        int? width,
        int? height,
        PageType pageType,
        bool isDoublePage,
        string? checksum) {
        if (ordinal < 0) {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }
        ValidateArchiveMember(archiveMember);
        var normalizedMimeType = mimeType?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedMimeType) ||
            normalizedMimeType.Length > MaximumMimeTypeLength ||
            !SafeImageMimeTypes.Contains(normalizedMimeType)) {
            throw new ArgumentException(
                "A page MIME type must identify a supported browser-safe raster image.",
                nameof(mimeType));
        }
        if (width is < 0) {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        if (height is < 0) {
            throw new ArgumentOutOfRangeException(nameof(height));
        }
        if (!Enum.IsDefined(pageType)) {
            throw new ArgumentOutOfRangeException(nameof(pageType));
        }
        if (checksum?.Length > MaximumChecksumLength) {
            throw new ArgumentException(
                $"A page checksum cannot exceed {MaximumChecksumLength} characters.",
                nameof(checksum));
        }

        Ordinal = ordinal;
        ArchiveMember = archiveMember;
        MimeType = normalizedMimeType;
        Width = width;
        Height = height;
        PageType = pageType;
        IsDoublePage = isDoublePage;
        Checksum = string.IsNullOrWhiteSpace(checksum) ? null : checksum;
    }

    /// <summary>Zero-based stable display order.</summary>
    public int Ordinal { get; }

    /// <summary>Exact source-archive member; clients never reconstruct this value.</summary>
    public string ArchiveMember { get; }

    /// <summary>Content type emitted when serving the page.</summary>
    public string MimeType { get; }

    /// <summary>Decoded pixel width when known.</summary>
    public int? Width { get; }

    /// <summary>Decoded pixel height when known.</summary>
    public int? Height { get; }

    /// <summary>Semantic page role.</summary>
    public PageType PageType { get; }

    /// <summary>Whether clients should treat the image as an intentional two-page spread.</summary>
    public bool IsDoublePage { get; }

    /// <summary>Optional content checksum used to recognize unchanged pages.</summary>
    public string? Checksum { get; }

    private static void ValidateArchiveMember(string archiveMember) {
        if (string.IsNullOrWhiteSpace(archiveMember) || archiveMember.Length > MaximumArchiveMemberLength) {
            throw new ArgumentException(
                $"An archive member must contain 1-{MaximumArchiveMemberLength} characters.",
                nameof(archiveMember));
        }
        var hasWindowsDrivePrefix = archiveMember.Length >= 2 &&
            char.IsLetter(archiveMember[0]) && archiveMember[1] == ':';
        if (archiveMember[0] is '/' or '\\' || archiveMember[^1] is '/' or '\\' ||
            archiveMember.Contains('\0') || hasWindowsDrivePrefix) {
            throw new ArgumentException("An archive member must be a relative file entry.", nameof(archiveMember));
        }

        var segments = archiveMember.Split(['/', '\\'], StringSplitOptions.None);
        if (segments.Any(segment => segment.Length == 0 || segment is "." or "..")) {
            throw new ArgumentException(
                "An archive member cannot contain empty, current-directory, or parent-directory segments.",
                nameof(archiveMember));
        }
    }
}
