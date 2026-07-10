using Prismedia.Application.Files;

namespace Prismedia.Application.Jobs.Scanning;

/// <summary>
/// Computes the change set between two file enumerations as an order-independent comparison keyed by
/// path.
/// <para>
/// A line-oriented text diff is deliberately avoided: filesystem listings have no meaningful order,
/// so inserting a single new file near the top would shift every following line and a positional diff
/// would report a cascade of false changes. Instead each path is collapsed into one dictionary entry
/// recording the signature seen on each side. Membership classifies a path as added (current only),
/// removed (previous only), or present-in-both; for present-in-both a signature comparison splits
/// changed from unchanged. The result is independent of enumeration order.
/// </para>
/// </summary>
public static class ScanSnapshotDiff {
    /// <summary>
    /// Classifies every path across the previous and current enumerations into added, removed,
    /// changed, and unchanged. Path comparison follows the host filesystem so case-distinct Unix
    /// entries remain separate while Windows paths retain case-insensitive identity; the last
    /// signature seen for a given path on each side wins.
    /// </summary>
    /// <param name="previous">Signatures recorded by the previous scan.</param>
    /// <param name="current">Signatures from the current enumeration.</param>
    /// <returns>The classified <see cref="ScanDelta"/>.</returns>
    public static ScanDelta Compute(
        IReadOnlyCollection<FileSignature> previous,
        IReadOnlyCollection<FileSignature> current) {
        // One dictionary keyed by path holds the signature from each side, so a single pass over its
        // values classifies every path with no second lookup — the map analogue of "count membership
        // in each set" but carrying the signatures needed to also detect in-place modifications.
        var byPath = new Dictionary<string, SignaturePair>(
            previous.Count, FileSystemPathComparison.Comparer);

        foreach (var entry in previous) {
            byPath[entry.Path] = new SignaturePair(entry, null);
        }

        foreach (var entry in current) {
            byPath[entry.Path] = byPath.TryGetValue(entry.Path, out var existing)
                ? existing with { Current = entry }
                : new SignaturePair(null, entry);
        }

        var added = new List<FileSignature>();
        var removed = new List<FileSignature>();
        var changed = new List<FileSignature>();
        var unchanged = 0;

        foreach (var pair in byPath.Values) {
            switch (pair) {
                case { Previous: null, Current: { } onlyCurrent }:
                    added.Add(onlyCurrent);
                    break;
                case { Previous: { } onlyPrevious, Current: null }:
                    removed.Add(onlyPrevious);
                    break;
                case { Previous: { } before, Current: { } after }:
                    if (before.SizeBytes == after.SizeBytes && before.ModifiedTicks == after.ModifiedTicks) {
                        unchanged++;
                    } else {
                        changed.Add(after);
                    }

                    break;
            }
        }

        return new ScanDelta(added, removed, changed, unchanged);
    }

    private readonly record struct SignaturePair(FileSignature? Previous, FileSignature? Current);
}
