using System.Globalization;
using System.Text.RegularExpressions;

namespace Prismedia.Application.Acquisition;

/// <summary>Reads absolute episode tokens without confusing technical metadata with episode numbers.</summary>
internal static partial class TvAbsoluteEpisodeTokens {
    // prism-vocab: external — filename technical-number forms, decoded only at this boundary.
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:[hx][ ._-]*26[45]|(?:aac|ddp?|eac3|ac3|dts(?:[ ._-]*hd)?)[ ._-]*\d{1,2}\.\d(?:\.\d)?|\d{1,6}[ ._-]*(?:bits?|kbps|mbps|khz|hz|kb|mb|gb|tb)|\d{3,5}\s*[x×]\s*\d{3,5}|\d{1,6}\.\d{1,2}(?:\.\d)?|\d{1,5}/\d{1,5}|(?:season|s)[ ._-]*\d{1,3})(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TechnicalNumberRegex();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?<number>\d{1,6})(?:v[1-9]\d{0,2})?(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbsoluteNumberRegex();

    /// <summary>Tests known absolute identities, optionally only at the beginning of a work-title suffix.</summary>
    public static bool Matches(string candidate, IReadOnlyList<string> identities, bool leadingOnly = false) {
        if (identities.Count == 0) return false;
        // Equal-length masking preserves token boundaries and positions for leading-identity checks.
        var content = TechnicalNumberRegex().Replace(candidate, match => new string(' ', match.Length));
        foreach (Match match in AbsoluteNumberRegex().Matches(content)) {
            if (leadingOnly && match.Index != 0) return false;
            if (int.TryParse(match.Groups["number"].Value, CultureInfo.InvariantCulture, out var number)
                && identities.Contains(number.ToString(CultureInfo.InvariantCulture))) return true;
        }
        return false;
    }
}
