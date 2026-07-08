using System.Text.RegularExpressions;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Computes a stable, normalized identity for a release so the blocklist can recognize the same bad
/// release across searches even when the indexer re-orders or lightly reformats its title. The info
/// hash is authoritative when present (the same torrent is the same content regardless of title);
/// otherwise the identity falls back to the indexer name plus a separator-normalized title. The
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

        return $"title:{ReleaseTitleText.Normalize(indexerName)}|{ReleaseTitleText.Normalize(title)}";
    }

    /// <summary>
    /// True when a release matches any listed identity, including the legacy whitespace-only title
    /// fallback used before separator normalization was introduced.
    /// </summary>
    public static bool IsListed(IReadOnlySet<string>? identities, string? infoHash, string? indexerName, string? title) =>
        identities is { Count: > 0 } && CandidateKeys(infoHash, indexerName, title).Any(identities.Contains);

    /// <summary>True when <paramref name="identity"/> is one of the current or legacy identities for the release.</summary>
    public static bool Matches(string? identity, string? infoHash, string? indexerName, string? title) =>
        !string.IsNullOrWhiteSpace(identity)
        && CandidateKeys(infoHash, indexerName, title).Contains(identity, StringComparer.Ordinal);

    private static IEnumerable<string> CandidateKeys(string? infoHash, string? indexerName, string? title) {
        var current = For(infoHash, indexerName, title);
        yield return current;

        var legacy = LegacyFor(infoHash, indexerName, title);
        if (!string.Equals(current, legacy, StringComparison.Ordinal)) {
            yield return legacy;
        }
    }

    private static string LegacyFor(string? infoHash, string? indexerName, string? title) {
        if (!string.IsNullOrWhiteSpace(infoHash)) {
            return $"hash:{infoHash.Trim().ToLowerInvariant()}";
        }

        return $"title:{NormalizeWhitespaceOnly(indexerName)}|{NormalizeWhitespaceOnly(title)}";
    }

    private static string NormalizeWhitespaceOnly(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        return WhitespaceRuns().Replace(value.Trim().ToLowerInvariant(), " ");
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRuns();
}
