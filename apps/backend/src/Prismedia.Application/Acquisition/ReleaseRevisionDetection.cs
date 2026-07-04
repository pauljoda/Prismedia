using System.Text.RegularExpressions;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Detects a release's revision number from its title tokens — the PROPER/REPACK/RERIP and anime version
/// axis, separate from the quality ladder. Pure title truth (no payload probing), mirroring the other
/// release-token detectors.
/// </summary>
/// <remarks>
/// Adapted from Sonarr's <c>QualityParser.ParseQualityModifiers</c> revision semantics. Sonarr models a
/// revision as (Version, Real, IsRepack); Prismedia collapses that to a single integer <c>revision</c>
/// because the acquisition upgrade loop only ever compares "is this a strictly higher revision", and a
/// scalar is enough to answer that. Deliberate divergences from Sonarr:
/// <list type="bullet">
/// <item>Sonarr's <c>REAL</c> tag increments a distinct <c>Real</c> axis that dominates <c>Version</c>
/// in comparisons; Prismedia does not model a separate REAL axis. A bare <c>REAL</c> is not currently
/// detected as a revision bump (scene <c>REAL</c> markers are rare and usually co-occur with a PROPER),
/// keeping the extractor simple. Numbered/anime versions and PROPER/REPACK/RERIP cover the cases the
/// upgrade loop acts on.</item>
/// <item>Prismedia takes the MAX across every recognized token rather than Sonarr's set-if-unset
/// precedence, so a title carrying both <c>PROPER</c> and <c>v3</c> resolves to the higher of the two.</item>
/// </list>
/// </remarks>
public static partial class ReleaseRevisionDetection {
    /// <summary>
    /// The revision a release title declares: <c>1</c> for a plain release; a PROPER/REPACK/RERIP token
    /// raises it to <c>2</c>; a numbered variant (<c>PROPER2</c>, <c>REPACK2</c>) raises it to that number
    /// plus one (so <c>REPACK2</c> → <c>3</c>); an anime-style version token (<c>v2</c>, <c>v3</c>) maps to
    /// that number. When several are present the highest wins. Never returns below <c>1</c>.
    /// </summary>
    /// <param name="title">The release title to inspect.</param>
    /// <returns>The detected revision (minimum <c>1</c>).</returns>
    public static int Detect(string title) {
        if (string.IsNullOrWhiteSpace(title)) {
            return 1;
        }

        var revision = 1;

        // PROPER / REPACK / RERIP, optionally numbered (PROPER2, REPACK2). A plain token means "a second,
        // more-correct release" → revision 2; an explicit number N means the N-th such re-release → N + 1
        // (REPACK2 is the third revision of the content: original, REPACK, REPACK2).
        foreach (Match match in ProperRepackRegex().Matches(title)) {
            var bump = match.Groups["n"].Success && int.TryParse(match.Groups["n"].Value, out var n) && n > 0
                ? n + 1
                : 2;
            revision = Math.Max(revision, bump);
        }

        // Anime-style version tokens: a word-bounded "vN" (" v2", ".v3.") maps directly to that version.
        foreach (Match match in AnimeVersionRegex().Matches(title)) {
            if (int.TryParse(match.Groups["v"].Value, out var version) && version > 0) {
                revision = Math.Max(revision, version);
            }
        }

        return revision;
    }

    // \bproper\b keeps "Property Brothers" and other words merely containing "proper" from matching.
    // An immediately-trailing digit (no separator) is the numbered variant, e.g. PROPER2 / REPACK2.
    [GeneratedRegex(@"\b(?:proper|repack|rerip)(?<n>\d+)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProperRepackRegex();

    // A version token must be separator-bounded on the left (start, space, dot, dash, underscore, bracket)
    // so it never fires inside a word like "revamp" or a codec token; the digits run to a word boundary.
    [GeneratedRegex(@"(?:^|[.\s_\-\[\(])v(?<v>\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnimeVersionRegex();
}

/// <summary>
/// Extracts the scene/P2P release group from a release title — the trailing <c>-GROUP</c> segment after
/// stripping bracketed site suffixes and container noise. Pure title truth, mirroring the other release
/// detectors. Groundwork for custom-format scoring; not yet wired into ranking.
/// </summary>
/// <remarks>
/// A clean, regex-based port of the recognizable behavior of Sonarr's <c>ReleaseGroupParser</c>
/// (trailing <c>-GroupName</c> with quality/source/codec/language false-positive guards, plus a
/// <c>[GroupName]</c> bracket alternate). Deliberately omits Sonarr's hard-coded exception lists for
/// oddly-named groups (e.g. <c>Fight-BB</c>, <c>Anime Time</c>): those are a maintenance liability and the
/// generic trailing-group pattern covers the overwhelming majority of releases. Anime <c>[SubGroup]</c>
/// leading brackets are also out of scope for this extractor.
/// </remarks>
public static partial class ReleaseGroupDetection {
    /// <summary>
    /// The release group named at the end of the title (<c>Show.S01E05...-NTb</c> → <c>NTb</c>), or null
    /// when the title carries no confident trailing group. Site suffixes such as <c>[rartv]</c> /
    /// <c>[eztv]</c> and any file extension are stripped first, so <c>...-GROUP[rartv].mkv</c> still
    /// resolves to <c>GROUP</c>.
    /// </summary>
    /// <param name="title">The release title to inspect.</param>
    /// <returns>The trailing release group, or null when none is confidently found.</returns>
    public static string? Detect(string? title) {
        if (string.IsNullOrWhiteSpace(title)) {
            return null;
        }

        var cleaned = title.Trim();

        // Drop a trailing file extension (…-GROUP.mkv) so it doesn't swallow the group.
        cleaned = ExtensionRegex().Replace(cleaned, string.Empty);

        // Strip trailing bracketed site-suffix noise ([rartv], [eztv], [rarbg], [ettv], …): repeat so a
        // stacked "…-GROUP[rartv][eztv]" peels down to the group.
        string previous;
        do {
            previous = cleaned;
            cleaned = SiteSuffixRegex().Replace(cleaned, string.Empty).TrimEnd();
        } while (!string.Equals(previous, cleaned, StringComparison.Ordinal));

        // Trailing -GROUP form. The negative lookahead keeps a quality/source/codec/language token that
        // happens to sit after the last hyphen (…-1080p, …-x264, …-ITA) from being read as a group.
        var trailing = TrailingGroupRegex().Match(cleaned);
        if (trailing.Success) {
            return trailing.Groups["group"].Value;
        }

        // Alternate [GROUP] bracket form for the group itself (distinct from the site suffixes stripped above).
        var bracket = BracketGroupRegex().Match(cleaned);
        return bracket.Success ? bracket.Groups["group"].Value : null;
    }

    [GeneratedRegex(@"\.[a-z0-9]{2,4}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionRegex();

    [GeneratedRegex(@"\[(?:rartv|eztv|rarbg|ettv|cttv|publichd|tgx|glodls|rarbgprox?y?)\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SiteSuffixRegex();

    // A trailing "-Group" (allowing one embedded hyphen, e.g. -Xtra-Ordinary). The lookahead after the
    // hyphen rejects common quality/source/audio/language tokens so they are never mistaken for a group —
    // including the bare halves of hyphenated source tags (WEB, DL, RIP) so "…WEB-DL" doesn't read as "-DL".
    [GeneratedRegex(
        @"-(?!(?:HDTV|SDTV|WEB|DL|RIP|WEB-?DL|WEB-?Rip|Blu-?Ray|BDRip|BRRip|Remux|DTS(?:-(?:HD|X|ES|MA))?|DDP?5|AAC|H\.?26[45]|x26[45]|HEVC|AVC|10bit|8bit|HDR|ITA|GER|FRE?|FRA|SPA|ENG?|MULTi|480p|540p|576p|720p|1080p|1440p|2160p)(?:$|[\s.\-]))(?<group>[a-z0-9]+(?:-[a-z0-9]+)?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingGroupRegex();

    [GeneratedRegex(@"\[(?<group>[a-z0-9]+)\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BracketGroupRegex();
}
