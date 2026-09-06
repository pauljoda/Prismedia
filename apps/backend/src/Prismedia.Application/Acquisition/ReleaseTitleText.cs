using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Normalizes release-title text for token-ish matching. Indexers routinely use dots, underscores,
/// hyphens, or doubled separators where a human would read spaces; scoring and gates should compare
/// those titles by their words while preserving the raw title for display and download actions.
/// </summary>
public static partial class ReleaseTitleText {
    /// <summary>Removes decomposable Latin accents for search spelling variants without changing non-Latin marks.</summary>
    public static string FoldLatinAccents(string value) {
        if (value.All(character => character <= 0x7f)) return value;
        var result = new StringBuilder(value.Length);
        var latinLetter = false;
        foreach (var character in value.Normalize(NormalizationForm.FormD)) {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) {
                if (!latinLetter) result.Append(character);
                continue;
            }
            latinLetter = character is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
            result.Append(character);
        }
        return result.ToString().Normalize(NormalizationForm.FormC);
    }

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

    /// <summary>Separates sentence punctuation around words for work and episode identity comparisons.</summary>
    internal static string NormalizeIdentityPunctuation(string value) => WordPunctuation().Replace(value, " ");

    /// <summary>Reads raw tokens with their end offsets using the same separator vocabulary as normalization.</summary>
    internal static IEnumerable<(string Value, int End)> TokenSegments(string value) {
        var start = 0;
        foreach (Match separator in SeparatorRuns().Matches(value)) {
            if (separator.Index > start) yield return (value[start..separator.Index], separator.Index);
            start = separator.Index + separator.Length;
        }
        if (start < value.Length) yield return (value[start..], value.Length);
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

    // Work/episode identity tolerates sentence punctuation, but strict audio-track reconciliation
    // retains it. Requiring adjacent words also preserves symbol-only names such as "!!!".
    [GeneratedRegex(@"(?<=[\p{L}\p{N}])[!?""“”]+|[!?""“”]+(?=[\p{L}\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex WordPunctuation();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRuns();
}
