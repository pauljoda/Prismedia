using System.Text.RegularExpressions;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Scores how closely an indexer release title names the work being searched. This is intentionally a
/// ranking signal, not an acceptance gate: a loose match still appears for manual review, but auto-pick
/// prefers the release whose content-title tokens are closest to the requested title.
/// </summary>
public static partial class ReleaseTitleRelevance {
    private const double CoverageBoost = 1_000_000;
    private const double OrderedPhraseBoost = 500_000;
    private const double ExactContentBoost = 1_500_000;
    private const double ExtraTokenPenalty = 750_000;
    private const double MissingTokenPenalty = 1_000_000;

    /// <summary>
    /// Returns a signed relevance contribution. Exact title-content matches are strongly positive; releases
    /// that include all target words plus extra content words are still positive, but lower; missing target
    /// words become negative.
    /// </summary>
    public static double Score(IndexerRelease release, BookAcquisitionRules rules) {
        var targetTokens = ReleaseTitleText.Tokens(rules.TargetTitle).ToArray();
        if (targetTokens.Length == 0) {
            return 0;
        }

        var targetSet = targetTokens.ToHashSet(StringComparer.Ordinal);
        var candidateTokens = ContentTokens(release.Title, rules, targetSet).ToArray();
        if (candidateTokens.Length == 0) {
            return -targetSet.Count * MissingTokenPenalty;
        }

        var candidateSet = candidateTokens.ToHashSet(StringComparer.Ordinal);
        var matched = targetSet.Count(candidateSet.Contains);
        var missing = targetSet.Count - matched;
        var extra = candidateTokens.Count(token => !targetSet.Contains(token));
        var coverage = matched / (double)targetSet.Count;
        var ordered = ContainsOrdered(targetTokens, candidateTokens);

        return coverage * CoverageBoost
            + (ordered ? OrderedPhraseBoost : 0)
            + (missing == 0 && extra == 0 ? ExactContentBoost : 0)
            - (missing * MissingTokenPenalty)
            - (extra * ExtraTokenPenalty);
    }

    private static IEnumerable<string> ContentTokens(string title, BookAcquisitionRules rules, IReadOnlySet<string> targetTokens) {
        var releaseGroupTokens = ReleaseTitleText.Tokens(ReleaseGroupDetection.Detect(title)).ToHashSet(StringComparer.Ordinal);
        foreach (var token in ReleaseTitleText.Tokens(title)) {
            if (targetTokens.Contains(token)) {
                yield return token;
                continue;
            }

            if (!IsMetadataToken(token, rules, releaseGroupTokens)) {
                yield return token;
            }
        }
    }

    private static bool IsMetadataToken(string token, BookAcquisitionRules rules, IReadOnlySet<string> releaseGroupTokens) =>
        ReleaseTitleVocabulary.MetadataTokens.Contains(token)
        || releaseGroupTokens.Contains(token)
        || YearTokenRegex().IsMatch(token)
        || ResolutionTokenRegex().IsMatch(token)
        || BitDepthTokenRegex().IsMatch(token)
        || IsTvUnitToken(token, rules);

    private static bool IsTvUnitToken(string token, BookAcquisitionRules rules) {
        if (rules.SeasonNumber is not { } season) {
            return false;
        }

        if (token == Unit("s", season) || token == season.ToString()) {
            return true;
        }

        if (rules.EpisodeNumber is { } episode) {
            return token == Unit("e", episode)
                || token == $"s{season:00}e{episode:00}"
                || token == $"s{season}e{episode}";
        }

        return false;
    }

    private static string Unit(string prefix, int value) => $"{prefix}{value:00}";

    private static bool ContainsOrdered(IReadOnlyList<string> needle, IReadOnlyList<string> haystack) {
        if (needle.Count == 0 || needle.Count > haystack.Count) {
            return false;
        }

        for (var i = 0; i <= haystack.Count - needle.Count; i++) {
            var matched = true;
            for (var j = 0; j < needle.Count; j++) {
                if (!string.Equals(haystack[i + j], needle[j], StringComparison.Ordinal)) {
                    matched = false;
                    break;
                }
            }

            if (matched) {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"^(?:19|20)\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex YearTokenRegex();

    [GeneratedRegex(@"^\d{3,4}[pi]$", RegexOptions.CultureInvariant)]
    private static partial Regex ResolutionTokenRegex();

    [GeneratedRegex(@"^\d+bit$", RegexOptions.CultureInvariant)]
    private static partial Regex BitDepthTokenRegex();
}
