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
