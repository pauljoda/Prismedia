namespace Prismedia.Application.Acquisition;

/// <summary>A proposed additive repair for a shared library file, preserving every existing episode owner.</summary>
public sealed record TvOwnedEpisodeCoveragePlan(Guid SeasonEntityId, int SeasonNumber, IReadOnlyList<TvEpisodeTitle> MissingEpisodes);

/// <summary>
/// Complete title-proven coverage replacing one incorrect file owner. The writer must prove that the
/// old owner is an unreviewed scanner placeholder and that every destination remains monitored and wanted.
/// </summary>
public sealed record TvOwnedEpisodeRemappingPlan(Guid SeasonEntityId, int SeasonNumber, IReadOnlyList<TvEpisodeTitle> Episodes);

/// <summary>
/// Finds missing episode links from original import names and current catalog titles. The writer must
/// independently verify file provenance, current ownership, monitoring, and lifecycle state before applying a plan.
/// </summary>
public static class TvOwnedEpisodeCoveragePlanner {
    /// <summary>
    /// Proposes complete paired coverage for a file whose sole saved owner is outside that coverage.
    /// Numbers alone, occupied destinations, and partly correct shared mappings require review.
    /// </summary>
    public static TvOwnedEpisodeRemappingPlan? PlanReassignment(string originalFileName, string seriesTitle,
        int requestedSeason, IReadOnlyList<TvSeasonEpisodeCatalog> catalog, IReadOnlyCollection<Guid> existingOwnerIds,
        IReadOnlyList<string>? alternativeWorkTitles = null) {
        if (existingOwnerIds.Count != 1) return null;
        var coverage = ReadVerifiedCoverage(originalFileName, seriesTitle, requestedSeason, catalog, alternativeWorkTitles);
        if (coverage is null || coverage.Episodes.Any(episode => !episode.IsWanted
                || existingOwnerIds.Contains(episode.EntityId!.Value))) return null;
        return new(coverage.Season.SeasonEntityId!.Value, coverage.Season.SeasonNumber, coverage.Episodes);
    }

    /// <summary>Returns a conservative additive coverage proposal, or null when evidence conflicts or no gap exists.</summary>
    public static TvOwnedEpisodeCoveragePlan? Plan(string originalFileName, string seriesTitle, int requestedSeason,
        IReadOnlyList<TvSeasonEpisodeCatalog> catalog, IReadOnlyCollection<Guid> existingOwnerIds,
        IReadOnlyList<string>? alternativeWorkTitles = null) {
        if (existingOwnerIds.Count == 0) return null;
        var coverage = ReadVerifiedCoverage(originalFileName, seriesTitle, requestedSeason, catalog, alternativeWorkTitles);
        if (coverage is null || existingOwnerIds.Any(owner => !coverage.Episodes.Any(episode => episode.EntityId == owner))) return null;
        var missing = coverage.Episodes.Where(episode => !existingOwnerIds.Contains(episode.EntityId!.Value)).ToArray();
        return missing.Length == 0 || missing.Any(episode => !episode.IsWanted) ? null
            : new(coverage.Season.SeasonEntityId!.Value, coverage.Season.SeasonNumber, missing);
    }

    private static Coverage? ReadVerifiedCoverage(string originalFileName, string seriesTitle, int requestedSeason,
        IReadOnlyList<TvSeasonEpisodeCatalog> catalog, IReadOnlyList<string>? alternativeWorkTitles) {
        var name = Path.GetFileNameWithoutExtension(originalFileName);
        var workMatched = AcquisitionWorkTitles.Match(name, seriesTitle, alternativeWorkTitles ?? []).TitleMatched;
        var titleEvidence = AcquisitionWorkTitles.EpisodeEvidence(name, seriesTitle, alternativeWorkTitles ?? []);
        if (!workMatched
            && (TvReleaseTokens.ParseEpisodes(name) is not null || titleEvidence == name)) return null;
        var coverage = ReadCoverage(originalFileName, seriesTitle, requestedSeason, catalog, alternativeWorkTitles);
        if (coverage is not { Season.SeasonEntityId: not null } || coverage.Episodes.Count < 2) return null;
        var episodes = coverage.Episodes;
        if (episodes.Any(episode => episode.EntityId is null)
            || episodes.Select(episode => episode.EntityId).Distinct().Count() != episodes.Count) return null;

        // Historical filenames are useful evidence, but numeric ranges alone are not enough to add
        // coverage to an existing library file. Every proposed half needs its distinctive catalog title.
        var tail = TvReleaseTokens.EpisodeTitleTail(name) ?? AcquisitionWorkTitles.EpisodeEvidence(name, seriesTitle, alternativeWorkTitles ?? []);
        if (episodes.Any(episode => !TvCrossSeasonImportEvidence.IsDistinctiveTitle(episode.Title)
                || !ReleaseTitleIdentity.ContainsMeaningfulRun(tail, episode.Title))
            || TvCrossSeasonImportEvidence.TitlesOverlap(episodes)) return null;
        if (!workMatched && !new TvEpisodeEvidenceIndex(episodes).HasDistinctLeadingTitles(
                titleEvidence, episodes.Select(episode => episode.Episode).ToArray())) return null;
        return coverage;
    }

    private static Coverage? ReadCoverage(string originalFileName, string seriesTitle, int requestedSeason,
        IReadOnlyList<TvSeasonEpisodeCatalog> catalog, IReadOnlyList<string>? alternativeWorkTitles) {
        ImportCandidateFile[] files = [new(originalFileName, 1)];
        var foreign = TvCrossSeasonImportEvidence.Find(files, requestedSeason, catalog, seriesTitle, alternativeWorkTitles);
        if (foreign.Count > 0) {
            var file = foreign[0];
            return file.Destination is { SeasonEntityId: not null } destination ? new(destination, file.Episodes) : null;
        }
        var seasons = catalog.Where(season => season.SeasonNumber == requestedSeason).ToArray();
        if (seasons.Length != 1 || seasons[0].SeasonEntityId is null) return null;
        var plan = TvImportPlanBuilder.PlanUnits(files, seriesTitle, requestedSeason, null, episodeTitles: seasons[0].Episodes, alternativeWorkTitles: alternativeWorkTitles);
        if (plan.Blocked || plan.Units.Count != 1) return null;
        var unit = plan.Units[0];
        var positions = unit.ExtraEpisodes.Prepend(unit.Episode).ToHashSet();
        var episodes = seasons[0].Episodes.Where(episode => positions.Contains(episode.Episode)).ToArray();
        return episodes.Length == positions.Count && positions.SetEquals(episodes.Select(episode => episode.Episode))
            ? new(seasons[0], episodes) : null;
    }

    private sealed record Coverage(TvSeasonEpisodeCatalog Season, IReadOnlyList<TvEpisodeTitle> Episodes);
}
