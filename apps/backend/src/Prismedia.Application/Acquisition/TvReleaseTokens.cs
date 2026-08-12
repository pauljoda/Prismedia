using System.Text.RegularExpressions;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Parses the TV unit a release or file name declares — the SxxEyy / 1x05 episode conventions and the
/// S01 / "Season 1" season-pack conventions. One decode site shared by the TV decision engine (does
/// this release name the unit we seek?) and the TV import planner (which episode is this file?).
/// </summary>
public static partial class TvReleaseTokens {
    [GeneratedRegex(@"(?:^|[\s._\-(\[])[Ss](?<season>\d{1,3})[\s._-]*[Ee](?<episode>\d{1,4})(?<more>(?:[\s._-]*[-Ee]+[\s._-]*\d{1,4})*)(?:\D|$)")]
    private static partial Regex EpisodeTokenRegex();

    [GeneratedRegex(@"(?:^|[\s._\-(\[])(?<season>\d{1,2})x(?<episode>\d{2,4})(?:\D|$)", RegexOptions.IgnoreCase)]
    private static partial Regex AltEpisodeTokenRegex();

    [GeneratedRegex(@"\d{1,4}", RegexOptions.CultureInvariant)]
    private static partial Regex EpisodeNumberRunRegex();

    /// <summary>Upper bound when expanding an EyyEzz/Eyy-Ezz continuation into episode numbers — a runaway range is treated as its endpoints only.</summary>
    private const int MaxEpisodeRangeExpansion = 400;

    [GeneratedRegex(@"(?:^|[\s._\-(\[])(?:[Ss](?<season>\d{1,3})|Season[\s._-]*(?<season>\d{1,3}))(?:\D|$)", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonTokenRegex();

    [GeneratedRegex(@"(?:^|[\s._\-(\[])(?:the[\s._-]+)?(?<ordinal>\d{1,3}(?:st|nd|rd|th)|first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|eleventh|twelfth|thirteenth|fourteenth|fifteenth|sixteenth|seventeenth|eighteenth|nineteenth|twentieth|thirtieth|fortieth|fiftieth|sixtieth|seventieth|eightieth|ninetieth|(?:twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety)[\s._-]+(?:first|second|third|fourth|fifth|sixth|seventh|eighth|ninth))[\s._-]+season(?:\D|$)", RegexOptions.IgnoreCase)]
    private static partial Regex OrdinalSeasonTokenRegex();

    [GeneratedRegex(@"(?:^|[\s._\-(\[])(?:complete|collection)(?:\D|$)", RegexOptions.IgnoreCase)]
    private static partial Regex CompleteSeriesTokenRegex();

    [GeneratedRegex(@"(?:^|[\s._\-(\[])(?:complete[\s._-]*series|series[\s._-]*collection|all[\s._-]*seasons?)(?:\D|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitCompleteSeriesTokenRegex();

    /// <summary>The (season, first episode) a name declares via SxxEyy or 1x05 conventions, or null when it names none.</summary>
    public static (int Season, int Episode)? ParseEpisode(string name) =>
        ParseEpisodes(name) is { } unit ? (unit.Season, unit.Episodes[0]) : null;

    /// <summary>
    /// Every episode a name declares, including multi-episode conventions: <c>S01E41E42</c> lists both,
    /// and a dashed continuation (<c>S01E01-E03</c>, <c>S01E01-03</c>) expands the range — a double-episode
    /// file fulfils a search for either of its halves. <c>1x05</c> declares its single episode. Null when
    /// the name declares no episode at all.
    /// </summary>
    public static (int Season, IReadOnlyList<int> Episodes)? ParseEpisodes(string name) {
        var match = EpisodeTokenRegex().Match(name);
        if (!match.Success) {
            var alt = AltEpisodeTokenRegex().Match(name);
            return alt.Success
                && int.TryParse(alt.Groups["season"].Value, out var altSeason)
                && int.TryParse(alt.Groups["episode"].Value, out var altEpisode)
                    ? (altSeason, [altEpisode])
                    : null;
        }

        if (!int.TryParse(match.Groups["season"].Value, out var season)
            || !int.TryParse(match.Groups["episode"].Value, out var first)) {
            return null;
        }

        var episodes = new List<int> { first };
        var more = match.Groups["more"].Value;
        foreach (Match run in EpisodeNumberRunRegex().Matches(more)) {
            if (int.TryParse(run.Value, out var episode) && !episodes.Contains(episode)) {
                episodes.Add(episode);
            }
        }

        // A single dashed continuation is a range (E01-E03 covers E02 too); an E-joined list is literal.
        if (episodes.Count == 2 && more.Contains('-')
            && episodes[1] > episodes[0] + 1 && episodes[1] - episodes[0] <= MaxEpisodeRangeExpansion) {
            episodes = [.. Enumerable.Range(episodes[0], episodes[1] - episodes[0] + 1)];
        }

        return (season, episodes);
    }

    /// <summary>
    /// The season a name declares via S01, "Season 1", "3rd Season", or English ordinal conventions
    /// such as "The Complete Third Season" (episode tokens also declare their season), or null.
    /// </summary>
    public static int? ParseSeason(string name) {
        if (ParseEpisode(name) is { } episode) {
            return episode.Season;
        }

        var match = SeasonTokenRegex().Match(name);
        if (match.Success && int.TryParse(match.Groups["season"].Value, out var season)) {
            return season;
        }

        var ordinal = OrdinalSeasonTokenRegex().Match(name);
        return ordinal.Success ? ParseOrdinal(ordinal.Groups["ordinal"].Value) : null;
    }

    private static int? ParseOrdinal(string value) {
        var digitCount = value.TakeWhile(char.IsDigit).Count();
        if (digitCount > 0 && int.TryParse(value[..digitCount], out var numeric)) {
            return numeric;
        }

        var words = value
            .Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.ToLowerInvariant())
            .ToArray();
        if (words.Length == 1) {
            return words[0] switch {
                "first" => 1,
                "second" => 2,
                "third" => 3,
                "fourth" => 4,
                "fifth" => 5,
                "sixth" => 6,
                "seventh" => 7,
                "eighth" => 8,
                "ninth" => 9,
                "tenth" => 10,
                "eleventh" => 11,
                "twelfth" => 12,
                "thirteenth" => 13,
                "fourteenth" => 14,
                "fifteenth" => 15,
                "sixteenth" => 16,
                "seventeenth" => 17,
                "eighteenth" => 18,
                "nineteenth" => 19,
                "twentieth" => 20,
                "thirtieth" => 30,
                "fortieth" => 40,
                "fiftieth" => 50,
                "sixtieth" => 60,
                "seventieth" => 70,
                "eightieth" => 80,
                "ninetieth" => 90,
                _ => null
            };
        }

        if (words.Length != 2) {
            return null;
        }

        var tens = words[0] switch {
            "twenty" => 20,
            "thirty" => 30,
            "forty" => 40,
            "fifty" => 50,
            "sixty" => 60,
            "seventy" => 70,
            "eighty" => 80,
            "ninety" => 90,
            _ => 0
        };
        var ones = words[1] switch {
            "first" => 1,
            "second" => 2,
            "third" => 3,
            "fourth" => 4,
            "fifth" => 5,
            "sixth" => 6,
            "seventh" => 7,
            "eighth" => 8,
            "ninth" => 9,
            _ => 0
        };
        return tens > 0 && ones > 0 ? tens + ones : null;
    }

    /// <summary>
    /// True when the name declares a complete-series pack (which satisfies any season of that series).
    /// A season-scoped title such as <c>S52 COMPLETE</c> means that one complete season, never the whole
    /// series; explicit <c>complete series</c>/<c>all seasons</c> wording remains authoritative.
    /// </summary>
    public static bool NamesCompleteSeries(string name) =>
        ExplicitCompleteSeriesTokenRegex().IsMatch(name)
        || (ParseSeason(name) is null && CompleteSeriesTokenRegex().IsMatch(name));

    /// <summary>
    /// The text AFTER the first episode token — where scene naming puts the episode title(s)
    /// ("Show_S01E01_MY BEST FRIEND_CLEO'S FAIR SHARE" → "_MY BEST FRIEND_CLEO'S FAIR SHARE").
    /// Null when the name declares no episode token or nothing follows it.
    /// </summary>
    public static string? EpisodeTitleTail(string name) {
        var match = EpisodeTokenRegex().Match(name);
        if (!match.Success) {
            match = AltEpisodeTokenRegex().Match(name);
        }

        if (!match.Success) {
            return null;
        }

        // The regexes consume one trailing non-digit as the end anchor; keep it in the tail.
        var end = match.Index + match.Length - (match.Length > 0 && !char.IsDigit(name[match.Index + match.Length - 1]) ? 1 : 0);
        var tail = name[end..];
        return string.IsNullOrWhiteSpace(tail) ? null : tail;
    }
}
