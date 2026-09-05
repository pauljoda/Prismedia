using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>An automatic TV import still waiting for review, with its exact held-state observation.</summary>
public sealed record HeldTvImport(Guid Id, Guid EntityId, DateTimeOffset HeldAt, string? RecoveryFingerprint);

/// <summary>Durable compare-and-swap boundary for retrying a retained TV payload after mapping inputs change.</summary>
public interface IHeldTvImportRecoveryStore {
    /// <summary>Lists held television attempts without an in-progress placement checkpoint or upgrade replacement.</summary>
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
    ILogger<HeldTvImportRecoveryService> logger) {
    /// <summary>Checks held payloads under their active Entity monitor, preserving paused and destructive lifecycle intent.</summary>
    public async Task RecoverAsync(CancellationToken cancellationToken) {
        foreach (var held in await recovery.ListAsync(cancellationToken)) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                await monitors.ExecuteIfActiveEntityMutationAsync(held.EntityId,
                    token => ReconsiderAsync(held, token), cancellationToken);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "Could not reconsider held TV acquisition {Id}.", held.Id);
            }
        }
    }

    private async Task ReconsiderAsync(HeldTvImport held, CancellationToken cancellationToken) {
        var import = await acquisitions.GetImportContextAsync(held.Id, cancellationToken);
        var selected = await acquisitions.GetSelectedReleaseAsync(held.Id, cancellationToken);
        if (import is not { SeasonNumber: { } season, ContentPath: { } contentPath }
            || import.EntityId != held.EntityId || selected is null || selected.ManualPick
            || import.TvImportCheckpoint is not null || import.ImportPlacementCheckpoint is not null
            || !string.IsNullOrWhiteSpace(import.FinalSourcePath)
            || await targets.HasUnnumberedWantedTvEpisodesAsync(held.EntityId, season, cancellationToken)) {
            return;
        }

        var titles = await targets.GetSeasonEpisodeTitlesAsync(held.EntityId, season, cancellationToken);
        var payload = payloads.Read(contentPath);
        if (titles.Count == 0 || payload is null
            || DangerousFileDetection.FindDangerousFile(payload.Files.Select(file => file.RelativePath).ToArray()) is not null) {
            return;
        }
        var search = await acquisitions.GetSearchInputAsync(held.Id, cancellationToken);
        var series = search?.WorkTitle ?? (string.IsNullOrWhiteSpace(import.Series) ? import.Title : import.Series);
        if (AcquisitionPayloadValidation.FindConflict(
                payload.Files.Select(file => file.RelativePath).ToArray(), import.Kind, series, search?.Year ?? import.Year,
                season, import.EpisodeNumber, TvReleaseTokens.NamesCompleteSeries(selected.Title),
                search?.Title ?? import.Title, search?.AbsoluteEpisodeNumber, titles) is not null) {
            return;
        }

        var profile = await profiles.GetImportProfileAsync(import.ProfileId, import.Kind, cancellationToken);
        var plan = TvImportPlanBuilder.PlanUnits(payload.Files, series, season, import.EpisodeNumber,
            profile?.PathTemplate, MediaQualityLadder.Detect(import.Kind, selected.Title).Code, titles);
        if (plan.Blocked) {
            return;
        }
        var layout = await targets.GetTvLayoutAsync(held.EntityId, cancellationToken);
        var knownEpisodes = titles.Select(title => title.Episode).ToHashSet();
        // Reopening a review must offer a real catalog gap. Completely owned packs stay held; they
        // cannot repair missing links and their quality/edition review is a separate user decision.
        var fillsGap = plan.Units.Any(unit => unit.ExtraEpisodes.Prepend(unit.Episode)
            .Any(episode => knownEpisodes.Contains(episode)
                && layout?.Seasons.GetValueOrDefault(unit.Season)?.EpisodeFileByNumber.ContainsKey(episode) != true));
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
            Files = payload.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).Select(file => new {
                file.RelativePath, file.SizeBytes,
                ModifiedAt = File.GetLastWriteTimeUtc(Path.Combine(payload.ContentRoot, file.RelativePath))
            }).ToArray(),
            Owned = layout?.Seasons.OrderBy(pair => pair.Key).Select(pair => new {
                Season = pair.Key,
                Files = pair.Value.EpisodeFileByNumber.OrderBy(file => file.Key).ToArray()
            }).ToArray()
        })));
        if (fingerprint == held.RecoveryFingerprint) {
            return;
        }
        if (await recovery.TryResumeAsync(held, fingerprint, cancellationToken)) {
            logger.LogInformation("Held TV acquisition {Id} has a new mapping that can fill library gaps; resuming its retained payload.", held.Id);
        }
    }
}
