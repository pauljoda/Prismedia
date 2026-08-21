using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>Manifest-level facts for one Entity-owned ordered image-page sequence.</summary>
public sealed class EntityPageManifestRow {
    /// <summary>Owning readable Entity identifier.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Canonical page progression direction.</summary>
    public PageReadingDirection Direction { get; set; }

    /// <summary>Preferred initial reader layout.</summary>
    public ReaderMode DefaultMode { get; set; }

    /// <summary>Cover page ordinal when known.</summary>
    public int? CoverOrdinal { get; set; }

    /// <summary>Stable signature of the archive and sidecar facts used to build the manifest.</summary>
    public string SourceSignature { get; set; } = string.Empty;

    /// <summary>Last atomic manifest replacement time.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One exact, ordered archive member in an Entity page manifest.</summary>
public sealed class EntityPageEntryRow {
    /// <summary>Owning readable Entity identifier.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Zero-based stable display order.</summary>
    public int Ordinal { get; set; }

    /// <summary>Exact archive member retained from the source.</summary>
    public string ArchiveMember { get; set; } = string.Empty;

    /// <summary>Content type used when serving the page.</summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>Decoded pixel width when known.</summary>
    public int? Width { get; set; }

    /// <summary>Decoded pixel height when known.</summary>
    public int? Height { get; set; }

    /// <summary>Semantic page role.</summary>
    public PageType PageType { get; set; }

    /// <summary>Whether the image represents an intentional two-page spread.</summary>
    public bool IsDoublePage { get; set; }

    /// <summary>Optional checksum identifying the exact page bytes.</summary>
    public string? Checksum { get; set; }
}
