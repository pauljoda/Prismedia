namespace Prismedia.Application.Acquisition;

/// <summary>
/// Validates a download's actual file list against the work an acquisition expects — the safety net the
/// release title can't provide. Scene titles are gated at search time, but a mislabeled or year-less
/// release only reveals its true contents in its files ("Clifford.the.Big.Red.Dog.S01" whose files say
/// 2019, or a "season pack" holding a different season). Torrent clients expose the full file list as
/// soon as metadata arrives, so a wrong download is caught and replaced while it is still downloading;
/// the import pipeline runs the same check as the last line of defense for usenet and fast completions.
/// Evidence-based and conservative: only a POSITIVE contradiction (a matched title naming a conflicting
/// year, or unit markers that exclude the sought season/episode) counts — absence of markers never does,
/// so terse inner file names ("01 - Pilot.mkv") can never fail a healthy download.
/// </summary>
public static class AcquisitionPayloadValidation {
    /// <summary>Year drift tolerated before a title-adjacent year counts as a contradiction (regional release-date offsets).</summary>
    private const int YearToleranceYears = 1;

    /// <summary>
    /// Finds a contradiction between a payload's file names and the expected work, or null when the
    /// payload is consistent (or carries no usable evidence). Applies to video kinds only — books and
    /// music payloads don't carry comparable naming conventions.
    /// </summary>
    /// <param name="filePaths">The download's file paths (relative paths as reported by the client or read from disk).</param>
    /// <param name="kind">The acquisition's media kind.</param>
    /// <param name="expectedTitle">The sought work's title (the series for TV units, the movie title otherwise).</param>
    /// <param name="expectedYear">The sought work's year identity (series première / movie release), when known.</param>
    /// <param name="seasonNumber">The sought season for TV units; null elsewhere.</param>
    /// <param name="episodeNumber">The sought episode for a single-episode acquisition; null elsewhere.</param>
    /// <param name="completeSeriesSelected">True when the selected release names a complete-series pack, which legitimately spans seasons.</param>
    public static string? FindConflict(
        IReadOnlyList<string> filePaths,
        Domain.Entities.EntityKind kind,
        string? expectedTitle,
        int? expectedYear,
        int? seasonNumber = null,
        int? episodeNumber = null,
        bool completeSeriesSelected = false) {
        if (filePaths.Count == 0 || !MediaQualityLadder.IsVideoKind(kind)) {
            return null;
        }

        // Every distinct path segment (folders and file stems) is title/year evidence: the torrent's
        // root folder usually repeats the release name, and per-episode files often repeat it too.
        if (expectedYear is { } year && !string.IsNullOrWhiteSpace(expectedTitle)) {
            foreach (var segment in DistinctSegments(filePaths)) {
                var identity = ReleaseTitleIdentity.Match(segment, expectedTitle);
                if (identity is { TitleMatched: true, TitleYear: { } named } && Math.Abs(named - year) > YearToleranceYears) {
                    return $"The download's files name \"{expectedTitle} {named}\", but {expectedTitle} ({year}) was expected.";
                }
            }
        }

        if (seasonNumber is { } season) {
            return FindTvUnitConflict(filePaths, season, episodeNumber, completeSeriesSelected);
        }

        return null;
    }

    /// <summary>
    /// A TV payload contradicts the sought unit only when its video files carry unit markers that
    /// EXCLUDE it: no file covers the sought season/episode while at least one names a different one.
    /// Specials (season 0) ride along in legitimate packs and never count as contrary evidence, and a
    /// complete-series selection legitimately spans every season.
    /// </summary>
    private static string? FindTvUnitConflict(
        IReadOnlyList<string> filePaths, int season, int? episodeNumber, bool completeSeriesSelected) {
        var coversSought = false;
        var contrary = default(string?);
        foreach (var path in filePaths) {
            if (!MovieImportPlanBuilder.VideoExtensions.Contains(Path.GetExtension(path))) {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(path);
            var declared = TvReleaseTokens.ParseEpisodes(name) is { } unit
                ? unit
                : TvReleaseTokens.ParseSeason(name) is { } bareSeason
                    ? (Season: bareSeason, Episodes: (IReadOnlyList<int>)[])
                    : default((int Season, IReadOnlyList<int> Episodes)?);
            if (declared is not { } markers) {
                continue;
            }

            var covers = episodeNumber is { } episode
                ? markers.Season == season && markers.Episodes.Contains(episode)
                : markers.Season == season;
            if (covers) {
                coversSought = true;
                break;
            }

            if (markers.Season != 0 && !completeSeriesSelected) {
                contrary ??= name;
            }
        }

        if (coversSought || contrary is null) {
            return null;
        }

        var sought = episodeNumber is { } soughtEpisode ? $"S{season:00}E{soughtEpisode:00}" : $"S{season:00}";
        return $"None of the download's video files cover {sought} (e.g. \"{contrary}\").";
    }

    private static IEnumerable<string> DistinctSegments(IReadOnlyList<string> filePaths) {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in filePaths) {
            foreach (var raw in path.Split('/', '\\')) {
                var segment = Path.GetExtension(raw).Length > 0 ? Path.GetFileNameWithoutExtension(raw) : raw;
                if (!string.IsNullOrWhiteSpace(segment) && seen.Add(segment)) {
                    yield return segment;
                }
            }
        }
    }
}
