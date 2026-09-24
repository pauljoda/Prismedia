using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class LibraryRootRow {
    public Guid Id { get; set; }

    public string Path { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool Recursive { get; set; } = true;

    public bool ScanVideos { get; set; } = true;

    public bool ScanImages { get; set; } = true;

    public bool ScanAudio { get; set; } = true;

    public bool ScanBooks { get; set; }

    public bool IsNsfw { get; set; }

    /// <summary>
    /// Whether media scanned from this root participates in Auto Identify. When false, scans of this
    /// root never enqueue auto-identify jobs even while the global Auto Identify setting is on.
    /// </summary>
    public bool AutoIdentify { get; set; } = true;

    public DateTimeOffset? LastScannedAt { get; set; }

    /// <summary>User that created this root; null for roots predating multi-user or system-created ones.</summary>
    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Whether this root scans the media a kind's acquisition profile needs.</summary>
    public bool Supports(LibraryRootMediaCapability capability) => capability switch {
        LibraryRootMediaCapability.ScanBooks => ScanBooks,
        LibraryRootMediaCapability.ScanVideos => ScanVideos,
        LibraryRootMediaCapability.ScanAudio => ScanAudio,
        _ => false
    };
}
