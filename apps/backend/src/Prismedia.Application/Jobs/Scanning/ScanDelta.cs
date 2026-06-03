namespace Prismedia.Application.Jobs.Scanning;

/// <summary>
/// The difference between a scan's previous file snapshot and its current enumeration, classified by
/// path membership and content signature. Drives the incremental rescan: when nothing was added,
/// removed, or changed the detailed scan body can be skipped entirely.
/// </summary>
/// <param name="Added">Files present now but absent from the previous snapshot.</param>
/// <param name="Removed">Files in the previous snapshot that are no longer present.</param>
/// <param name="Changed">Files present in both snapshots whose size or modified time differs.</param>
/// <param name="UnchangedCount">Number of files present in both snapshots with an identical signature.</param>
public sealed record ScanDelta(
    IReadOnlyList<FileSignature> Added,
    IReadOnlyList<FileSignature> Removed,
    IReadOnlyList<FileSignature> Changed,
    int UnchangedCount) {
    /// <summary>An empty delta — nothing on either side.</summary>
    public static ScanDelta Empty { get; } = new([], [], [], 0);

    /// <summary>True when any file was added, removed, or changed since the previous snapshot.</summary>
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0 || Changed.Count > 0;
}
