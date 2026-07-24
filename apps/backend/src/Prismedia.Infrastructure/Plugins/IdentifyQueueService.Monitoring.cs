using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Plugins;

public sealed partial class IdentifyQueueService {
    /// <summary>
    /// A reviewed provider change can reveal structural works that Identify intentionally did not invent
    /// because they have no local descendants. An active parent monitor owns that missing-content intent,
    /// so hand the refreshed provider route to its exact background maintenance job immediately.
    /// </summary>
    private async Task QueueMonitoredRefreshAsync(
        Guid entityId,
        string title,
        CancellationToken cancellationToken) {
        var activelyMonitored = await _db.Monitors
            .AsNoTracking()
            .AnyAsync(
                monitor => monitor.EntityId == entityId && monitor.Status == MonitorStatus.Active,
                cancellationToken);
        if (!activelyMonitored) {
            return;
        }

        await _jobs.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.MonitoredSearch,
                TargetEntityKind: JobTargetKinds.Entity,
                TargetEntityId: entityId.ToString(),
                TargetLabel: $"Check monitored content for {title}"),
            cancellationToken);
    }
}
