namespace Prismedia.Application.Jobs.Scanning;

/// <summary>
/// Result of one root's detailed scan pass. <see cref="FailedPaths"/> lists files whose
/// persistence failed and were skipped so the rest of the root could finish. The base scan
/// handler withholds those paths from the scan snapshot — so the next scan retries exactly
/// them instead of the whole root — and then fails the job with a message naming them, after
/// everything that did succeed has been persisted.
/// </summary>
/// <param name="FailedPaths">Source paths of files the scan discovered but could not persist.</param>
public sealed record ScanRootOutcome(IReadOnlyCollection<string> FailedPaths) {
    /// <summary>A scan pass that persisted every discovered file.</summary>
    public static ScanRootOutcome Success { get; } = new([]);
}
