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

    public DateTimeOffset? LastScannedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
