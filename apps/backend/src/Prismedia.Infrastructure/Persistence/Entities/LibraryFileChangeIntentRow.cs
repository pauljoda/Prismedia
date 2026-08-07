namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>
/// One coalesced filesystem watcher observation awaiting a media-family scan. The composite key makes
/// repeated writes to the same path refresh one intent instead of creating a fanout queue.
/// </summary>
public sealed class LibraryFileChangeIntentRow {
    public Guid LibraryRootId { get; set; }
    public string ScanKind { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTimeOffset ObservedAt { get; set; }
}
