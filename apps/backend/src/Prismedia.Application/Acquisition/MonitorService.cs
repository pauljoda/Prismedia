using Prismedia.Application.Requests;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Application use case for monitors: start/stop/pause/resume monitoring of a wanted acquisition or a
/// library container entity, and list the monitored items. Starting a monitor denormalizes the target's
/// title onto the monitor so the monitored list and re-search labels stand alone.
/// </summary>
public sealed class MonitorService(IMonitorStore monitors, IAcquisitionStore acquisitions, IWantedEntityWriter entities) {
    public Task<IReadOnlyList<MonitorView>> ListAsync(CancellationToken cancellationToken) =>
        monitors.ListAsync(cancellationToken);

    /// <summary>Starts (or re-activates) monitoring of an existing acquisition. Returns null when the acquisition does not exist.</summary>
    public async Task<MonitorView?> StartAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var detail = await acquisitions.GetAsync(acquisitionId, cancellationToken);
        if (detail is null) {
            return null;
        }

        var summary = detail.Summary;
        return await monitors.StartAsync(acquisitionId, summary.Kind, summary.Title, summary.Author, cancellationToken);
    }

    /// <summary>
    /// Starts (or re-activates) a container monitor watching a library entity (an author, an artist) for
    /// new works. Works for wanted placeholders and real scanned-in entities alike, as long as the
    /// entity carries a provider identity the daily sync can re-resolve it from (a scanned-in author
    /// gains one the moment Identify runs). Returns null when the entity is missing, isn't a monitorable
    /// container kind, or has no provider identity yet.
    /// </summary>
    public async Task<MonitorView?> StartForEntityAsync(Guid entityId, CancellationToken cancellationToken) {
        var container = await entities.GetContainerAsync(entityId, cancellationToken);
        if (container is null || container.ProviderIds.Count == 0) {
            return null;
        }

        var monitorable = RequestKindRegistry.All.Any(descriptor =>
            descriptor is { IsContainer: true, Committable: true } && descriptor.WantedEntityKind == container.Kind);
        if (!monitorable) {
            return null;
        }

        return await monitors.StartForEntityAsync(entityId, container.Kind, container.Title, targeting: null, cancellationToken);
    }

    /// <summary>The container monitor watching an entity, or null when it is not monitored.</summary>
    public Task<MonitorView?> GetForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        monitors.GetByEntityAsync(entityId, cancellationToken);

    /// <summary>
    /// Stamps the entity's container monitor as just-searched after a manual sync, so the daily sweep's
    /// clock restarts from now instead of double-syncing. A no-op when the entity isn't monitored.
    /// </summary>
    public async Task MarkEntitySearchedAsync(Guid entityId, CancellationToken cancellationToken) {
        var monitor = await monitors.GetByEntityAsync(entityId, cancellationToken);
        if (monitor is not null) {
            await monitors.MarkSearchedAsync(monitor.Id, cancellationToken);
        }
    }

    public Task<bool> StopAsync(Guid monitorId, CancellationToken cancellationToken) =>
        monitors.DeleteAsync(monitorId, cancellationToken);

    public Task<bool> PauseAsync(Guid monitorId, CancellationToken cancellationToken) =>
        monitors.SetStatusAsync(monitorId, MonitorStatus.Paused, cancellationToken);

    public Task<bool> ResumeAsync(Guid monitorId, CancellationToken cancellationToken) =>
        monitors.SetStatusAsync(monitorId, MonitorStatus.Active, cancellationToken);
}
