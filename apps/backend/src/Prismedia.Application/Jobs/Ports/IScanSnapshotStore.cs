using Prismedia.Application.Jobs.Scanning;

namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Persists the per-scan file snapshot that makes rescans incremental. A snapshot is the set of file
/// signatures a given scan job last saw under a library root, keyed by <c>(root, scan kind)</c> so
/// each scan handler keeps its own view. A later scan loads the snapshot, diffs the current
/// enumeration against it, and only does detailed work when something changed.
/// </summary>
public interface IScanSnapshotStore {
    /// <summary>
    /// Loads the file signatures stored by the last run of <paramref name="scanKind"/> for a root.
    /// Returns an empty list when no snapshot exists yet (e.g. the first scan).
    /// </summary>
    /// <param name="rootId">Library root identifier.</param>
    /// <param name="scanKind">Stable scan-kind code (the scan job type code).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<FileSignature>> LoadAsync(Guid rootId, string scanKind, CancellationToken cancellationToken);

    /// <summary>
    /// Applies a computed delta to the stored snapshot: inserts <see cref="ScanDelta.Added"/>,
    /// updates <see cref="ScanDelta.Changed"/> signatures, and deletes <see cref="ScanDelta.Removed"/>.
    /// A no-op when the delta has no changes.
    /// </summary>
    /// <param name="rootId">Library root identifier.</param>
    /// <param name="scanKind">Stable scan-kind code (the scan job type code).</param>
    /// <param name="delta">The delta to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ApplyAsync(Guid rootId, string scanKind, ScanDelta delta, CancellationToken cancellationToken);
}

/// <summary>One durable, quieted set of filesystem paths awaiting a media-family reconciliation.</summary>
public sealed record LibraryFileChangeBatch(
    IReadOnlyList<string> Paths,
    DateTimeOffset ObservedThrough) {
    /// <summary>Whether the batch contains no work.</summary>
    public bool IsEmpty => Paths.Count == 0;

    /// <summary>Canonical empty batch.</summary>
    public static LibraryFileChangeBatch Empty { get; } = new([], DateTimeOffset.MinValue);
}

/// <summary>
/// Durable coalescing ledger for filesystem watcher hints. One row per root, scan kind, and absolute
/// path survives worker restarts; completion is cutoff-guarded so a newer event cannot be erased by an
/// older scan that happened to process the same path.
/// </summary>
public interface ILibraryFileChangeIntake {
    /// <summary>Records or refreshes exact changed paths for one scan kind.</summary>
    Task RecordAsync(
        Guid rootId,
        string scanKind,
        IReadOnlyCollection<string> absolutePaths,
        CancellationToken cancellationToken);

    /// <summary>Loads the oldest bounded set of paths awaiting reconciliation.</summary>
    Task<LibraryFileChangeBatch> LoadAsync(
        Guid rootId,
        string scanKind,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Returns whether any paths remain for the root and scan kind.</summary>
    Task<bool> HasPendingAsync(Guid rootId, string scanKind, CancellationToken cancellationToken);

    /// <summary>Completes only observations at or before the loaded batch cutoff.</summary>
    Task CompleteAsync(
        Guid rootId,
        string scanKind,
        IReadOnlyCollection<string> absolutePaths,
        DateTimeOffset observedThrough,
        CancellationToken cancellationToken);
}
