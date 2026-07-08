using System.Text.RegularExpressions;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Normalizes release-title text for token-ish matching. Indexers routinely use dots, underscores,
/// hyphens, or doubled separators where a human would read spaces; scoring and gates should compare
/// those titles by their words while preserving the raw title for display and download actions.
/// </summary>
public static partial class ReleaseTitleText {
    /// <summary>Lowercases a title-like value and collapses common release separators to single spaces.</summary>
    public static string Normalize(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var stripped = Apostrophes().Replace(value.Trim().ToLowerInvariant(), string.Empty);
        var separated = SeparatorRuns().Replace(stripped, " ");
        return WhitespaceRuns().Replace(separated, " ").Trim();
    }

    /// <summary>The normalized tokens in a title-like value, with common release separators treated as spaces.</summary>
    public static IReadOnlyList<string> Tokens(string? value) {
        var normalized = Normalize(value);
        return normalized.Length == 0
            ? []
            : normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>True when <paramref name="value"/> contains <paramref name="term"/> after separator normalization.</summary>
    public static bool ContainsTerm(string? value, string? term) {
        var normalizedTerm = Normalize(term);
        return normalizedTerm.Length > 0 && Normalize(value).Contains(normalizedTerm, StringComparison.Ordinal);
    }

    /// <summary>True when any normalized token in <paramref name="value"/> equals one of <paramref name="tokens"/>.</summary>
    public static bool ContainsToken(string? value, params string[] tokens) {
        if (tokens.Length == 0) {
            return false;
        }

        var expected = new HashSet<string>(tokens.Select(Normalize).Where(token => token.Length > 0), StringComparer.Ordinal);
        return Tokens(value).Any(expected.Contains);
    }

    [GeneratedRegex(@"['’`]+", RegexOptions.CultureInvariant)]
    private static partial Regex Apostrophes();

    [GeneratedRegex(@"[\s._\-:;()\[\]{}+,]+", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorRuns();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRuns();
}
