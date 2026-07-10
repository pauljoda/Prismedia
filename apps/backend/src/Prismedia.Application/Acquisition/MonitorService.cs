using Prismedia.Application.Requests;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Application use case for stable Entity monitoring: start/stop/pause/resume the durable intent, attach
/// transient acquisition work when needed, and list monitored items. Starting a monitor denormalizes the
/// target's title so the monitored list and re-search labels stand alone.
/// </summary>
public sealed class MonitorService(
    IMonitorStore monitors,
    IAcquisitionStore acquisitions,
    IWantedEntityWriter entities,
    IProviderTrackingCatalog tracking,
    EntityUnmonitorService unmonitoring,
    IWantedSuppressionStore suppressions) {
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
        if (summary.EntityId is not { } entityId) {
            return await monitors.StartAsync(
                acquisitionId,
                summary.Kind,
                summary.Title,
                summary.Author,
                cancellationToken);
        }

        MonitorView? monitor = null;
        var accepted = await monitors.ExecuteIfEntityLifecycleMutableAsync(
            entityId,
            async leaseCancellationToken => monitor = await monitors.StartAsync(
                acquisitionId,
                summary.Kind,
                summary.Title,
                summary.Author,
                leaseCancellationToken),
            cancellationToken);
        if (!accepted) {
            throw LifecycleConflict();
        }
        return monitor;
    }

    /// <summary>
    /// Starts (or re-activates) the stable monitor for a library Entity. Grouping Entities discover
    /// children; leaves retain on/off intent and attach acquisition work when required by their registry
    /// descriptor. Wanted placeholders and source-backed Entities use the same path. Returns null when the
    /// Entity is missing, its kind is not committable, or no enabled plugin can track its provider route.
    /// </summary>
    public async Task<MonitorView?> StartForEntityAsync(Guid entityId, MonitorPreset? preset, CancellationToken cancellationToken) {
        var (entity, trackable) = await ResolveEligibilityAsync(entityId, cancellationToken);
        if (entity is null || trackable.Count == 0) {
            return null;
        }
        // A null preset (the bare monitor toggle) keeps whatever preset a prior request recorded, or the All
        // default for a fresh container — so a hand toggle never narrows an author's discovery scope. A
        // caller that passes a preset (choosing one on the series page) records it.
        MonitorView? monitor = null;
        var accepted = await monitors.ExecuteIfEntityLifecycleMutableAsync(
            entityId,
            async leaseCancellationToken => {
                // Re-read the authoritative provider binding inside the stable Entity lease. A stale
                // preflight must not publish intent for a route that changed while deletion was claiming.
                var (currentEntity, currentTrackable) = await ResolveEligibilityAsync(
                    entityId,
                    leaseCancellationToken);
                if (currentEntity is null || currentTrackable.Count == 0) {
                    return;
                }

                monitor = await monitors.StartForEntityAsync(
                    entityId,
                    currentEntity.Kind,
                    currentEntity.Title,
                    targeting: null,
                    preset,
                    leaseCancellationToken);
                if (monitor.Status == MonitorStatus.Active) {
                    // Explicitly monitoring the root again removes its child-off override. Descendants
                    // were never suppressed, so a parent monitor can rediscover them on its next sync.
                    await suppressions.ClearAsync(
                        currentEntity.ExternalIdentities,
                        leaseCancellationToken);
                }
            },
            cancellationToken);
        if (!accepted) {
            throw LifecycleConflict();
        }

        return monitor;
    }

    /// <summary>
    /// Whether the Entity can carry a stable monitor: its registry kind must be committable and its
    /// authoritative provider identity must be trackable by an enabled plugin. The provider list lets the
    /// UI explain which plugin owns the durable identity route.
    /// </summary>
    public async Task<MonitorEligibilityView> GetEligibilityAsync(Guid entityId, CancellationToken cancellationToken) {
        var (entity, trackable) = await ResolveEligibilityAsync(entityId, cancellationToken);
        var descriptor = entity is null
            ? null
            : RequestKindRegistry.All.FirstOrDefault(candidate =>
                candidate.Committable && candidate.WantedEntityKind == entity.Kind);
        var childDescriptor = descriptor is null ? null : RequestKindRegistry.ChildOf(descriptor);
        var canSearchMissingChildren = descriptor is not null
            && RequestKindRegistry.CanSearchMissingChildren(descriptor);
        return new MonitorEligibilityView(
            descriptor is not null && trackable.Count > 0,
            trackable,
            descriptor?.IsContainer ?? false,
            canSearchMissingChildren,
            canSearchMissingChildren ? childDescriptor!.WantedEntityKind : null);
    }

    /// <summary>
    /// Returns the bounded direct monitoring snapshot for the requested Entity ids. Entity/identity,
    /// monitor, and latest-acquisition persistence are each batch-loaded; plugin route validation is
    /// delegated as one catalog batch. Input order is preserved after de-duplication, including missing ids.
    /// </summary>
    public async Task<IReadOnlyList<EntityMonitorStateView>> GetStatesAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        var requestedIds = entityIds.Distinct().ToArray();
        if (requestedIds.Length == 0) {
            return [];
        }

        // All three production adapters share this scoped unit of work. Keep the bounded queries sequential:
        // EF Core forbids concurrent operations on one DbContext, while this remains three SQL reads rather
        // than an Entity-count-dependent N+1.
        var eligibilityEntities = await entities.ListMonitorEligibilityEntitiesAsync(
            requestedIds,
            cancellationToken);
        var monitorByEntity = await monitors.ListByEntityIdsAsync(requestedIds, cancellationToken);
        var acquisitionByEntity = await acquisitions.ListLatestSummariesForEntityIdsAsync(
            requestedIds,
            cancellationToken);
        var descriptors = eligibilityEntities.Values.ToDictionary(
            entity => entity.EntityId,
            entity => RequestKindRegistry.All.FirstOrDefault(candidate =>
                candidate.Committable && candidate.WantedEntityKind == entity.Kind));
        var trackingQueries = eligibilityEntities.Values
            .Where(entity => descriptors.GetValueOrDefault(entity.EntityId) is not null)
            .Select(entity => new ProviderTrackingQuery(
                entity.EntityId,
                descriptors[entity.EntityId]!.PluginKindCode,
                entity.ExternalIdentities,
                entity.ProviderIdentity))
            .ToArray();
        var trackableByEntity = await tracking.TrackableProvidersBatchAsync(
            trackingQueries,
            cancellationToken);
        return requestedIds.Select(entityId => {
            var descriptor = descriptors.GetValueOrDefault(entityId);
            var childDescriptor = descriptor is null ? null : RequestKindRegistry.ChildOf(descriptor);
            var canSearchMissingChildren = descriptor is not null
                && RequestKindRegistry.CanSearchMissingChildren(descriptor);
            var eligibilityEntity = eligibilityEntities.GetValueOrDefault(entityId);
            var trackable = trackableByEntity.GetValueOrDefault(entityId) ?? [];
            return new EntityMonitorStateView(
                entityId,
                descriptor is not null && trackable.Count > 0,
                eligibilityEntity?.IsWanted == true && descriptor?.Committable == true,
                trackable,
                descriptor?.IsContainer ?? false,
                canSearchMissingChildren,
                canSearchMissingChildren ? childDescriptor!.WantedEntityKind : null,
                monitorByEntity.GetValueOrDefault(entityId),
                acquisitionByEntity.GetValueOrDefault(entityId));
        }).ToArray();
    }

    /// <summary>
    /// Loads the monitorable Entity and validates its authoritative provider route. Monitoring fails
    /// closed when the stable plugin + identity binding is absent; the Entity is also ineligible when it
    /// is missing or its registry kind is not committable.
    /// </summary>
    private async Task<(MonitorableEntity? Entity, IReadOnlyList<string> Trackable)> ResolveEligibilityAsync(
        Guid entityId, CancellationToken cancellationToken) {
        var entity = await entities.GetEntityAsync(entityId, cancellationToken);
        if (entity is null) {
            return (null, []);
        }

        var descriptor = RequestKindRegistry.All.FirstOrDefault(candidate =>
            candidate.Committable && candidate.WantedEntityKind == entity.Kind);
        if (descriptor is null || entity.ProviderIdentity is null) {
            return (entity, []);
        }

        var trackable = await tracking.TrackableProvidersAsync(
            descriptor.PluginKindCode,
            entity.ExternalIdentities,
            entity.ProviderIdentity,
            cancellationToken);
        return (entity, trackable);
    }

    /// <summary>The stable monitor targeting an Entity, or null when it is not monitored.</summary>
    public Task<MonitorView?> GetForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        monitors.GetByEntityAsync(entityId, cancellationToken);

    /// <summary>
    /// Stamps the Entity monitor as just-searched after manual work, so the sweep's clock restarts from
    /// now instead of immediately repeating it. A no-op when the Entity is not monitored.
    /// </summary>
    public async Task MarkEntitySearchedAsync(Guid entityId, CancellationToken cancellationToken) {
        var monitor = await monitors.GetByEntityAsync(entityId, cancellationToken);
        if (monitor is not null) {
            await monitors.MarkSearchedAsync(monitor.Id, cancellationToken);
        }
    }

    /// <summary>
    /// Stops monitoring the target Entity subtree and removes all acquisition-only state it governed.
    /// Source-backed Entities/files remain; an unsafe active import rejects the whole operation before
    /// anything is claimed or deleted.
    /// </summary>
    public Task<MonitorStopResult> StopAsync(Guid monitorId, CancellationToken cancellationToken) =>
        unmonitoring.StopAsync(monitorId, cancellationToken);

    public Task<bool> PauseAsync(Guid monitorId, CancellationToken cancellationToken) =>
        SetExplicitStatusAsync(monitorId, MonitorStatus.Paused, cancellationToken);

    public Task<bool> ResumeAsync(Guid monitorId, CancellationToken cancellationToken) =>
        SetExplicitStatusAsync(monitorId, MonitorStatus.Active, cancellationToken);

    private async Task<bool> SetExplicitStatusAsync(
        Guid monitorId,
        MonitorStatus status,
        CancellationToken cancellationToken) {
        var monitor = (await monitors.ListAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.Id == monitorId);
        if (monitor is null) {
            return false;
        }
        if (monitor.EntityId is not { } entityId) {
            return await monitors.SetStatusAsync(monitorId, status, cancellationToken);
        }

        var changed = false;
        var accepted = await monitors.ExecuteIfEntityLifecycleMutableAsync(
            entityId,
            async leaseCancellationToken => changed = await monitors.SetStatusAsync(
                monitorId,
                status,
                leaseCancellationToken),
            cancellationToken);
        return accepted && changed;
    }

    private static AcquisitionConfigurationException LifecycleConflict() =>
        new(
            Prismedia.Contracts.System.ApiProblemCodes.AcquisitionInvalid,
            "This Entity is being cleaned up. Wait for that operation to finish, then try again.");
}
