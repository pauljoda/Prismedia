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

    /// <summary>
    /// Returns the authored title followed by its exact numeric identities. Numeric identities include
    /// the persisted absolute episode position and the number from an exact generic title such as
    /// <c>Episode 1316</c>. Season-relative bare numbers are deliberately excluded.
    /// </summary>
    public static TvEpisodeIdentifierSet Create(string? providerTitle, int? absoluteEpisodeNumber) {
        var identifiers = new List<string>();
        var numericIdentifiers = new List<string>();
        Add(identifiers, providerTitle);

        if (absoluteEpisodeNumber is > 0) {
            AddNumeric(absoluteEpisodeNumber.Value);
        }

        var genericTitle = GenericEpisodeTitleRegex().Match(providerTitle ?? string.Empty);
        if (genericTitle.Success
            && int.TryParse(genericTitle.Groups["number"].Value, CultureInfo.InvariantCulture, out var titleNumber)
            && titleNumber > 0) {
            AddNumeric(titleNumber);
        }

        return new TvEpisodeIdentifierSet(identifiers, numericIdentifiers, providerTitle);

        void AddNumeric(int number) {
            var value = number.ToString(CultureInfo.InvariantCulture);
            Add(numericIdentifiers, value);
            Add(identifiers, value);
        }
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
        All.Any(identifier => ReleaseTitleIdentity.ContainsMeaningfulRun(candidate, identifier));

    /// <summary>Whether an absolute or generic episode-number identity occurs in the candidate.</summary>
    public bool MatchesNumeric(string candidate) =>
        Numeric.Any(identifier => ReleaseTitleIdentity.ContainsMeaningfulRun(candidate, identifier));

    /// <summary>Whether the provider-authored title occurs in the candidate.</summary>
    public bool MatchesProviderTitle(string candidate) =>
        ReleaseTitleIdentity.ContainsMeaningfulRun(candidate, ProviderTitle);
}
