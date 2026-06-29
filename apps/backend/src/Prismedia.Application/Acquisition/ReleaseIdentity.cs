using System.Text.RegularExpressions;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Computes a stable, normalized identity for a release so the blocklist can recognize the same bad
/// release across searches even when the indexer re-orders or lightly reformats its title. The info
/// hash is authoritative when present (the same torrent is the same content regardless of title);
/// otherwise the identity falls back to the indexer name plus a whitespace-normalized title. The
/// <c>hash:</c>/<c>title:</c> prefixes keep a title that happens to look like a hash from colliding
/// with a real one. Pure and deterministic — no I/O, no wall-clock.
/// </summary>
public static partial class ReleaseIdentity {
    /// <summary>
    /// Builds the blocklist identity for a release. When <paramref name="infoHash"/> is present it is
    /// used verbatim (lowercased); otherwise the identity is <c>title:{indexer}|{normalized-title}</c>.
    /// </summary>
    public static string For(string? infoHash, string? indexerName, string? title) {
        if (!string.IsNullOrWhiteSpace(infoHash)) {
            return $"hash:{infoHash.Trim().ToLowerInvariant()}";
        }

        return $"title:{Normalize(indexerName)}|{Normalize(title)}";
    }

    /// <summary>Lowercases, trims, and collapses internal whitespace runs to a single space.</summary>
    private static string Normalize(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        return WhitespaceRuns().Replace(value.Trim().ToLowerInvariant(), " ");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRuns();
}
