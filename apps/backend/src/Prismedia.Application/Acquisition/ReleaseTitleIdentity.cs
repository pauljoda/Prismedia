using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Decides whether a release title names exactly the sought work, and extracts the title-adjacent year
/// scene naming uses to disambiguate same-name works ("Clifford.the.Big.Red.Dog.2019.S01" names the 2019
/// reboot, never the 2000 original). Mirrors Sonarr's clean-title comparison: digits are significant
/// (so a year or number inside a title survives the comparison), while separators, articles, and
/// diacritics do not. This feeds acceptance gates for AUTOMATIC selection only — rejected candidates
/// stay visible for manual picks, which deliberately bypass identity checks (the user is the authority).
/// </summary>
public static partial class ReleaseTitleIdentity {
    /// <summary>How a release title relates to the sought work's title.</summary>
    /// <param name="TitleMatched">True when the release's leading title tokens are exactly the target title.</param>
    /// <param name="TitleYear">The year token immediately following the matched title, when the release names one.</param>
    public sealed record Result(bool TitleMatched, int? TitleYear);

    /// <summary>A verdict used when no target is known: everything matches and no year is asserted.</summary>
    private static readonly Result NoTarget = new(true, null);

    // Words dropped from BOTH sides before comparing, per Sonarr's normalization (articles and
    // connectives that scene naming and metadata providers disagree on). "&" normalizes to "and"
    // upstream in ComparableTokens, so both spellings land here. prism-vocab: external.
    private static readonly IReadOnlySet<string> IgnoredWords = new HashSet<string>(StringComparer.Ordinal) {
        "a", "an", "the", "and", "or", "of"
    };

    // Tokens that legitimately end a title at the tail position — editions, cuts, and streaming-service
    // tags that scene naming places between the title and the quality block. Everything already known to
    // the shared metadata vocabulary (quality/source/codec/language) also ends a title. prism-vocab: external.
    private static readonly IReadOnlySet<string> BoundaryTokens = new HashSet<string>(StringComparer.Ordinal) {
        "extended", "unrated", "uncut", "remastered", "restored", "theatrical", "directors", "redux",
        "imax", "limited", "internal", "criterion", "anniversary", "edition", "hybrid", "dubbed", "subbed",
        "episode", "episodes", "collection", "hd",
        "amzn", "nf", "dsnp", "pcok", "hulu", "atvp", "hmax", "hbo", "max", "pmtp", "roku", "stan", "itunes"
    };

    /// <summary>
    /// Compares a release title's leading tokens against the sought work's title. The walk consumes the
    /// target tokens in order; once the target is fully matched, the first following token settles the
    /// verdict: a year token is the scene disambiguator (matched, year captured), a recognized
    /// metadata/unit/edition token simply ends the title (matched, no year), and any other word means the
    /// release names a longer, different title ("Dune Part Two" against "Dune"). An empty target
    /// disables the gate entirely (ad-hoc evaluations without a known work).
    /// </summary>
    public static Result Match(string releaseTitle, string? targetTitle) {
        var target = ComparableTokens(targetTitle);
        if (target.Count == 0) {
            return NoTarget;
        }

        var expectedIndex = 0;
        foreach (var token in ComparableTokens(releaseTitle)) {
            if (expectedIndex < target.Count) {
                if (string.Equals(token, target[expectedIndex], StringComparison.Ordinal)) {
                    expectedIndex++;
                    continue;
                }

                return new Result(false, null);
            }

            if (YearTokenRegex().IsMatch(token)) {
                return new Result(true, int.Parse(token, CultureInfo.InvariantCulture));
            }

            return new Result(IsBoundaryToken(token), null);
        }

        return new Result(expectedIndex == target.Count, null);
    }

    /// <summary>
    /// The comparison tokens of a title-like value: separator-normalized, diacritics folded, "&amp;"
    /// spelled out, and articles/connectives dropped — the digit-preserving clean form both sides of the
    /// identity comparison share.
    /// </summary>
    private static IReadOnlyList<string> ComparableTokens(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return [];
        }

        var tokens = new List<string>();
        foreach (var token in ReleaseTitleText.Tokens(value.Replace("&", " and "))) {
            var folded = FoldDiacritics(token);
            if (folded.Length > 0 && !IgnoredWords.Contains(folded)) {
                tokens.Add(folded);
            }
        }

        return tokens;
    }

    /// <summary>True when a tail-position token legitimately ends a title rather than extending it.</summary>
    private static bool IsBoundaryToken(string token) =>
        ReleaseTitleVocabulary.MetadataTokens.Contains(token)
        || BoundaryTokens.Contains(token)
        || ResolutionTokenRegex().IsMatch(token)
        || BitDepthTokenRegex().IsMatch(token)
        || TvUnitTokenRegex().IsMatch(token)
        || AltTvUnitTokenRegex().IsMatch(token);

    /// <summary>Strips combining marks so "Amélie" and "Amelie" compare equal (Sonarr folds diacritics the same way).</summary>
    private static string FoldDiacritics(string token) {
        if (token.IsNormalized(NormalizationForm.FormD) && !token.Any(ch => ch > 0x7F)) {
            return token;
        }

        var decomposed = token.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed) {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"^(?:19|20)\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex YearTokenRegex();

    [GeneratedRegex(@"^\d{3,4}[pi]$", RegexOptions.CultureInvariant)]
    private static partial Regex ResolutionTokenRegex();

    [GeneratedRegex(@"^\d+bit$", RegexOptions.CultureInvariant)]
    private static partial Regex BitDepthTokenRegex();

    [GeneratedRegex(@"^s\d{1,3}(?:e\d{1,4})*$", RegexOptions.CultureInvariant)]
    private static partial Regex TvUnitTokenRegex();

    [GeneratedRegex(@"^\d{1,2}x\d{2,4}$", RegexOptions.CultureInvariant)]
    private static partial Regex AltTvUnitTokenRegex();
}
