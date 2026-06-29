using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Application use case for monitors: start/stop/pause/resume monitoring of a wanted acquisition and list
/// the monitored items. Starting a monitor denormalizes the acquisition's title/author onto the monitor so
/// the monitored list and re-search labels stand alone.
/// </summary>
public sealed class MonitorService(IMonitorStore monitors, IAcquisitionStore acquisitions) {
    public Task<IReadOnlyList<MonitorView>> ListAsync(CancellationToken cancellationToken) =>
        monitors.ListAsync(cancellationToken);

    /// <summary>Starts (or re-activates) monitoring of an existing acquisition. Returns null when the acquisition does not exist.</summary>
    public async Task<MonitorView?> StartAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var detail = await acquisitions.GetAsync(acquisitionId, cancellationToken);
        if (detail is null) {
            return null;
        }

        var summary = detail.Summary;
        return await monitors.StartAsync(acquisitionId, EntityKind.Book, summary.Title, summary.Author, cancellationToken);
    }

    public Task<bool> StopAsync(Guid monitorId, CancellationToken cancellationToken) =>
        monitors.DeleteAsync(monitorId, cancellationToken);

    public Task<bool> PauseAsync(Guid monitorId, CancellationToken cancellationToken) =>
        monitors.SetStatusAsync(monitorId, MonitorStatus.Paused, cancellationToken);

    public Task<bool> ResumeAsync(Guid monitorId, CancellationToken cancellationToken) =>
        monitors.SetStatusAsync(monitorId, MonitorStatus.Active, cancellationToken);
}
