using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

public sealed partial class EfMonitorStore {
    /// <inheritdoc />
    public async Task<OwnedVideoInspectionNeeds?> GetOwnedVideoInspectionNeedsAsync(Guid monitorId, CancellationToken cancellationToken) {
        var monitor = await db.Monitors.AsNoTracking().SingleOrDefaultAsync(row => row.Id == monitorId, cancellationToken);
        if (monitor is null) return null;
        var baseline = monitor.AcquisitionId is { } baselineId
            ? await db.Acquisitions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == baselineId, cancellationToken) : null;
        if (monitor.AcquisitionId is not null && (baseline is null || baseline.Status != AcquisitionStatus.Imported
                || baseline.EntityId != monitor.EntityId || baseline.Kind != monitor.Kind
                || baseline.UpgradeOfAcquisitionId != null || !baseline.UpgradeQualityCaptured)) return null;
        if ((await ResolveUpgradePoliciesAsync(cancellationToken)).Resolve(baseline is null ? monitor.ProfileId : baseline.ProfileId, monitor.Kind)
                is not { AutoPick: true, UpgradeUntilCutoff: true }
            || await FindSingleOwnedSourceAsync(monitor, cancellationToken) is not { SizeBytes: > 0 } source) return null;
        if (baseline is not null) {
            try {
                var receipt = string.IsNullOrWhiteSpace(baseline.FinalSourcePath) ? null : VideoUpgradeFileSelection.Find(baseline.FinalSourcePath);
                if (receipt is null || !FileSystemPathComparison.Equals(Path.GetFullPath(receipt), Path.GetFullPath(source.Path))) return null;
            } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
                return null;
            }
        }
        if (await db.Acquisitions.AsNoTracking().AnyAsync(row => row.EntityId == source.EntityId
                && row.Id != monitor.AcquisitionId && row.Status != AcquisitionStatus.Cancelled, cancellationToken)
            || await db.EntityTechnical.AsNoTracking().AnyAsync(row => row.EntityId == source.EntityId
                && row.ProbeFailedAt != null, cancellationToken)) return null;
        var probes = await db.MediaSources.AsNoTracking().Where(row => row.EntityId == source.EntityId
                && row.EntityFileId == source.Id && row.Path == source.Path && row.SizeBytes == source.SizeBytes)
            .Select(row => new { row.Width, row.Height }).Take(2).ToArrayAsync(cancellationToken);
        // Repeating a successful probe that found no video does not make it usable upgrade evidence.
        if (probes.Length > 1 || probes.Length == 1 && VideoPayloadProfileValidation.ResolutionTier(probes[0].Width, probes[0].Height) is null) return null;
        var probeRequired = probes.Length == 0;
        var subtitlesRequired = probeRequired || !await db.EntitySubtitleStates.AsNoTracking()
            .AnyAsync(row => row.EntityId == source.EntityId && row.SubtitlesExtractedAt != null, cancellationToken);
        return probeRequired || subtitlesRequired ? new(probeRequired, subtitlesRequired) : null;
    }
}
