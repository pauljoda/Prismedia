using Prismedia.Application.Requests;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Application use case for monitors: start/stop/pause/resume monitoring of a wanted acquisition or a
/// library container entity, and list the monitored items. Starting a monitor denormalizes the target's
/// title onto the monitor so the monitored list and re-search labels stand alone.
/// </summary>
public sealed class MonitorService(IMonitorStore monitors, IAcquisitionStore acquisitions, IWantedEntityWriter entities, IProviderTrackingCatalog tracking) {
    public Task<IReadOnlyList<MonitorView>> ListAsync(CancellationToken cancellationToken) =>
        monitors.ListAsync(cancellationToken);

    /// <summary>
    /// A page of the Wanted "Missing" list: monitored items not yet in hand (an active per-item monitor
    /// whose acquisition is not imported, or whose acquisition is gone), newest-monitor-first. See
    /// <see cref="IMonitorStore.ListMissingAsync"/> for the paging/filter/clamp semantics.
    /// </summary>
    public async Task<WantedPageView> ListMissingAsync(int page, int pageSize, EntityKind? kind, CancellationToken cancellationToken) =>
        ToView(await monitors.ListMissingAsync(page, pageSize, kind, cancellationToken));

    /// <summary>
    /// A page of the Wanted "Cutoff Unmet" list: monitored items in hand but below their kind's cutoff,
    /// newest-monitor-first. See <see cref="IMonitorStore.ListCutoffUnmetAsync"/> for the paging semantics
    /// and why the page total is an upper bound.
    /// </summary>
    public async Task<WantedPageView> ListCutoffUnmetAsync(int page, int pageSize, EntityKind? kind, CancellationToken cancellationToken) =>
        ToView(await monitors.ListCutoffUnmetAsync(page, pageSize, kind, cancellationToken));

    private static WantedPageView ToView(WantedPage page) =>
        new(page.Items.Select(item => new WantedListItemView(
            item.MonitorId,
            item.AcquisitionId,
            item.EntityId,
            item.Kind,
            item.Title,
            item.MonitorStatus,
            item.AcquisitionStatus,
            item.LastSearchedAt,
            item.NextSearchAt,
            item.OwnedQuality,
            item.CutoffQuality,
            item.BarrenSearches,
            item.PosterUrl,
            item.Author)).ToArray(), page.Total);

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
    /// entity carries a provider identity an enabled metadata plugin can track — re-resolve by id on the
    /// daily sync (a scanned-in author gains one the moment Identify runs). Returns null when the entity
    /// is missing, isn't a monitorable container kind, or no plugin can track its provider identities.
    /// </summary>
    public async Task<MonitorView?> StartForEntityAsync(Guid entityId, MonitorPreset? preset, CancellationToken cancellationToken) {
        var (container, trackable) = await ResolveEligibilityAsync(entityId, cancellationToken);
        if (container is null || trackable.Count == 0) {
            return null;
        }

        // A null preset (the bare monitor toggle) keeps whatever preset a prior request recorded, or the All
        // default for a fresh container — so a hand toggle never narrows an author's discovery scope. A
        // caller that passes a preset (choosing one on the series page) records it.
        return await monitors.StartForEntityAsync(entityId, container.Kind, container.Title, targeting: null, preset, cancellationToken);
    }

    /// <summary>
    /// Whether the entity can carry a standing container monitor: it must be a monitorable container kind
    /// and hold a provider identity some enabled plugin can track (lookup by id). The trackable provider
    /// ids are surfaced so the UI can say which plugin identity the watch would ride on.
    /// </summary>
    public async Task<MonitorEligibilityView> GetEligibilityAsync(Guid entityId, CancellationToken cancellationToken) {
        var (_, trackable) = await ResolveEligibilityAsync(entityId, cancellationToken);
        return new MonitorEligibilityView(trackable.Count > 0, trackable);
    }

    /// <summary>
    /// Loads the entity as a monitorable container and the subset of its provider identities an enabled
    /// plugin can track. The container is null (and the list empty) when the entity is missing or isn't
    /// a monitorable container kind.
    /// </summary>
    private async Task<(MonitorableContainer? Container, IReadOnlyList<string> Trackable)> ResolveEligibilityAsync(
        Guid entityId, CancellationToken cancellationToken) {
        var container = await entities.GetContainerAsync(entityId, cancellationToken);
        if (container is null || container.ProviderIds.Count == 0) {
            return (null, []);
        }

        var descriptor = RequestKindRegistry.All.FirstOrDefault(candidate =>
            candidate is { IsContainer: true, Committable: true } && candidate.WantedEntityKind == container.Kind);
        if (descriptor is null) {
            return (null, []);
        }

        var trackable = await tracking.TrackableProvidersAsync(descriptor.PluginKindCode, container.ProviderIds, cancellationToken);
        return (container, trackable);
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
