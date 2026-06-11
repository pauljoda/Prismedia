namespace Prismedia.Infrastructure.StashCompat;

/// <summary>
/// Scores how closely a search-candidate title matches the query, as a 0–1 confidence. Stash name
/// searches otherwise return candidates with no confidence, which excluded them from auto-identify
/// (which only auto-applies scored candidates); scoring lets a Stash search candidate be ranked and
/// gated the same way a first-party provider's is.
/// </summary>
public static class TitleSimilarity {
    /// <summary>
    /// Sørensen–Dice overlap of the two titles' word-token sets: symmetric, order-independent, and
    /// tolerant of extra words. Returns 0 when either title is empty.
    /// </summary>
    public static decimal Score(string? candidateTitle, string? queryTitle) {
        var left = Tokenize(candidateTitle);
        var right = Tokenize(queryTitle);
        if (left.Count == 0 || right.Count == 0) {
            return 0m;
        }

        var overlap = left.Count(right.Contains);
        return Math.Round(2m * overlap / (left.Count + right.Count), 3);
    }

    private static IReadOnlyCollection<string> Tokenize(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return [];
        }

        return value.ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => new string(token.Where(char.IsLetterOrDigit).ToArray()))
            .Where(token => token.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }
}
