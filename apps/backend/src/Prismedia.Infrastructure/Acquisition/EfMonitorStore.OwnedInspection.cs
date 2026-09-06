using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

public sealed partial class EfMonitorStore {
    /// <inheritdoc />
    public async Task<OwnedVideoInspectionNeeds?> GetOwnedVideoInspectionNeedsAsync(Guid monitorId, CancellationToken cancellationToken) {
        var monitor = await db.Monitors.AsNoTracking().SingleOrDefaultAsync(row => row.Id == monitorId, cancellationToken);
        if (monitor is null || (await ResolveUpgradePoliciesAsync(cancellationToken)).Resolve(monitor.ProfileId, monitor.Kind)
                is not { AutoPick: true, UpgradeUntilCutoff: true }
            || await FindSingleOwnedSourceAsync(monitor, cancellationToken) is not { SizeBytes: > 0 } source) return null;
        if (await db.Acquisitions.AsNoTracking().AnyAsync(row => row.EntityId == source.EntityId
                && row.Status != AcquisitionStatus.Cancelled, cancellationToken)
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
