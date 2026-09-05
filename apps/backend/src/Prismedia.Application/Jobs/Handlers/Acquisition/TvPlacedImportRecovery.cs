using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>Rebuilds episode coverage for files already placed before an interrupted catalog handoff.</summary>
public static class TvPlacedImportRecovery {
    /// <summary>
    /// Uses original ledger filenames when naming templates erased paired-episode identities, otherwise
    /// reuses the normal filename planner. Null preserves ambiguous or unreadable evidence for review.
    /// </summary>
    public static IReadOnlyList<ImportedTvEpisode>? Plan(IReadOnlyList<string> files, string libraryRoot,
        string seriesFolder, string series, int? season, int? episode, IReadOnlyList<TvEpisodeTitle> titles,
        AcquisitionTransferInfo? transfer) {
        if (transfer?.ImportResultUnavailable == true) return null;
        var originalNames = new Dictionary<string, string>(FileSystemPathComparison.Comparer);
        foreach (var entry in transfer?.ImportResult?.Files ?? []) {
            if (entry.Role != AcquisitionImportFileRole.Media || entry.Status != AcquisitionImportFileStatus.Imported) continue;
            if (string.IsNullOrWhiteSpace(entry.DestinationRelativePath) || string.IsNullOrWhiteSpace(entry.SourceRelativePath)) return null;
            var destination = Path.GetFullPath(Path.Combine(libraryRoot, entry.DestinationRelativePath));
            if (!FileSystemPathComparison.IsSameOrDescendant(libraryRoot, destination)
                || !originalNames.TryAdd(destination, entry.SourceRelativePath)) return null;
        }
        var actualPaths = new Dictionary<string, string>(FileSystemPathComparison.Comparer);
        var candidates = new List<ImportCandidateFile>(files.Count);
        foreach (var file in files) {
            var original = originalNames.GetValueOrDefault(file) ?? Path.GetRelativePath(seriesFolder, file);
            if (!actualPaths.TryAdd(original, file)) return null;
            var length = new FileInfo(file).Length;
            if (length <= 0) return null;
            candidates.Add(new(original, length));
        }
        var plan = TvImportPlanBuilder.PlanUnits(candidates, series, season, episode, episodeTitles: titles);
        if (plan.Blocked) return null;
        var planned = plan.Units.Select(unit => unit.SourceRelativePath).ToHashSet(FileSystemPathComparison.Comparer);
        if (episode is null && candidates.Any(file => !planned.Contains(file.RelativePath)
            && !MovieImportPlanBuilder.IsSampleFile(file.RelativePath))) return null;
        return plan.Units.Select(unit => new ImportedTvEpisode(actualPaths[unit.SourceRelativePath], unit.Season,
            unit.Episode, unit.ExtraEpisodes)).ToArray();
    }
}
