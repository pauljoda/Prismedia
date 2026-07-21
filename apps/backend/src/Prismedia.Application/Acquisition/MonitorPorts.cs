using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// One stable Entity-monitor intent recorded before its transient acquisition work is published.
/// Container request fan-out batches these intents so every explicit child selection is durable and
/// visible while structural metadata is still being hydrated.
/// </summary>
public sealed record EntityMonitorStart(
    Guid EntityId,
    EntityKind Kind,
    string Title,
    AcquisitionTargeting? Targeting,
    MonitorPreset? Preset);

/// <summary>
/// Persistence port for monitors — stable Entity intents that may attach transient acquisitions. The due
/// and reconciliation logic detaches completed work while keeping Entity-linked intent Active; legacy rows
/// without EntityId retain terminal compatibility behavior.
/// </summary>
public interface IMonitorStore {
    /// <summary>Starts acquisition work on its stable Entity monitor and reuses legacy acquisition intent.</summary>
    Task<Contracts.Acquisition.MonitorView> StartAsync(
        Guid acquisitionId,
        EntityKind kind,
        string title,
        string? author,
        CancellationToken cancellationToken);

    /// <summary>
    /// Starts or reactivates an Entity monitor. Explicit targeting and preset values replace stored choices;
    /// null values preserve them so a provider sync never narrows user intent.
    /// </summary>
    Task<Contracts.Acquisition.MonitorView> StartForEntityAsync(
        Guid entityId,
        EntityKind kind,
        string title,
        AcquisitionTargeting? targeting,
        MonitorPreset? preset,
        CancellationToken cancellationToken);

    /// <summary>
    /// Starts multiple stable Entity monitors as one intent boundary. Production adapters should persist
    /// the batch atomically; the default preserves compatibility for focused test adapters.
    /// </summary>
    async Task<IReadOnlyList<Contracts.Acquisition.MonitorView>> StartForEntitiesAsync(
        IReadOnlyCollection<EntityMonitorStart> starts,
        CancellationToken cancellationToken) {
        var monitors = new List<Contracts.Acquisition.MonitorView>(starts.Count);
        foreach (var start in starts.DistinctBy(candidate => candidate.EntityId)) {
            monitors.Add(await StartForEntityAsync(
                start.EntityId,
                start.Kind,
                start.Title,
                start.Targeting,
                start.Preset,
                cancellationToken));
        }
        return monitors;
    }

    /// <summary>Returns the stable monitor targeting an Entity, including legacy acquisition-linked rows.</summary>
    Task<Contracts.Acquisition.MonitorView?> GetByEntityAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>Every parallel monitor targeting an Entity; defaults to the legacy single-monitor read.</summary>
    async Task<IReadOnlyList<Contracts.Acquisition.MonitorView>> ListForEntityAsync(
        Guid entityId,
        CancellationToken cancellationToken) =>
        await GetByEntityAsync(entityId, cancellationToken) is { } monitor ? [monitor] : [];

    /// <summary>Returns the stable monitor for one independently monitored Book rendition.</summary>
    Task<Contracts.Acquisition.MonitorView?> GetByEntityAsync(
        Guid entityId,
        BookRendition? bookRendition,
        CancellationToken cancellationToken) =>
        GetByEntityAsync(entityId, cancellationToken);

    /// <summary>Whether the exact monitor row is currently Active.</summary>
    async Task<bool> IsActiveAsync(Guid monitorId, CancellationToken cancellationToken) =>
        (await ListAsync(cancellationToken)).Any(monitor =>
            monitor.Id == monitorId && monitor.Status == MonitorStatus.Active);

    /// <summary>Runs Entity materialization only while holding the direct Active monitor's mutation lease.</summary>
    async Task<bool> ExecuteIfActiveEntityMutationAsync(
        Guid entityId,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken) {
        if (await GetByEntityAsync(entityId, cancellationToken) is not { Status: MonitorStatus.Active }) {
            return false;
        }
        await mutation(cancellationToken);
        return true;
    }

    /// <summary>Runs explicit user intent only when no destructive Entity lifecycle claim owns it.</summary>
    async Task<bool> ExecuteIfEntityLifecycleMutableAsync(
        Guid entityId,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken) {
        if (await GetByEntityAsync(entityId, cancellationToken) is {
                Status: MonitorStatus.Stopping or MonitorStatus.DeletingFiles
            }) {
            return false;
        }
        await mutation(cancellationToken);
        return true;
    }

    /// <summary>Direct or legacy monitor per requested Entity id, loaded as one bounded read.</summary>
    async Task<IReadOnlyDictionary<Guid, Contracts.Acquisition.MonitorView>> ListByEntityIdsAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        var result = new Dictionary<Guid, Contracts.Acquisition.MonitorView>();
        foreach (var entityId in entityIds.Distinct()) {
            if (await GetByEntityAsync(entityId, cancellationToken) is { } monitor) {
                result[entityId] = monitor;
            }
        }
        return result;
    }

    /// <summary>The request-time library/profile choices stored on an Entity monitor, or null when absent.</summary>
    Task<AcquisitionTargeting?> GetTargetingByEntityAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>The grouping preset stored on an Entity monitor, or null when the Entity is not monitored.</summary>
    Task<MonitorPreset?> GetPresetByEntityAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>Low-level monitor removal for internal lifecycle maintenance.</summary>
    Task<bool> DeleteAsync(Guid monitorId, CancellationToken cancellationToken);

    /// <summary>Re-points a monitor at replacement acquisition work and reactivates it.</summary>
    Task<bool> RetargetAsync(Guid fromAcquisitionId, Guid toAcquisitionId, CancellationToken cancellationToken);

    /// <summary>Completes a managed file-deletion reacquisition with a compare-and-swap in production.</summary>
    Task<bool> RetargetAfterFileDeletionAsync(
        Guid fromAcquisitionId,
        Guid toAcquisitionId,
        CancellationToken cancellationToken) =>
        RetargetAsync(fromAcquisitionId, toAcquisitionId, cancellationToken);

    /// <summary>Sets a monitor's status. Returns false when it no longer exists.</summary>
    Task<bool> SetStatusAsync(Guid monitorId, MonitorStatus status, CancellationToken cancellationToken);

    /// <summary>Lists all monitors with each linked acquisition's status.</summary>
    Task<IReadOnlyList<Contracts.Acquisition.MonitorView>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Returns one SQL-paged Wanted Missing surface.</summary>
    Task<WantedPage> ListMissingAsync(
        int page,
        int pageSize,
        EntityKind? kind,
        CancellationToken cancellationToken);

    /// <summary>Returns one SQL-paged Wanted Cutoff Unmet surface.</summary>
    Task<WantedPage> ListCutoffUnmetAsync(
        int page,
        int pageSize,
        EntityKind? kind,
        CancellationToken cancellationToken);

    /// <summary>Returns the monitor linked to an acquisition, or null when it is not monitored.</summary>
    Task<Contracts.Acquisition.MonitorView?> GetByAcquisitionAsync(
        Guid acquisitionId,
        CancellationToken cancellationToken);

    /// <summary>Cheap scheduler gate for any active monitor with live acquisition work.</summary>
    Task<bool> HasActiveMonitorsAsync(CancellationToken cancellationToken);

    /// <summary>Reconciles monitors and returns those due for another search.</summary>
    Task<IReadOnlyList<DueMonitor>> ListDueMonitorsAsync(
        int defaultIntervalMinutes,
        CancellationToken cancellationToken);

    /// <summary>Stamps a monitor as just searched.</summary>
    Task MarkSearchedAsync(Guid monitorId, CancellationToken cancellationToken);

    /// <summary>Makes an active acquisition monitor immediately eligible for another sweep.</summary>
    Task MarkSearchDueByAcquisitionAsync(Guid acquisitionId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>Claims the monitor's one upgrade slot and creates its child acquisition.</summary>
    Task<Guid?> CreateUpgradeChildAsync(Guid monitorId, CancellationToken cancellationToken);

    /// <summary>Releases an upgrade slot and records whether its replacement succeeded.</summary>
    Task ResolveUpgradeChildAsync(Guid childId, bool succeeded, CancellationToken cancellationToken);
}
