using System.Globalization;
using System.Text.RegularExpressions;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Builds the equivalent provider-authored identities of one TV episode. Search-result titles and
/// downloaded file names use this same set so archive/anime absolute numbering is interpreted
/// consistently on both sides of the download boundary.
/// </summary>
public static partial class TvEpisodeIdentifiers {
    [GeneratedRegex(@"^\s*episode[\s._-]*0*(?<number>\d{1,6})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericEpisodeTitleRegex();

    [GeneratedRegex(@"^\s*(?:s\d{1,3}[\s._-]*e\d{1,6}|\d{1,3}x\d{1,6}|season[\s._-]*\d{1,3}[\s._-]*episode[\s._-]*\d{1,6})\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StructuredEpisodeTitleRegex();

    /// <summary>Recognizes an unqualified episode-number label, which alone proves no numbering system.</summary>
    public static bool IsGenericTitle(string? title) =>
        GenericEpisodeTitleRegex().IsMatch(title ?? string.Empty)
        || StructuredEpisodeTitleRegex().IsMatch(title ?? string.Empty);

    /// <summary>
    /// Returns a descriptive provider title and independently supplied absolute position. Generic
    /// labels such as <c>Episode 1</c> repeat across seasons and cannot establish absolute identity.
    /// A generic title is usable only when its number agrees with the supplied absolute position.
    /// </summary>
    public static TvEpisodeIdentifierSet Create(string? providerTitle, int? absoluteEpisodeNumber) {
        var identifiers = new List<string>();
        var numericIdentifiers = new List<string>();
        var genericTitle = GenericEpisodeTitleRegex().Match(providerTitle ?? string.Empty);
        var usableTitle = !IsGenericTitle(providerTitle)
            || (genericTitle.Success
                && int.TryParse(genericTitle.Groups["number"].Value, CultureInfo.InvariantCulture, out var titleNumber)
                && titleNumber > 0 && titleNumber == absoluteEpisodeNumber)
            ? providerTitle : null;
        Add(identifiers, usableTitle);

        if (absoluteEpisodeNumber is > 0) {
            var value = absoluteEpisodeNumber.Value.ToString(CultureInfo.InvariantCulture);
            Add(numericIdentifiers, value);
            Add(identifiers, value);
        }

        return new TvEpisodeIdentifierSet(identifiers, numericIdentifiers, usableTitle);
    }

    private static void Add(ICollection<string> values, string? value) {
        if (!string.IsNullOrWhiteSpace(value)
            && !values.Contains(value.Trim(), StringComparer.Ordinal)) {
            values.Add(value.Trim());
        }
    }
}

/// <summary>The provider-title and numeric identities that can positively identify one TV episode.</summary>
public sealed record TvEpisodeIdentifierSet(
    IReadOnlyList<string> All,
    IReadOnlyList<string> Numeric,
    string? ProviderTitle) {
    /// <summary>Whether any episode identity occurs as a normalized contiguous token run.</summary>
    public bool Matches(string candidate) =>
        MatchesProviderTitle(candidate) || MatchesNumeric(candidate);

    /// <summary>Whether an independently supplied absolute episode identity occurs in the candidate.</summary>
    public bool MatchesNumeric(string candidate) =>
        TvAbsoluteEpisodeTokens.Matches(candidate, Numeric);

    /// <summary>Whether the provider-authored title occurs in the candidate.</summary>
    public bool MatchesProviderTitle(string candidate) =>
        ReleaseTitleIdentity.ContainsMeaningfulRun(candidate, ProviderTitle);
}
