namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>
/// One file remembered by a scan, used to make later rescans incremental. Rows are keyed by
/// <c>(library root, scan kind, path)</c> so each scan job (video, gallery, audio, book) keeps its
/// own snapshot of the files it last saw under a root. A rescan diffs the current enumeration against
/// these rows and only does detailed work for what changed. Deleting a library root cascades and
/// removes its snapshot.
/// </summary>
public sealed class ScannedFileRow {
    /// <summary>Owning library root.</summary>
    public Guid LibraryRootId { get; set; }

    /// <summary>Scan-kind code (the scan job type code) that recorded this file.</summary>
    public string ScanKind { get; set; } = string.Empty;

    /// <summary>Absolute file path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>File length in bytes at last scan.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Last-write time in UTC ticks at last scan.</summary>
    public long ModifiedTicks { get; set; }

    /// <summary>When this row was last written.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
