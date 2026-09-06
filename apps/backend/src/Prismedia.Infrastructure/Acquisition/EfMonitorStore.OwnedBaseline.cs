using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

public sealed partial class EfMonitorStore {
    /// <summary>Repairs lost links to existing import receipts before selecting normal upgrade work; never fabricates an acquisition for a scanned file.</summary>
    private async Task RestoreOwnedBaselinesAsync(Guid? entityId, Guid? monitorId, CancellationToken cancellationToken) {
        var kinds = Enum.GetValues<EntityKind>().Where(MediaQualityLadder.IsUpgradeCapableKind).ToArray();
        var candidates = await db.Monitors.AsNoTracking()
            .Where(monitor => monitor.Status == MonitorStatus.Active && monitor.AcquisitionId == null
                && monitor.EntityId != null && monitor.UpgradeChildAcquisitionId == null && kinds.Contains(monitor.Kind)
                && (entityId == null || monitor.EntityId == entityId)
                && (monitorId == null || monitor.Id == monitorId)
                && db.Acquisitions.Any(acquisition => acquisition.EntityId == monitor.EntityId
                    && acquisition.Kind == monitor.Kind && acquisition.Status == AcquisitionStatus.Imported
                    && acquisition.UpgradeQualityCaptured && acquisition.FinalSourcePath != null))
            .Select(monitor => new { monitor.Id, EntityId = monitor.EntityId!.Value })
            .ToArrayAsync(cancellationToken);
        foreach (var candidate in candidates) {
            await lifecycle.ExecuteAsync(candidate.EntityId, async token => {
                var current = await db.Monitors.FirstOrDefaultAsync(row => row.Id == candidate.Id, token);
                if (current is null) return;
                await db.Entry(current).ReloadAsync(token);
                if (current.EntityId != candidate.EntityId || await FindOwnedBaselineAsync(current, token) is not { } baseline) return;
                AttachOwnedBaseline(current, baseline);
                await db.SaveChangesAsync(token);
            }, cancellationToken);
        }
    }

    private async Task<AcquisitionRow?> FindOwnedBaselineAsync(MonitorRow monitor, CancellationToken cancellationToken) {
        if (monitor.Status != MonitorStatus.Active || monitor.AcquisitionId is not null || monitor.EntityId is not { } entityId
            || monitor.UpgradeChildAcquisitionId is not null || !MediaQualityLadder.IsUpgradeCapableKind(monitor.Kind)) return null;
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
            if (!file.Exists || file.Length <= 0 || file.Attributes.HasFlag(FileAttributes.ReparsePoint)
                || source.SizeBytes is > 0 && source.SizeBytes != file.Length) return null;
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
