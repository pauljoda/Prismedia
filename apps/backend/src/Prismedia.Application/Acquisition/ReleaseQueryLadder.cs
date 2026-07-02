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
/// TV drills the same way across acquisition units, top-down: a series acquisition first seeks the
/// whole series, a season acquisition the season pack ("{series} S01"), an episode the single episode
/// ("{series} S01E01") — each unit's queries built from the levels above it. The season/episode rungs
/// activate when the per-episode engine lands and stamps season/episode positions on acquisitions.
/// </summary>
public static class ReleaseQueryLadder {
    public static IReadOnlyList<string> For(AcquisitionSearchInput input) {
        var title = input.Title?.Trim() ?? string.Empty;
        if (title.Length == 0) {
            return [];
        }

        var creator = input.Author?.Trim();
        var rungs = input.Kind switch {
            // Music releases are conventionally "{artist} - {album}"; artist-first matches far more.
            EntityKind.AudioLibrary or EntityKind.AudioTrack or EntityKind.MusicArtist =>
                new[] { Join(creator, title), title },
            // Movies are commonly year-disambiguated ("Dune 2021" vs "Dune 1984").
            EntityKind.Movie or EntityKind.Video =>
                [Join(title, input.Year?.ToString()), title],
            // A series acquisition seeks the whole series first; drilling into season/episode units is
            // the TV engine's job (each unit re-enters this ladder with its own kind and context).
            EntityKind.VideoSeries or EntityKind.VideoSeason =>
                [Join(title, "complete"), title],
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
