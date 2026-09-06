using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>An automatic TV import still waiting for review, with its exact held-state observation.</summary>
/// <param name="FinalSourcePath">Previously imported location, retained during partial-payload recovery.</param>
/// <param name="ImportResultSnapshot">Opaque persisted ledger snapshot used to reject stale recovery writes.</param>
/// <param name="CheckpointSnapshot">Exact saved television plan; recovery must preserve its elected units.</param>
/// <param name="SelectedReleaseSnapshot">Exact release authority observed before reconsideration.</param>
/// <param name="TransferId">Latest completed transfer, compared again when publishing recovery.</param>
/// <param name="TransferContentPath">Observed payload boundary; a changed location invalidates the observation.</param>
/// <param name="TransferClientItemId">Observed downloader item; a different attempt cannot reuse this plan.</param>
public sealed record HeldTvImport(Guid Id, Guid EntityId, DateTimeOffset HeldAt, string? RecoveryFingerprint,
    string? FinalSourcePath = null, string? ImportResultSnapshot = null,
    string? CheckpointSnapshot = null, string? SelectedReleaseSnapshot = null,
    Guid? TransferId = null, string? TransferContentPath = null, string? TransferClientItemId = null);

/// <summary>Durable compare-and-swap boundary for retrying a retained TV payload after mapping inputs change.</summary>
public interface IHeldTvImportRecoveryStore {
    /// <summary>Lists automatic held television payloads, including saved new-file plans, without active claims or upgrade replacements.</summary>
    Task<IReadOnlyList<HeldTvImport>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a Downloaded completion ticket only while the same held attempt is still current and
    /// these mapping inputs have not already been retried. The fingerprint survives worker restarts.
    /// </summary>
    Task<bool> TryResumeAsync(HeldTvImport held, string fingerprint, CancellationToken cancellationToken);
}

/// <summary>
/// Reconsiders retained automatic TV imports when current provider identities can fill library gaps.
/// Uses the normal import planner and completion handoff; unchanged inputs never create a retry loop.
/// </summary>
public sealed class HeldTvImportRecoveryService(
    IHeldTvImportRecoveryStore recovery,
    IAcquisitionStore acquisitions,
    IImportTargetIndex targets,
    IDownloadPayloadReader payloads,
    IBookAcquisitionProfileStore profiles,
    ILibraryScanRootPersistence roots,
    IMonitorStore monitors,
    ILogger<HeldTvImportRecoveryService> logger,
    ITvEpisodeCatalogEvidenceSource? catalogEvidence = null,
    HeldTvImportRecoveryCursor? recoveryCursor = null,
    TimeProvider? timeProvider = null) {
    private static readonly TimeSpan MaximumSweepTime = TimeSpan.FromSeconds(15);

    /// <summary>Checks held payloads under their active Entity monitor, preserving paused and destructive lifecycle intent.</summary>
    public async Task RecoverAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var clock = timeProvider ?? TimeProvider.System;
        var cursor = recoveryCursor ?? new HeldTvImportRecoveryCursor();
        var started = clock.GetTimestamp();
        using var budget = new CancellationTokenSource(MaximumSweepTime, clock);
        using var sweep = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        try {
            foreach (var held in cursor.Order(await recovery.ListAsync(sweep.Token))) {
                cancellationToken.ThrowIfCancellationRequested();
                if (budget.IsCancellationRequested || clock.GetElapsedTime(started) >= MaximumSweepTime) break;
                cursor.Advance(held);
                try {
                    if ((await monitors.GetByEntityAsync(held.EntityId, sweep.Token))?.Status == MonitorStatus.Active) {
                        await ReconsiderAsync(held, sweep.Token);
                    }
                } catch (OperationCanceledException) {
                    throw;
                } catch (Exception ex) {
                    logger.LogWarning(ex, "Could not reconsider held TV acquisition {Id}.", held.Id);
                }
            }
        } catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            logger.LogDebug("Held TV recovery reached its sweep budget; continuing the backlog on the next tick.");
        }
    }

    private async Task ReconsiderAsync(HeldTvImport held, CancellationToken cancellationToken) {
        var import = await acquisitions.GetImportContextAsync(held.Id, cancellationToken);
        var selected = await acquisitions.GetSelectedReleaseAsync(held.Id, cancellationToken);
        if (import is not { SeasonNumber: { } season, ContentPath: { } contentPath }
            || import.EntityId != held.EntityId || selected is null || selected.ManualPick
            || import.ImportPlacementCheckpoint is not null || import.AtomicUpgradeCheckpoint is not null
            || import.UpgradeOfAcquisitionId is not null) {
            return;
        }
        if (import.TvImportCheckpoint is { } checkpoint) {
            await ReconsiderCheckpointAsync(held, import, checkpoint, selected, cancellationToken);
            return;
        }
        if (await targets.HasUnnumberedWantedTvEpisodesAsync(held.EntityId, season, cancellationToken)) return;
        var retainedPartial = !string.IsNullOrWhiteSpace(import.FinalSourcePath)
            && (await acquisitions.GetTransferInfoAsync(held.Id, cancellationToken))?.ImportResult?.HasRetainedTvVideos() == true;
        if (!string.IsNullOrWhiteSpace(import.FinalSourcePath) && !retainedPartial) {
            return;
        }

        var titles = await targets.GetSeasonEpisodeTitlesAsync(held.EntityId, season, cancellationToken);
        var payload = payloads.Read(contentPath);
        if (payload is null
            || DangerousFileDetection.FindDangerousFile(payload.Files.Select(file => file.RelativePath).ToArray()) is not null) {
            return;
        }
        var search = await acquisitions.GetSearchInputAsync(held.Id, cancellationToken);
        var series = search?.WorkTitle ?? (string.IsNullOrWhiteSpace(import.Series) ? import.Title : import.Series);
        var profile = await profiles.GetImportProfileAsync(import.ProfileId, import.Kind, cancellationToken);
        var catalogPlan = await new TvAcquisitionImportPlanner(targets, monitors, catalogEvidence).PlanAsync(
            import with { Series = series }, payload, profile, MediaQualityLadder.Detect(import.Kind, selected.Title).Code, cancellationToken);
        var plan = catalogPlan.Plan;
        if (plan.Blocked) {
            return;
        }
        var resumesForeignExtras = retainedPartial && catalogPlan.MonitoredExtras.Count > 0;
        if (AcquisitionPayloadValidation.FindConflict(
                payload.Files.Select(file => file.RelativePath).ToArray(), import.Kind, series, search?.Year ?? import.Year,
                resumesForeignExtras ? null : season, import.EpisodeNumber, TvReleaseTokens.NamesCompleteSeries(selected.Title),
                search?.Title ?? import.Title, search?.AbsoluteEpisodeNumber, titles, search?.AlternativeWorkTitles ?? import.AlternativeWorkTitles) is not null) {
            return;
        }

        var layout = await targets.GetTvLayoutAsync(held.EntityId, cancellationToken);
        var knownEpisodes = catalogPlan.Catalog.SelectMany(catalogSeason => catalogSeason.Episodes
                .Select(episode => (Season: catalogSeason.SeasonNumber, episode.Episode)))
            .Concat(titles.Select(title => (Season: season, title.Episode))).ToHashSet();
        var owned = TvOwnedEpisodeCoverage.Read(layout);
        // Reopening a review must offer a real catalog gap. Completely owned packs stay held; they
        // cannot repair missing links and their quality/edition review is a separate user decision.
        var fillsGap = plan.Units.Any(unit => unit.ExtraEpisodes.Prepend(unit.Episode)
            .Any(episode => knownEpisodes.Contains((unit.Season, episode))
                && !owned.Contains((unit.Season, episode))));
        if (!fillsGap) {
            return;
        }

        var root = layout is not null && Directory.Exists(layout.SeriesFolderPath)
            ? await ImportRootResolution.ResolveOwningAsync(roots, layout.SeriesFolderPath,
                candidate => candidate.ScanVideos, cancellationToken)
            : await ImportRootResolution.ResolveAsync(roots, import.TargetLibraryRootId, profile?.TargetLibraryRootId,
                candidate => candidate.ScanVideos, cancellationToken);
        if (root is null || !Directory.Exists(root.Path)) {
            return;
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
            // Deterministic assembly identity permits one reconsideration after a mapping-engine update,
            // even when metadata is unchanged and the release build number stays within the same body.
            MappingEngine = typeof(TvImportPlanBuilder).Assembly.ManifestModule.ModuleVersionId,
            Search = search,
            import.EntityId, import.Kind, import.Series, import.Title, import.Year, import.SeasonNumber,
            import.EpisodeNumber, import.ProfileId, import.TargetLibraryRootId, ReleaseTitle = selected.Title, selected.Identity,
            Profile = profile,
            Root = new { root.Id, root.Path },
            Titles = titles.OrderBy(title => title.Episode).ThenBy(title => title.EntityId).ToArray(),
            catalogPlan.Catalog, catalogPlan.MonitoredExtras,
            Files = payload.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).Select(file => new {
                file.RelativePath, file.SizeBytes,
                ModifiedAt = File.GetLastWriteTimeUtc(Path.Combine(payload.ContentRoot, file.RelativePath))
            }).ToArray(),
            PresentEpisodes = owned.OrderBy(episode => episode.Season).ThenBy(episode => episode.Episode)
                .Select(episode => new { episode.Season, episode.Episode }).ToArray(),
            Owned = layout?.Seasons.OrderBy(pair => pair.Key).Select(pair => new {
                Season = pair.Key,
                Files = pair.Value.EpisodeFileByNumber.OrderBy(file => file.Key).ToArray()
            }).ToArray()
        })));
        await ResumeIfChangedAsync(held, fingerprint, cancellationToken);
    }

    private async Task ReconsiderCheckpointAsync(HeldTvImport held, AcquisitionImportContext import,
        TvImportCheckpoint checkpoint, SelectedRelease selected, CancellationToken cancellationToken) {
        if (held.CheckpointSnapshot is null || checkpoint.TransferClientItemId != import.ClientItemId) return;
        // A saved plan is an immutable election. Observe its pending bytes and current validation
        // settings without rebuilding episode mappings or probing the whole payload every tick.
        var pending = checkpoint.Units.Where(unit => unit.PreviousFilePath is null && !unit.AdoptedExistingTarget
            && (unit.FinalPath is null || !File.Exists(unit.FinalPath))).ToArray();
        if (checkpoint.Units.Any(unit => unit.PreviousFilePath is not null)
            || !pending.Any(unit => File.Exists(unit.SourceAbsolutePath))) return;
        var payload = payloads.Read(import.ContentPath!);
        if (payload is null || DangerousFileDetection.FindDangerousFile(
                payload.Files.Select(file => file.RelativePath).ToArray()) is not null) return;
        if (pending.Any(unit => unit.SourceAbsolutePath is null || !FileSystemPathComparison.Equals(
                Path.GetFullPath(unit.SourceAbsolutePath), Path.GetFullPath(Path.Combine(payload.ContentRoot, unit.SourceRelativePath))))) return;
        var root = await roots.GetLibraryRootAsync(checkpoint.LibraryRootId, cancellationToken);
        if (root is not { ScanVideos: true } || !Directory.Exists(root.Path)) return;

        var requestedRules = await profiles.GetRulesAsync(import.ProfileId, import.Kind, cancellationToken);
        var foreignNumbers = pending.Select(unit => unit.SeasonNumber).Distinct()
            .Where(number => number != import.SeasonNumber).Order().ToArray();
        var catalog = foreignNumbers.Length > 0
            ? await targets.GetSeriesEpisodeCatalogAsync(held.EntityId, cancellationToken) : [];
        var destinationRules = new List<HeldDestinationRules>();
        foreach (var number in foreignNumbers) {
            var destinations = catalog.Where(season => season.SeasonNumber == number).ToArray();
            if (destinations.Length != 1 || destinations[0].SeasonEntityId is not { } destinationId
                || await monitors.GetByEntityAsync(destinationId, cancellationToken) is not { } monitor) return;
            // Monitoring was captured when the plan elected the extras. Match the importer's current
            // destination profile lookup without re-electing episodes when that monitor later pauses.
            destinationRules.Add(new(number, destinationId, monitor.ProfileId,
                await profiles.GetRulesAsync(monitor.ProfileId, EntityKind.VideoSeason, cancellationToken)));
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
            ValidationEngine = typeof(TvNewFileValidation).Assembly.ManifestModule.ModuleVersionId,
            checkpoint.AttemptId, checkpoint.LibraryRootId, checkpoint.SeriesFolderPath, checkpoint.ImportMode,
            checkpoint.AllowFormatChange, checkpoint.TransferClientItemId, checkpoint.Units,
            import.ProfileId, import.Kind, import.SeasonNumber, Rules = requestedRules, DestinationRules = destinationRules,
            Root = new { root.Id, root.Path }, Selected = selected,
            Files = checkpoint.Units.Select(unit => new {
                Source = ObserveFile(unit.SourceAbsolutePath), Target = ObserveFile(unit.TargetAbsolutePath)
            }).ToArray()
        })));
        await ResumeIfChangedAsync(held, fingerprint, cancellationToken);
    }

    private static HeldFileObservation? ObserveFile(string? path) {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var file = new FileInfo(path);
        return new(path, file.Exists ? file.Length : null, file.Exists ? file.LastWriteTimeUtc : null);
    }

    private sealed record HeldDestinationRules(int Season, Guid EntityId, Guid? ProfileId, BookAcquisitionRules Rules);

    private sealed record HeldFileObservation(string Path, long? Length, DateTime? ModifiedAt);

    private async Task ResumeIfChangedAsync(HeldTvImport held, string fingerprint, CancellationToken cancellationToken) {
        if (fingerprint == held.RecoveryFingerprint) {
            return;
        }
        // Provider lookups must not keep a monitor row locked. Recheck current intent only when
        // publishing the completion ticket; the importer validates and resumes any saved placement.
        await monitors.ExecuteIfActiveEntityMutationAsync(held.EntityId, async token => {
            if (await recovery.TryResumeAsync(held, fingerprint, token)) {
                logger.LogInformation("Held TV acquisition {Id} has changed import inputs; resuming its retained payload.", held.Id);
            }
        }, cancellationToken);
    }
}
