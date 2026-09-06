using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

public sealed partial class EfMonitorStore {
    /// <summary>Restores import links or registers measured library ownership before selecting normal upgrade work.</summary>
    private async Task RestoreOwnedBaselinesAsync(Guid? entityId, Guid? monitorId, UpgradePolicies policies, CancellationToken cancellationToken) {
        var kinds = Enum.GetValues<EntityKind>().Where(MediaQualityLadder.IsUpgradeCapableKind).ToArray();
        var candidates = await db.Monitors.AsNoTracking()
            .Where(monitor => monitor.Status == MonitorStatus.Active && monitor.AcquisitionId == null
                && monitor.EntityId != null && monitor.UpgradeChildAcquisitionId == null && kinds.Contains(monitor.Kind)
                && (entityId == null || monitor.EntityId == entityId)
                && (monitorId == null || monitor.Id == monitorId)
                && db.EntityFiles.Any(file => file.EntityId == monitor.EntityId && file.Role == EntityFileRole.Source))
            .Select(monitor => new { monitor.Id, EntityId = monitor.EntityId!.Value })
            .ToArrayAsync(cancellationToken);
        foreach (var candidate in candidates) {
            await lifecycle.ExecuteAsync(candidate.EntityId, async token => {
                var current = await db.Monitors.FirstOrDefaultAsync(row => row.Id == candidate.Id, token);
                if (current is null) return;
                await db.Entry(current).ReloadAsync(token);
                if (current.EntityId != candidate.EntityId) return;
                var baseline = await FindOwnedBaselineAsync(current, token)
                    ?? await RegisterLibraryBaselineAsync(current, policies, token);
                if (baseline is null) return;
                AttachOwnedBaseline(current, baseline);
                await db.SaveChangesAsync(token);
            }, cancellationToken);
        }
    }

    private async Task<AcquisitionRow?> FindOwnedBaselineAsync(MonitorRow monitor, CancellationToken cancellationToken) {
        if (monitor.Status != MonitorStatus.Active || monitor.AcquisitionId is not null || monitor.EntityId is not { } entityId
            || monitor.UpgradeChildAcquisitionId is not null || !MediaQualityLadder.IsUpgradeCapableKind(monitor.Kind)) return null;
        var source = await FindSingleOwnedSourceAsync(monitor, cancellationToken);
        if (source is null) return null;
        try {
            var file = new FileInfo(source.Path);
            var folder = file.DirectoryName;
            var baselines = await db.Acquisitions.Where(acquisition => acquisition.EntityId == entityId
                    && acquisition.Kind == monitor.Kind && acquisition.Status == AcquisitionStatus.Imported
                    && acquisition.UpgradeOfAcquisitionId == null && acquisition.UpgradeQualityCaptured
                    && (acquisition.FinalSourcePath == source.Path || acquisition.FinalSourcePath == folder))
                .Take(2).ToArrayAsync(cancellationToken);
            if (baselines.Length != 1) return null;
            var baseline = baselines[0];
            await db.Entry(baseline).ReloadAsync(cancellationToken);
            if (db.Entry(baseline).State == EntityState.Detached || baseline.EntityId != entityId
                || baseline.Kind != monitor.Kind || baseline.Status != AcquisitionStatus.Imported
                || !baseline.UpgradeQualityCaptured || baseline.FinalSourcePath is null) return null;
            var receiptFile = VideoUpgradeFileSelection.Find(baseline.FinalSourcePath!);
            if (receiptFile is null || !FileSystemPathComparison.Equals(Path.GetFullPath(receiptFile), file.FullName)
                || await db.Acquisitions.AsNoTracking().AnyAsync(child => child.UpgradeOfAcquisitionId == baseline.Id
                    && child.Status != AcquisitionStatus.Cancelled, cancellationToken)) return null;
            return baseline;
        } catch (IOException) {
            return null;
        } catch (UnauthorizedAccessException) {
            return null;
        } catch (ArgumentException) {
            return null;
        } catch (NotSupportedException) {
            return null;
        }
    }

    private async Task<EntityFileRow?> FindSingleOwnedSourceAsync(MonitorRow monitor, CancellationToken cancellationToken) {
        if (monitor.Status != MonitorStatus.Active || monitor.AcquisitionId is not null
            || monitor.UpgradeChildAcquisitionId is not null || monitor.EntityId is not { } entityId
            || monitor.Kind is not (EntityKind.Movie or EntityKind.VideoEpisode)) return null;
        var kindCode = monitor.Kind.ToCode();
        if (!await db.Entities.AsNoTracking().AnyAsync(entity => entity.Id == entityId && entity.KindCode == kindCode, cancellationToken)) return null;
        var sources = await db.EntityFiles.AsNoTracking()
            .Where(file => file.EntityId == entityId && file.Role == EntityFileRole.Source).Take(2).ToArrayAsync(cancellationToken);
        if (sources.Length != 1) return null;
        var source = sources[0];
        if (await db.EntityFiles.AsNoTracking().AnyAsync(file => file.Role == EntityFileRole.Source
                && file.Path == source.Path && file.EntityId != entityId, cancellationToken)) return null;
        try {
            var file = new FileInfo(source.Path);
            return file.Exists && file.Length > 0 && !file.Attributes.HasFlag(FileAttributes.ReparsePoint)
                && (source.SizeBytes is not > 0 || source.SizeBytes == file.Length) ? source : null;
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
            return null;
        }
    }

    private async Task<AcquisitionRow?> RegisterLibraryBaselineAsync(MonitorRow monitor, UpgradePolicies policies, CancellationToken cancellationToken) {
        if (policies.Resolve(monitor.ProfileId, monitor.Kind) is not { AutoPick: true, UpgradeUntilCutoff: true }
            || await FindSingleOwnedSourceAsync(monitor, cancellationToken) is not { } source) return null;
        var entityId = source.EntityId;
        // Existing attempts, including uncertain import receipts, must follow their own recovery path.
        if (await db.Acquisitions.AsNoTracking().AnyAsync(row => row.EntityId == entityId
                && row.Status != AcquisitionStatus.Cancelled, cancellationToken)
            || await OwnedVideoEvidence.ReadResolutionAsync(db, entityId, cancellationToken) is not > 0
            || !await db.EntitySubtitleStates.AsNoTracking().AnyAsync(row => row.EntityId == entityId
                && row.SubtitlesExtractedAt != null, cancellationToken)) return null;
        var entity = await db.Entities.AsNoTracking().SingleAsync(row => row.Id == entityId, cancellationToken);
        string? seriesTitle = null;
        int? seasonNumber = null, episodeNumber = null;
        if (monitor.Kind == EntityKind.VideoEpisode) {
            var seasonCode = EntityKind.VideoSeason.ToCode();
            var seriesCode = EntityKind.VideoSeries.ToCode();
            var ancestry = await (from season in db.Entities.AsNoTracking()
                join series in db.Entities.AsNoTracking() on season.ParentEntityId equals series.Id
                where season.Id == entity.ParentEntityId && season.KindCode == seasonCode && series.KindCode == seriesCode
                select new { SeasonId = season.Id, SeriesTitle = series.Title }).SingleOrDefaultAsync(cancellationToken);
            if (ancestry is null || string.IsNullOrWhiteSpace(ancestry.SeriesTitle)) return null;
            var positions = await db.EntityPositions.AsNoTracking()
                .Where(position => position.EntityId == entityId || position.EntityId == ancestry.SeasonId)
                .ToArrayAsync(cancellationToken);
            seasonNumber = positions.FirstOrDefault(position => position.EntityId == entityId && position.Code == EntityPositionCodes.Season)?.Value
                ?? positions.FirstOrDefault(position => position.EntityId == ancestry.SeasonId && position.Code == EntityPositionCodes.Season)?.Value;
            episodeNumber = positions.FirstOrDefault(position => position.EntityId == entityId && position.Code == EntityPositionCodes.Episode)?.Value;
            if (seasonNumber is null or < 0 || episodeNumber is null or <= 0) return null;
            seriesTitle = ancestry.SeriesTitle;
        }
        var now = DateTimeOffset.UtcNow;
        var baseline = new AcquisitionRow {
            Id = Guid.NewGuid(), EntityId = entityId, Kind = monitor.Kind, Title = entity.Title,
            Series = seriesTitle, SeasonNumber = seasonNumber, EpisodeNumber = episodeNumber,
            ProfileId = monitor.ProfileId, TargetLibraryRootId = monitor.TargetLibraryRootId,
            Status = AcquisitionStatus.Imported, FinalSourcePath = source.Path,
            StatusMessage = "Monitoring an existing library file for quality upgrades.",
            // Dimensions prove resolution, not the release source or revision. Searches use the linked probe.
            OwnedMediaQuality = VideoQuality.Unknown.ToCode(), UpgradeQualityCaptured = true,
            CreatedAt = now, UpdatedAt = now
        };
        // Entity-backed search resolves current catalog identities and positions, including absolute numbering.
        db.Acquisitions.Add(baseline);
        return baseline;
    }

    private static void AttachOwnedBaseline(MonitorRow monitor, AcquisitionRow baseline) {
        var now = DateTimeOffset.UtcNow;
        monitor.AcquisitionId = baseline.Id;
        monitor.LastSearchedAt = null;
        monitor.BarrenSearches = 0;
        monitor.UpdatedAt = now;
        // A stored explicit choice wins. A bare monitor toggle retains the original import policy;
        // StartForEntity applies explicit resets to defaults after attaching this baseline.
        if (monitor.ProfileId is { } profileId) baseline.ProfileId = profileId;
        if (monitor.TargetLibraryRootId is { } rootId) baseline.TargetLibraryRootId = rootId;
        baseline.UpdatedAt = now;
    }
}
