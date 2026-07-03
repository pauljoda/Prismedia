using System.Text.RegularExpressions;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Parses the TV unit a release or file name declares — the SxxEyy / 1x05 episode conventions and the
/// S01 / "Season 1" season-pack conventions. One decode site shared by the TV decision engine (does
/// this release name the unit we seek?) and the TV import planner (which episode is this file?).
/// </summary>
public static partial class TvReleaseTokens {
    [GeneratedRegex(@"(?:^|[\s._\-(\[])[Ss](?<season>\d{1,3})[\s._-]*[Ee](?<episode>\d{1,4})(?:\D|$)")]
    private static partial Regex EpisodeTokenRegex();

    [GeneratedRegex(@"(?:^|[\s._\-(\[])(?<season>\d{1,2})x(?<episode>\d{2,4})(?:\D|$)", RegexOptions.IgnoreCase)]
    private static partial Regex AltEpisodeTokenRegex();

    [GeneratedRegex(@"(?:^|[\s._\-(\[])(?:[Ss](?<season>\d{1,3})|Season[\s._-]*(?<season>\d{1,3}))(?:\D|$)", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonTokenRegex();

    [GeneratedRegex(@"(?:^|[\s._\-(\[])(?:complete|collection|all[\s._-]*seasons?)(?:\D|$)", RegexOptions.IgnoreCase)]
    private static partial Regex CompleteSeriesTokenRegex();

    /// <summary>The (season, episode) a name declares via SxxEyy or 1x05 conventions, or null when it names none.</summary>
    public static (int Season, int Episode)? ParseEpisode(string name) {
        var match = EpisodeTokenRegex().Match(name);
        if (!match.Success) {
            match = AltEpisodeTokenRegex().Match(name);
        }

        return match.Success
            && int.TryParse(match.Groups["season"].Value, out var season)
            && int.TryParse(match.Groups["episode"].Value, out var episode)
                ? (season, episode)
                : null;
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
}
