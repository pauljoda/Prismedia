using Prismedia.Application.Files;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// A payload file whose evidence points outside the requested season. Null destination means the
/// foreign evidence is incomplete or ambiguous and the file must remain available for review.
/// </summary>
public sealed record TvCrossSeasonFileEvidence(
    string SourceRelativePath, TvSeasonEpisodeCatalog? Destination, IReadOnlyList<TvEpisodeTitle> Episodes);

/// <summary>
/// Recognizes foreign-season files against current series metadata. Numbered foreign files require
/// known catalog slots; overriding a season label requires unique, distinctive title evidence for
/// every declared half. This layer never infers provider completeness from an absent episode.
/// </summary>
public static class TvCrossSeasonImportEvidence {
    /// <summary>Finds foreign or conflicting files; ordinary requested-season files remain with the standard planner.</summary>
    public static IReadOnlyList<TvCrossSeasonFileEvidence> Find(
        IReadOnlyList<ImportCandidateFile> files, int requestedSeason, IReadOnlyList<TvSeasonEpisodeCatalog> catalog,
        string? seriesTitle = null) {
        var result = new List<TvCrossSeasonFileEvidence>();
        var videos = TvImportPlanBuilder.UnmappedVideos(files, []);
        var paths = videos.Select(file => file.RelativePath).ToHashSet(FileSystemPathComparison.Comparer);
        foreach (var file in videos.Where(file => !TvImportPlanBuilder.HasInternetArchiveOriginalSibling(file.RelativePath, paths))) {
            var name = Path.GetFileNameWithoutExtension(file.RelativePath);
            var declared = TvReleaseTokens.ParseEpisodes(name);
            var seriesAgrees = declared is null || seriesTitle is null || ReleaseTitleIdentity.Match(name, seriesTitle).TitleMatched;
            var tail = declared is null ? name : TvReleaseTokens.EpisodeTitleTail(name) ?? string.Empty;
            var matches = catalog.SelectMany(season => season.Episodes
                    .Where(episode => IsDistinctiveTitle(episode.Title)
                        && ReleaseTitleIdentity.ContainsMeaningfulRun(tail, episode.Title))
                    .Select(episode => (Season: season, Episode: episode)))
                .ToArray();
            if (matches.Any(match => match.Season.SeasonNumber != requestedSeason)) {
                var destinations = matches.Select(match => match.Season.SeasonNumber).Distinct().ToArray();
                var episodes = matches.Select(match => match.Episode).OrderBy(episode => episode.Episode).ToArray();
                var destination = destinations.Length == 1 ? matches[0].Season : null;
                var unambiguous = seriesAgrees && destination is { SeasonNumber: > 0 }
                    && catalog.Count(season => season.SeasonNumber == destination.SeasonNumber) == 1
                    && episodes.Length >= (declared?.Episodes.Count ?? 1)
                    && episodes.All(episode => episode.EntityId is not null || episode.ProviderIdentity is not null)
                    && episodes.Select(episode => episode.Episode).Distinct().Count() == episodes.Length
                    && !TitlesOverlap(episodes);
                result.Add(new(file.RelativePath, unambiguous ? destination : null, unambiguous ? episodes : []));
                continue;
            }

            if (declared is not { } numeric || numeric.Season == requestedSeason) {
                continue;
            }
            // A title pointing back into the requested season contradicts the foreign numeric label.
            // Let review resolve this rather than accepting the foreign slots from numbering alone.
            var seasons = catalog.Where(season => season.SeasonNumber == numeric.Season).ToArray();
            var known = seasons.Length == 1
                ? seasons[0].Episodes.Where(episode => numeric.Episodes.Contains(episode.Episode)).ToArray()
                : [];
            var valid = seriesAgrees && numeric.Season > 0 && matches.Length == 0 && known.Length == numeric.Episodes.Count
                && known.All(episode => episode.EntityId is not null || episode.ProviderIdentity is not null)
                && known.Select(episode => episode.Episode).Distinct().Count() == known.Length;
            result.Add(new(file.RelativePath, valid ? seasons[0] : null, valid ? known : []));
        }
        return result;
    }

    internal static bool IsDistinctiveTitle(string title) =>
        TvEpisodeIdentifiers.Create(title, null).Numeric.Count == 0
        && ReleaseTitleIdentity.ComparableTokens(title).Count >= 2;

    internal static bool TitlesOverlap(IReadOnlyList<TvEpisodeTitle> episodes) =>
        episodes.Where((episode, index) => episodes.Skip(index + 1).Any(other =>
            ReleaseTitleIdentity.ContainsMeaningfulRun(episode.Title, other.Title)
            || ReleaseTitleIdentity.ContainsMeaningfulRun(other.Title, episode.Title))).Any();
}
