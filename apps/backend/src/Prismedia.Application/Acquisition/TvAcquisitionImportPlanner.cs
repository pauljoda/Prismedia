using Prismedia.Application.Files;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>A TV placement proposal and the monitored destination seasons required to elect it.</summary>
public sealed record TvCatalogImportPlan(TvUnitsPlan Plan, IReadOnlyList<Guid> MonitoredExtras) {
    /// <summary>Local and provider catalog evidence used to decide the plan, retained for recovery fingerprints.</summary>
    public IReadOnlyList<TvSeasonEpisodeCatalog> Catalog { get; init; } = [];
}

/// <summary>
/// Shares automatic TV mapping between import and recovery, preserving manual choices and retaining
/// unmonitored or uncertain foreign-season payloads. Planning performs no filesystem or catalog mutations.
/// </summary>
public sealed class TvAcquisitionImportPlanner(IImportTargetIndex targets, IMonitorStore? monitors = null,
    ITvEpisodeCatalogEvidenceSource? providerEvidence = null) {
    /// <summary>Plans a payload against current local episode identities and active destination monitors.</summary>
    public async Task<TvCatalogImportPlan> PlanAsync(AcquisitionImportContext import, DownloadPayload payload,
        BookImportProfile? profile, string? quality, CancellationToken cancellationToken) {
        var catalog = import.EntityId is { } entityId && import.SeasonNumber is not null
            ? await targets.GetSeriesEpisodeCatalogAsync(entityId, cancellationToken)
            : [];
        var local = await BuildAsync(import, payload, profile, quality, catalog, cancellationToken);
        if (providerEvidence is not null && import.EntityId is { } linkedId && import.SeasonNumber is { } requestedSeason
            && import.ManualFileMappings is not { Count: > 0 } && NeedsProviderEvidence(local, payload, catalog, requestedSeason)) {
            var providerCatalog = await providerEvidence.ReadAsync(linkedId, requestedSeason, payload.Files, cancellationToken);
            if (providerCatalog.Count > 0) {
                catalog = MergeProviderEvidence(catalog, providerCatalog);
                local = await BuildAsync(import, payload, profile, quality, catalog, cancellationToken);
            }
        }
        return local with { Catalog = catalog };
    }

    private static bool NeedsProviderEvidence(TvCatalogImportPlan plan, DownloadPayload payload,
        IReadOnlyList<TvSeasonEpisodeCatalog> catalog, int requestedSeason) {
        var accounted = plan.Plan.Units.Select(unit => unit.SourceRelativePath)
            .Concat(TvCrossSeasonImportEvidence.Find(payload.Files, requestedSeason, catalog)
                .Where(file => file.Destination is not null).Select(file => file.SourceRelativePath));
        // A fully recognized foreign payload may be held solely because its season is unmonitored.
        // That decision has all the evidence it needs; another provider lookup cannot authorize it.
        if (TvImportPlanBuilder.UnmappedVideos(payload.Files, accounted).Count > 0) return true;
        var known = catalog.SelectMany(season => season.Episodes.Select(episode => (season.SeasonNumber, episode.Episode))).ToHashSet();
        return plan.Plan.Units.Any(unit => unit.ExtraEpisodes.Prepend(unit.Episode).Any(episode => !known.Contains((unit.Season, episode))))
            || plan.Plan.Units.Any(unit => NeedsDescriptiveTitleEvidence(unit, catalog));
    }

    private static bool NeedsDescriptiveTitleEvidence(TvPlanUnit unit, IReadOnlyList<TvSeasonEpisodeCatalog> catalog) {
        var positions = unit.ExtraEpisodes.Prepend(unit.Episode).ToHashSet();
        var generic = catalog.Where(season => season.SeasonNumber == unit.Season).SelectMany(season => season.Episodes)
            .Any(episode => positions.Contains(episode.Episode)
                && (TvReleaseTokens.ParseEpisodes(episode.Title) is not null
                    || TvEpisodeIdentifiers.IsGenericTitle(episode.Title)));
        if (!generic) return false;
        var tail = TvReleaseTokens.EpisodeTitleTail(Path.GetFileNameWithoutExtension(unit.SourceRelativePath));
        // Only the leading descriptive phrase can justify another catalog read. Codec, quality,
        // language, channel-count, and release-group tails do not supply episode-title evidence.
        return ReleaseTitleIdentity.ComparableTokens(tail)
            .TakeWhile(token => token.All(char.IsLetter) && !ReleaseTitleVocabulary.MetadataTokens.Contains(token))
            .Take(2).Count() == 2;
    }

    // Local metadata owns existing positions and titles, including explicit user corrections. Provider
    // evidence fills absent catalog slots; it never silently rewrites those local choices.
    private static IReadOnlyList<TvSeasonEpisodeCatalog> MergeProviderEvidence(
        IReadOnlyList<TvSeasonEpisodeCatalog> local, IReadOnlyList<TvSeasonEpisodeCatalog> provider) {
        var result = local.ToList();
        foreach (var remote in provider) {
            var matches = result.Select((season, index) => (season, index))
                .Where(item => item.season.SeasonNumber == remote.SeasonNumber).ToArray();
            if (matches.Length == 0) {
                result.Add(remote);
                continue;
            }
            if (matches.Length != 1) continue;
            var (existing, index) = matches[0];
            var known = existing.Episodes.Select(episode => episode.Episode).ToHashSet();
            result[index] = existing with {
                Episodes = existing.Episodes.Concat(remote.Episodes.Where(episode => !known.Contains(episode.Episode)))
                    .OrderBy(episode => episode.Episode).ToArray()
            };
        }
        return result.OrderBy(season => season.SeasonNumber).ThenBy(season => season.SeasonEntityId).ToArray();
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
        var foreignIds = evidence.Where(file => file.Destination?.SeasonEntityId is not null)
            .Select(file => file.Destination!.SeasonEntityId!.Value).Distinct().ToArray();
        var active = monitors is not null
            ? (await monitors.ListByEntityIdsAsync(foreignIds, cancellationToken))
                .Where(pair => pair.Value.Status == MonitorStatus.Active)
                .OrderBy(pair => pair.Value.Id).Select(pair => pair.Key).ToArray()
            : [];
        // Extras fill missing catalog slots. Replacing an owned foreign-season file needs that season's
        // own profile and measured upgrade evaluation, not the requested season's settings.
        var importableExtras = evidence.Where(file => file.Destination?.SeasonEntityId is { } destinationId
            && active.Contains(destinationId) && file.Episodes.All(episode => episode.IsWanted && episode.EntityId is not null)).ToArray();
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
