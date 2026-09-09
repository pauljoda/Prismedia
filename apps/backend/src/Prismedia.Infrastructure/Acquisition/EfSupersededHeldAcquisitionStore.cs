using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Checks a verified library replacement before electing cleanup of the retained download.</summary>
public sealed class EfSupersededHeldAcquisitionStore(PrismediaDbContext db, IAcquisitionStore acquisitions,
    IMonitorStore monitors, IImportTargetIndex targets, IDownloadPayloadReader payloads) : ISupersededHeldAcquisitionStore {
    /// <inheritdoc />
    public async Task<IReadOnlyList<SupersededHeldAcquisition>> ListAsync(CancellationToken cancellationToken) =>
        await (from replacement in db.Acquisitions.AsNoTracking()
               join held in db.Acquisitions.AsNoTracking() on replacement.RecoveryOfAcquisitionId equals held.Id
               where replacement.Status == AcquisitionStatus.Imported && replacement.EntityId == held.EntityId
                   && !held.ImportManualReview && held.ImportCheckpointJson == null && held.FinalSourcePath == null
                   && (held.Status == AcquisitionStatus.ManualImportRequired || held.Status == AcquisitionStatus.Cancelled
                       || held.Status == AcquisitionStatus.Stopping && held.TeardownIntent == AcquisitionTeardownIntent.Remove)
               orderby held.Id
               select new SupersededHeldAcquisition(held.Id, replacement.Id)).ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryClaimAsync(SupersededHeldAcquisition candidate, CancellationToken cancellationToken) {
        var entityId = await db.Acquisitions.AsNoTracking().Where(row => row.Id == candidate.ReplacementId)
            .Select(row => row.EntityId).SingleOrDefaultAsync(cancellationToken);
        if (entityId is null) return false;
        var claimed = false;
        await monitors.ExecuteIfActiveEntityMutationAsync(entityId.Value, async token => {
            var replacement = await db.Acquisitions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == candidate.ReplacementId
                && row.RecoveryOfAcquisitionId == candidate.HeldId && row.EntityId == entityId
                && row.Status == AcquisitionStatus.Imported && row.ImportCheckpointJson == null && !row.ImportManualReview, token);
            var held = await db.Acquisitions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == candidate.HeldId
                && row.EntityId == entityId && !row.ImportManualReview && row.ImportCheckpointJson == null
                && row.FinalSourcePath == null && row.ImportClaimJobId == null && row.UpgradeOfAcquisitionId == null, token);
            if (held is null || replacement is null || held.Kind != replacement.Kind
                || !await HasPresentReplacementAsync(replacement, token)
                || await acquisitions.GetSelectedReleaseAsync(held.Id, token) is not { ManualPick: false }
                || await acquisitions.GetSelectedReleaseAsync(replacement.Id, token) is not { ManualPick: false }) return;
            if (held.Status == AcquisitionStatus.Stopping) {
                claimed = held.TeardownIntent == AcquisitionTeardownIntent.Remove;
                return;
            }
            if (held.Status is not (AcquisitionStatus.ManualImportRequired or AcquisitionStatus.Cancelled)
                || held.TeardownIntent is not null) return;
            var import = await acquisitions.GetImportContextAsync(held.Id, token);
            if (import?.ContentPath is not { } contentPath) return;
            var payload = payloads.Read(contentPath);
            // Downloader removal deletes its payload boundary. It must never contain an owned library file.
            var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(contentPath)) + Path.DirectorySeparatorChar;
            if (await db.EntityFiles.AnyAsync(file => file.Role == EntityFileRole.Source
                    && (file.Path == contentPath || file.Path.StartsWith(prefix)), token)) return;
            if (payload is not null && import.SeasonNumber is { } season) {
                var catalog = await targets.GetSeriesEpisodeCatalogAsync(entityId.Value, token);
                if (TvCrossSeasonImportEvidence.Find(payload.Files, season, catalog, import.Series, import.AlternativeWorkTitles).Count > 0)
                    return; // Foreign and ambiguous extras keep their review copy, regardless of replacement success.
                var usable = HeldAcquisitionRecoveryPolicy.UsablePayload(payload,
                    (await acquisitions.GetTransferInfoAsync(held.Id, token))?.ImportResult)!;
                if (await HasUnownedCoverageAsync(import, usable, token)) return;
            }
            var claimable = db.Acquisitions.Where(row => row.Id == held.Id && row.Status == held.Status
                && row.UpdatedAt == held.UpdatedAt && !row.ImportManualReview && row.ImportClaimJobId == null
                && row.ImportCheckpointJson == null && row.FinalSourcePath == null
                && row.SelectedReleaseJson == held.SelectedReleaseJson && row.TeardownIntent == null);
            if (db.Database.IsRelational()) {
                claimed = await claimable.ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.TeardownOriginalStatus, held.Status)
                    .SetProperty(row => row.TeardownIntent, AcquisitionTeardownIntent.Remove)
                    .SetProperty(row => row.Status, AcquisitionStatus.Stopping)
                    .SetProperty(row => row.StatusMessage, "Removing the held download after its replacement finished importing.")
                    .SetProperty(row => row.UpdatedAt, DateTimeOffset.UtcNow), token) == 1;
                if (claimed && db.ChangeTracker.Entries<AcquisitionRow>().FirstOrDefault(entry => entry.Entity.Id == held.Id) is { } entry)
                    await entry.ReloadAsync(token);
                return;
            }
            claimed = await acquisitions.TryClaimTeardownAsync(held.Id, held.Status, AcquisitionTeardownIntent.Remove,
                "Removing the held download after its replacement finished importing.", token);
        }, cancellationToken);
        return claimed;
    }

    private async Task<bool> HasUnownedCoverageAsync(AcquisitionImportContext import, DownloadPayload payload,
        CancellationToken cancellationToken) {
        var planned = await new TvAcquisitionImportPlanner(targets).PlanAsync(import, payload, null, null, cancellationToken);
        if (planned.Plan.Blocked) return false;
        var positions = planned.Plan.Units.SelectMany(unit => unit.ExtraEpisodes.Prepend(unit.Episode)
            .Select(episode => (unit.Season, episode))).ToHashSet();
        var covered = planned.Catalog.SelectMany(season => season.Episodes
            .Where(episode => positions.Contains((season.SeasonNumber, episode.Episode))).Select(episode => episode.EntityId)).ToArray();
        if (covered.Length < positions.Count || covered.Any(id => id is null)) return true;
        var ids = covered.Select(id => id!.Value).ToArray();
        var owned = await db.EntityFiles.AsNoTracking().Where(file => ids.Contains(file.EntityId) && file.Role == EntityFileRole.Source)
            .Select(file => new { file.EntityId, file.Path }).ToArrayAsync(cancellationToken);
        return ids.Any(id => !owned.Any(file => file.EntityId == id && File.Exists(file.Path)));
    }

    private async Task<bool> HasPresentReplacementAsync(AcquisitionRow replacement, CancellationToken cancellationToken) {
        if (replacement.EntityId is not { } entityId || replacement.FinalSourcePath is null
            || await db.AcquisitionImportHints.AnyAsync(hint => hint.AcquisitionId == replacement.Id && !hint.Consumed, cancellationToken)) return false;
        if (replacement.Kind is EntityKind.VideoEpisode or EntityKind.Movie) {
            return File.Exists(replacement.FinalSourcePath) && new FileInfo(replacement.FinalSourcePath).Length > 0
                && await db.EntityFiles.AnyAsync(file => file.EntityId == entityId && file.Role == EntityFileRole.Source
                    && file.Path == replacement.FinalSourcePath, cancellationToken);
        }
        if (replacement.Kind != EntityKind.VideoSeason || !Directory.Exists(replacement.FinalSourcePath)
            || !(await new EfEntityFulfillmentProjection(db).ResolveAsync([entityId], cancellationToken)).Contains(entityId)) return false;
        var episodeIds = await db.Entities.Where(entity => entity.ParentEntityId == entityId).Select(entity => entity.Id).ToArrayAsync(cancellationToken);
        var paths = await db.EntityFiles.Where(file => episodeIds.Contains(file.EntityId) && file.Role == EntityFileRole.Source)
            .Select(file => file.Path).ToArrayAsync(cancellationToken);
        return paths.Length > 0 && paths.All(path => File.Exists(path) && new FileInfo(path).Length > 0);
    }
}
