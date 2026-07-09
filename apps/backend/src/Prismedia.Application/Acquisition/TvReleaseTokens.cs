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

    [GeneratedRegex(@"(?:^|[\s._\-(\[])(?:complete|collection|all[\s._-]*seasons?)(?:\D|$)", RegexOptions.IgnoreCase)]
    private static partial Regex CompleteSeriesTokenRegex();

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

    /// <summary>The season a name declares via S01 or "Season 1" conventions (episode tokens also declare their season), or null.</summary>
    public static int? ParseSeason(string name) {
        if (ParseEpisode(name) is { } episode) {
            return episode.Season;
        }

        var match = SeasonTokenRegex().Match(name);
        return match.Success && int.TryParse(match.Groups["season"].Value, out var season) ? season : null;
    }

    /// <summary>True when the name declares a complete-series pack (which satisfies any season of that series).</summary>
    public static bool NamesCompleteSeries(string name) => CompleteSeriesTokenRegex().IsMatch(name);

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
