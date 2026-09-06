using Prismedia.Application.Files;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>A TV placement proposal and the monitored destination seasons required to elect it.</summary>
public sealed record TvCatalogImportPlan(TvUnitsPlan Plan, IReadOnlyList<Guid> MonitoredExtras) {
    /// <summary>Local catalog evidence used to decide the plan, retained for recovery fingerprints.</summary>
    public IReadOnlyList<TvSeasonEpisodeCatalog> Catalog { get; init; } = [];
}

/// <summary>
/// Shares automatic TV mapping between import and recovery, preserving manual choices and retaining
/// unmonitored or uncertain foreign-season payloads. Planning performs no filesystem or catalog mutations.
/// </summary>
public sealed class TvAcquisitionImportPlanner(IImportTargetIndex targets, IMonitorStore? monitors = null) {
    /// <summary>Plans a payload against current local episode identities and active destination monitors.</summary>
    public async Task<TvCatalogImportPlan> PlanAsync(AcquisitionImportContext import, DownloadPayload payload,
        BookImportProfile? profile, string? quality, CancellationToken cancellationToken) {
        var catalog = import.EntityId is { } entityId && import.SeasonNumber is not null
            ? await targets.GetSeriesEpisodeCatalogAsync(entityId, cancellationToken)
            : [];
        return (await BuildAsync(import, payload, profile, quality, catalog, cancellationToken)) with { Catalog = catalog };
    }

    private async Task<TvCatalogImportPlan> BuildAsync(
        AcquisitionImportContext import, DownloadPayload payload, BookImportProfile? profile,
        string? quality, IReadOnlyList<TvSeasonEpisodeCatalog> catalog, CancellationToken cancellationToken) {
        var series = string.IsNullOrWhiteSpace(import.Series) ? import.Title : import.Series;
        if (import.ManualFileMappings is { Count: > 0 } manualMappings) {
            return new(TvImportPlanBuilder.PlanManualUnits(payload.Files, manualMappings, series, profile?.PathTemplate, quality), []);
        }
        var evidence = import.SeasonNumber is { } requestedSeason
            ? TvCrossSeasonImportEvidence.Find(payload.Files, requestedSeason, catalog, series)
            : [];
        var excluded = evidence.Select(file => file.SourceRelativePath).ToHashSet(FileSystemPathComparison.Comparer);
        var ordinaryFiles = payload.Files.Where(file => !excluded.Contains(file.RelativePath)).ToArray();
        var seasonTitles = catalog.Where(season => season.SeasonNumber == import.SeasonNumber).ToArray();
        var titles = seasonTitles.Length == 1 ? seasonTitles[0].Episodes : import.EntityId is { } linkedId && import.SeasonNumber is { } number
            ? await targets.GetSeasonEpisodeTitlesAsync(linkedId, number, cancellationToken) : [];
        var ordinaryVideos = TvImportPlanBuilder.UnmappedVideos(ordinaryFiles, []);
        if (excluded.Count > 0 && import.EpisodeNumber is not null && ordinaryVideos.Count == 1
            && TvImportPlanBuilder.InferEpisode(ordinaryVideos[0].RelativePath, import.SeasonNumber, titles) is null) {
            return new(TvUnitsPlan.Block(ImportBlockReason.AmbiguousMultiplePrimaries), []);
        }
        var ordinaryPlan = ordinaryFiles.Any(file => TvImportPlanBuilder.IsVideoFile(file.RelativePath))
            ? TvImportPlanBuilder.PlanUnits(ordinaryFiles, series, import.SeasonNumber, import.EpisodeNumber,
                profile?.PathTemplate, quality, titles)
            : TvUnitsPlan.For([]);
        if (ordinaryPlan.Blocked) {
            return new(ordinaryPlan, []);
        }
        var foreignIds = evidence.Where(file => file.Destination is not null)
            .Select(file => file.Destination!.SeasonEntityId).Distinct().ToArray();
        var active = monitors is not null
            ? (await monitors.ListByEntityIdsAsync(foreignIds, cancellationToken))
                .Where(pair => pair.Value.Status == MonitorStatus.Active)
                .OrderBy(pair => pair.Value.Id).Select(pair => pair.Key).ToArray()
            : [];
        // Extras fill missing catalog slots. Replacing an owned foreign-season file needs that season's
        // own profile and measured upgrade evaluation, not the requested season's settings.
        var importableExtras = evidence.Where(file => file.Destination is { } destination
            && active.Contains(destination.SeasonEntityId) && file.Episodes.All(episode => episode.IsWanted)).ToArray();
        var mappings = importableExtras
            .SelectMany(file => file.Episodes.Select(episode => new ManualImportFileMapping(
                file.SourceRelativePath, episode.EntityId!.Value, file.Destination!.SeasonNumber, episode.Episode))).ToArray();
        if (mappings.Length == 0) {
            return new(ordinaryPlan.Units.Count > 0 ? ordinaryPlan : TvUnitsPlan.Block(ImportBlockReason.NoMatchingTvUnit), []);
        }
        var extrasPlan = TvImportPlanBuilder.PlanManualUnits(payload.Files, mappings, series, profile?.PathTemplate, quality);
        if (extrasPlan.Blocked) {
            return new(extrasPlan, []);
        }
        var units = ordinaryPlan.Units.Concat(extrasPlan.Units).ToArray();
        var claims = units.SelectMany(unit => unit.ExtraEpisodes.Prepend(unit.Episode).Select(episode => (unit.Season, episode))).ToArray();
        return claims.Distinct().Count() == claims.Length
            ? new(TvUnitsPlan.For(units), active.Where(id => importableExtras.Any(file => file.Destination!.SeasonEntityId == id)).ToArray())
            : new(TvUnitsPlan.Block(ImportBlockReason.AmbiguousMultiplePrimaries), []);
    }

}
