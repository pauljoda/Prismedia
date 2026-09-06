namespace Prismedia.Application.Acquisition;

/// <summary>A proposed additive repair for a shared library file, preserving every existing episode owner.</summary>
public sealed record TvOwnedEpisodeCoveragePlan(Guid SeasonEntityId, int SeasonNumber, IReadOnlyList<TvEpisodeTitle> MissingEpisodes);

/// <summary>
/// Finds missing episode links from original import names and current catalog titles. The writer must
/// independently verify file provenance, current ownership, monitoring, and lifecycle state before applying a plan.
/// </summary>
public static class TvOwnedEpisodeCoveragePlanner {
    /// <summary>Returns a conservative additive coverage proposal, or null when evidence conflicts or no gap exists.</summary>
    public static TvOwnedEpisodeCoveragePlan? Plan(string originalFileName, string seriesTitle, int requestedSeason,
        IReadOnlyList<TvSeasonEpisodeCatalog> catalog, IReadOnlyCollection<Guid> existingOwnerIds) {
        var name = Path.GetFileNameWithoutExtension(originalFileName);
        if (existingOwnerIds.Count == 0 || !ReleaseTitleIdentity.Match(name, seriesTitle).TitleMatched) return null;
        var coverage = ReadCoverage(originalFileName, seriesTitle, requestedSeason, catalog);
        if (coverage is not { Season.SeasonEntityId: { } seasonId } || coverage.Episodes.Count < 2) return null;
        var episodes = coverage.Episodes;
        if (episodes.Any(episode => episode.EntityId is null)
            || episodes.Select(episode => episode.EntityId).Distinct().Count() != episodes.Count
            || existingOwnerIds.Any(owner => !episodes.Any(episode => episode.EntityId == owner))) return null;
        var missing = episodes.Where(episode => !existingOwnerIds.Contains(episode.EntityId!.Value)).ToArray();
        if (missing.Length == 0 || missing.Any(episode => !episode.IsWanted)) return null;

        // Historical filenames are useful evidence, but numeric ranges alone are not enough to add
        // coverage to an existing library file. Every proposed half needs its distinctive catalog title.
        var tail = TvReleaseTokens.EpisodeTitleTail(name) ?? name;
        if (episodes.Any(episode => !TvCrossSeasonImportEvidence.IsDistinctiveTitle(episode.Title)
                || !ReleaseTitleIdentity.ContainsMeaningfulRun(tail, episode.Title))
            || TvCrossSeasonImportEvidence.TitlesOverlap(episodes)) return null;
        return new(seasonId, coverage.Season.SeasonNumber, missing);
    }

    private static Coverage? ReadCoverage(string originalFileName, string seriesTitle, int requestedSeason,
        IReadOnlyList<TvSeasonEpisodeCatalog> catalog) {
        ImportCandidateFile[] files = [new(originalFileName, 1)];
        var foreign = TvCrossSeasonImportEvidence.Find(files, requestedSeason, catalog, seriesTitle);
        if (foreign.Count > 0) {
            var file = foreign[0];
            return file.Destination is { SeasonEntityId: not null } destination ? new(destination, file.Episodes) : null;
        }
        var seasons = catalog.Where(season => season.SeasonNumber == requestedSeason).ToArray();
        if (seasons.Length != 1 || seasons[0].SeasonEntityId is null) return null;
        var plan = TvImportPlanBuilder.PlanUnits(files, seriesTitle, requestedSeason, null, episodeTitles: seasons[0].Episodes);
        if (plan.Blocked || plan.Units.Count != 1) return null;
        var unit = plan.Units[0];
        var positions = unit.ExtraEpisodes.Prepend(unit.Episode).ToHashSet();
        var episodes = seasons[0].Episodes.Where(episode => positions.Contains(episode.Episode)).ToArray();
        return episodes.Length == positions.Count && positions.SetEquals(episodes.Select(episode => episode.Episode))
            ? new(seasons[0], episodes) : null;
    }

    private sealed record Coverage(TvSeasonEpisodeCatalog Season, IReadOnlyList<TvEpisodeTitle> Episodes);
}
