using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

public sealed partial class RequestCommitService {
    /// <summary>
    /// Re-syncs a monitored container entity from its provider: resolves its direct child catalog, then
    /// materializes the works selected by the container's discovery policy through the same monitored
    /// acquisition path as a direct child toggle. Per-child suppression remains authoritative, so an
    /// explicitly unmonitored work does not reappear under an All or Future parent.
    /// </summary>
    public async Task<bool> SyncContainerAsync(Guid entityId, CancellationToken cancellationToken) {
        var container = await wanted.GetEntityAsync(entityId, cancellationToken);
        if (container?.ProviderIdentity is not { } route) {
            return false;
        }

        var descriptor = RequestKindRegistry.All.FirstOrDefault(candidate =>
            candidate is { IsContainer: true, Committable: true }
            && candidate.WantedEntityKind == container.Kind);
        if (descriptor is null) {
            return false;
        }

        // Only All and Future adopt newly discovered works. Missing and None retain their already selected
        // children. A legacy monitor with no stored preset keeps the original All behavior.
        var preset = await monitors.GetPresetByEntityAsync(entityId, cancellationToken) ?? MonitorPreset.All;
        var autoMonitorsNewWorks = preset is MonitorPreset.All or MonitorPreset.Future;
        var targeting = await monitors.GetTargetingByEntityAsync(entityId, cancellationToken)
            ?? AcquisitionTargeting.None;

        // Conservative SFW default: the background sweep has no user session.
        var review = await proposals.ResolveFreshDiscoveryAsync(
            descriptor, route, hideNsfw: true, cancellationToken);
        if (review?.Proposal is not { Patch: not null } proposal) {
            return false;
        }

        var selectedChildren = autoMonitorsNewWorks
            ? ResolveReviewedStructuralChildren(proposal, review.Targets)
            : [];

        // Provider resolution can be slow. Materialization runs under the exact direct monitor's active
        // lease, so recursive unmonitor and discovery cannot leave a partially visible graph.
        return await monitors.ExecuteIfActiveEntityMutationAsync(
            entityId,
            async leaseCancellationToken => {
                await CommitContainerCoreAsync(
                    descriptor, route.Identity, proposal, selectedChildren,
                    requestOwnedChildren: false,
                    startAcquisitions: autoMonitorsNewWorks,
                    explicitRequest: false,
                    targeting,
                    preset: null,
                    hideNsfw: true,
                    exactPluginId: route.PluginId,
                    preparedDescendants: null,
                    leaseCancellationToken);
            },
            cancellationToken);
    }
}
