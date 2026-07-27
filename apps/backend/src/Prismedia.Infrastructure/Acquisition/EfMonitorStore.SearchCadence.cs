using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Search-cadence timestamp mutations for <see cref="EfMonitorStore"/>.</summary>
public sealed partial class EfMonitorStore {
    public async Task MarkSearchedAsync(Guid monitorId, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            await db.Monitors
                .Where(monitor => monitor.Id == monitorId
                    && monitor.Status != MonitorStatus.Stopping
                    && monitor.Status != MonitorStatus.DeletingFiles)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(monitor => monitor.LastSearchedAt, now)
                    .SetProperty(monitor => monitor.UpdatedAt, now), cancellationToken);
            return;
        }

        var row = await db.Monitors.FirstOrDefaultAsync(
            monitor => monitor.Id == monitorId
                && monitor.Status != MonitorStatus.Stopping
                && monitor.Status != MonitorStatus.DeletingFiles,
            cancellationToken);
        if (row is null) {
            return;
        }

        row.LastSearchedAt = now;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkSearchDueByAcquisitionAsync(
        Guid acquisitionId,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            await db.Monitors
                .Where(row => row.AcquisitionId == acquisitionId && row.Status == MonitorStatus.Active)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.LastSearchedAt, (DateTimeOffset?)null)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken);
            return;
        }

        var monitor = await db.Monitors.FirstOrDefaultAsync(
            row => row.AcquisitionId == acquisitionId && row.Status == MonitorStatus.Active,
            cancellationToken);
        if (monitor is null) {
            return;
        }

        monitor.LastSearchedAt = null;
        monitor.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }
}
