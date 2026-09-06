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
        string? seriesTitle = null,
        IReadOnlyList<string>? alternativeWorkTitles = null) {
        var result = new List<TvCrossSeasonFileEvidence>();
        var titleIndexes = new Dictionary<TvSeasonEpisodeCatalog, TvEpisodeEvidenceIndex>();
        var videos = TvImportPlanBuilder.UnmappedVideos(files, []);
        var paths = videos.Select(file => file.RelativePath).ToHashSet(FileSystemPathComparison.Comparer);
        foreach (var file in videos.Where(file => !TvImportPlanBuilder.HasInternetArchiveOriginalSibling(file.RelativePath, paths))) {
            var name = Path.GetFileNameWithoutExtension(file.RelativePath);
            var declared = TvReleaseTokens.ParseEpisodes(name);
            var seriesAgrees = declared is null || seriesTitle is null || AcquisitionWorkTitles.Match(name, seriesTitle, alternativeWorkTitles ?? []).TitleMatched;
            var tail = declared is null
                ? AcquisitionWorkTitles.EpisodeEvidence(name, seriesTitle, alternativeWorkTitles ?? [])
                : TvReleaseTokens.EpisodeTitleTail(name) ?? string.Empty;
            var matches = catalog.SelectMany(season => season.Episodes
                    .Where(episode => IsDistinctiveTitle(episode.Title)
                        && ReleaseTitleIdentity.ContainsMeaningfulRun(tail, episode.Title))
                    .Select(episode => (Season: season, Episode: episode)))
                .ToArray();
            if (matches.Any(match => match.Season.SeasonNumber != requestedSeason)) {
                if (declared is null && ResolveCompleteTitleBundle(file.RelativePath, tail, matches, catalog, titleIndexes) is { } complete) {
                    if (complete.Destination!.SeasonNumber != requestedSeason) result.Add(complete);
                    continue;
                }
                var requested = catalog.Where(season => season.SeasonNumber == requestedSeason)
                    .SelectMany(season => season.Episodes)
                    .Where(episode => ReleaseTitleIdentity.ContainsMeaningfulRun(tail, episode.Title)).ToArray();
                // Reruns and recurring titles may appear in several seasons. Agreement between every
                // requested episode number and its complete title evidence outweighs those repeats.
                // Conflicting numbering or an incomplete pair still stays ambiguous below.
                if (seriesAgrees && declared is { } numbered && numbered.Season == requestedSeason
                    && catalog.Count(season => season.SeasonNumber == requestedSeason) == 1
                    && requested.Length == numbered.Episodes.Count
                    && requested.All(episode => episode.EntityId is not null || episode.ProviderIdentity is not null)
                    && requested.Select(episode => episode.Episode).ToHashSet().SetEquals(numbered.Episodes)
                    && matches.All(match => requested.Any(episode =>
                        ReleaseTitleIdentity.ComparableTokens(episode.Title)
                            .SequenceEqual(ReleaseTitleIdentity.ComparableTokens(match.Episode.Title))))
                    && !TitlesOverlap(requested)) continue;
                var destinations = matches.Select(match => match.Season.SeasonNumber).Distinct().ToArray();
                var episodes = matches.Select(match => match.Episode).OrderBy(episode => episode.Episode).ToArray();
                var destination = destinations.Length == 1 ? matches[0].Season : null;
                var unambiguous = seriesAgrees && destination is { SeasonNumber: >= 0 }
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
            var valid = seriesAgrees && numeric.Season >= 0 && matches.Length == 0 && known.Length == numeric.Episodes.Count
                && known.All(episode => episode.EntityId is not null || episode.ProviderIdentity is not null)
                && known.Select(episode => episode.Episode).Distinct().Count() == known.Length;
            result.Add(new(file.RelativePath, valid ? seasons[0] : null, valid ? known : []));
        }
        return result;
    }

    private static TvCrossSeasonFileEvidence? ResolveCompleteTitleBundle(string sourcePath, string tail,
        IReadOnlyList<(TvSeasonEpisodeCatalog Season, TvEpisodeTitle Episode)> matches,
        IReadOnlyList<TvSeasonEpisodeCatalog> catalog,
        Dictionary<TvSeasonEpisodeCatalog, TvEpisodeEvidenceIndex> titleIndexes) {
        var groups = matches.GroupBy(match => match.Season).ToArray();
        if (groups.Any(group => catalog.Count(season => season.SeasonNumber == group.Key.SeasonNumber) != 1
                || group.Select(match => match.Episode.Episode).Distinct().Count() != group.Count())) return null;
        // Compare complete content, including a competing single compound title. A pair is unique
        // only when no other season can explain the whole title sequence.
        var complete = groups.Where(group => {
            if (!titleIndexes.TryGetValue(group.Key, out var index)) {
                index = new TvEpisodeEvidenceIndex(group.Key.Episodes);
                titleIndexes.Add(group.Key, index);
            }
            return index.HasDistinctLeadingTitles(tail, group.Select(match => match.Episode.Episode).ToArray());
        }).Take(2).ToArray();
        if (complete.Length != 1 || complete[0].Count() < 2 || complete[0].Key.SeasonNumber < 0
            || complete[0].Any(match => match.Episode.EntityId is null && match.Episode.ProviderIdentity is null)) return null;
        var resolved = complete[0];
        return new(sourcePath, resolved.Key, resolved.Select(match => match.Episode).OrderBy(episode => episode.Episode).ToArray());
    }

    internal static bool IsDistinctiveTitle(string title) =>
        !TvEpisodeIdentifiers.IsGenericTitle(title)
        && ReleaseTitleIdentity.ComparableTokens(title).Count >= 2;

    internal static bool TitlesOverlap(IReadOnlyList<TvEpisodeTitle> episodes) =>
        episodes.Where((episode, index) => episodes.Skip(index + 1).Any(other =>
            ReleaseTitleIdentity.ContainsMeaningfulRun(episode.Title, other.Title)
            || ReleaseTitleIdentity.ContainsMeaningfulRun(other.Title, episode.Title))).Any();
}
