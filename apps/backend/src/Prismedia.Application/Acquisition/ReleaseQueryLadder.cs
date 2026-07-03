using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Builds the ordered release-search queries for an acquisition — the drill-down rule: every query is
/// composed from the item's ancestor context (an album leads with its artist, a book with its author,
/// an episode with its series), and the ladder runs from the most context-rich phrasing down to the
/// bare title. The search runner tries each rung against the indexers and stops at the first rung that
/// yields an acceptable release, so a precise phrasing wins when the indexer understands it and a
/// spartan one still catches releases named differently.
///
/// TV drills the same way across acquisition units, top-down: a season acquisition leads with the
/// season pack ("{series} S01", "{series} Season 1") and falls back to the complete series; an episode
/// leads with the single episode ("{series} S01E05", "{series} 1x05") — each unit's queries built from
/// the series context stamped on the acquisition.
/// </summary>
public static class ReleaseQueryLadder {
    public static IReadOnlyList<string> For(AcquisitionSearchInput input) {
        var title = input.Title?.Trim() ?? string.Empty;
        if (title.Length == 0) {
            return [];
        }

        var creator = input.Author?.Trim();
        // TV units are named after their series, not their own title ("Season 1" / episode names are
        // meaningless release queries on their own).
        var series = input.Series?.Trim();
        var tvBase = string.IsNullOrWhiteSpace(series) ? title : series;
        var rungs = input.Kind switch {
            // Music releases are conventionally "{artist} - {album}"; artist-first matches far more.
            EntityKind.AudioLibrary or EntityKind.AudioTrack or EntityKind.MusicArtist =>
                new[] { Join(creator, title), title },
            // A single episode: the SxxEyy phrasing first, the "1x05" convention second.
            EntityKind.Video when input is { SeasonNumber: { } season, EpisodeNumber: { } episode } =>
                [
                    Join(tvBase, $"S{season:00}E{episode:00}"),
                    Join(tvBase, $"{season}x{episode:00}"),
                ],
            // Movies are commonly year-disambiguated ("Dune 2021" vs "Dune 1984").
            EntityKind.Movie or EntityKind.Video =>
                [Join(title, input.Year?.ToString()), title],
            // A season pack: precise phrasings first, then the complete-series pack that contains it.
            EntityKind.VideoSeason when input.SeasonNumber is { } season =>
                [
                    Join(tvBase, $"S{season:00}"),
                    Join(tvBase, $"Season {season}"),
                    Join(tvBase, "complete"),
                ],
            // A series-level acquisition (or a season with no number) seeks the whole series.
            EntityKind.VideoSeries or EntityKind.VideoSeason =>
                [Join(tvBase, "complete"), tvBase],
            // Books search "{title} {author}", falling back to the bare title for releases that don't
            // carry the author in their name.
            _ => [Join(title, creator), title],
        };

        // Context fields are often null, which collapses rungs into duplicates — keep the first of each.
        return rungs
            .Where(rung => !string.IsNullOrWhiteSpace(rung))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Join(string? left, string? right) =>
        string.Join(' ', new[] { left, right }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
}
