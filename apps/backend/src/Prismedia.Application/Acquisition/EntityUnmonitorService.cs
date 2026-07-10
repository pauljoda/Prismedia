using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>The explicitly unmonitored root Entity whose provider identities form its durable off override.</summary>
public sealed record UnmonitorSuppressionTarget(
    Guid EntityId,
    EntityKind Kind,
    string Title,
    IReadOnlyList<ExternalIdentity> ExternalIdentities);

/// <summary>
/// Immutable cleanup scope resolved from one monitor: the target Entity subtree, every acquisition and
/// monitor it owns, and the explicitly unmonitored root identity. Only the root is suppressed: descendants
/// removed with a parent-off cascade must remain discoverable if that parent is monitored again. The
/// persistence adapter revalidates destructive Entity decisions at completion time.
/// </summary>
public sealed record EntityUnmonitorScope(
    Guid MonitorId,
    Guid? RootEntityId,
    IReadOnlyList<Guid> EntityIds,
    IReadOnlyList<Guid> AcquisitionIds,
    IReadOnlyList<Guid> MonitorIds,
    UnmonitorSuppressionTarget? RootSuppression,
    bool SyntheticMonitorAnchor = false,
    IReadOnlyDictionary<Guid, AcquisitionStatus>? AcquisitionStatuses = null);

/// <summary>
/// Persistence boundary for generalized Entity unmonitoring. Resolution is read-only; claiming is the
/// post-preflight claim that prevents background monitor work; completion removes monitors and prunes
/// only still-fileless acquisition-only branches, regardless of a stale Wanted flag.
/// </summary>
public interface IEntityUnmonitorPersistence {
    /// <summary>Resolves the monitor's Entity subtree and cleanup scope without changing state.</summary>
    Task<EntityUnmonitorScope?> ResolveAsync(Guid monitorId, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves cleanup from an Entity even when it has no monitor yet. Implementations give a monitorless,
    /// fileless Entity a synthetic stopping-anchor identity that is persisted only when the scope is claimed.
    /// </summary>
    Task<EntityUnmonitorScope?> ResolveForEntityAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically collapses the scoped monitor rows into one non-resumable stopping claim after
    /// acquisition preflight succeeds. The claim retains the target Entity id even after acquisition
    /// foreign keys are cleared, so an interrupted cleanup can safely re-resolve and retry its subtree. It
    /// also publishes the root's durable provider suppression in the same transaction before lifecycle
    /// locks are released. A synthetic anchor is inserted directly in the stopping state during this claim.
    /// Returns false when the root monitor changed or disappeared before it could be claimed.
    /// </summary>
    Task<bool> ClaimAsync(
        EntityUnmonitorScope scope,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<bool>>? revalidateRemovalEligibility = null);

    /// <summary>
    /// Removes all monitors in scope, deletes still-fileless acquisition-only branches, and clears a stale
    /// Wanted flag from retained source-backed branches. Implementations re-read file ownership before pruning.
    /// </summary>
    Task<bool> CompleteAsync(EntityUnmonitorScope scope, CancellationToken cancellationToken);
}

/// <summary>Outcome of stopping an Entity monitor and cleaning the acquisition-only state it governed.</summary>
/// <param name="Found">Whether the requested monitor existed when cleanup was resolved.</param>
/// <param name="Stopped">Whether the full recursive cleanup completed.</param>
/// <param name="Message">Actionable conflict detail when cleanup could not safely start or finish.</param>
/// <param name="RootEntityPruned">Whether the completed cleanup removed its fileless root Entity.</param>
public sealed record MonitorStopResult(
    bool Found,
    bool Stopped,
    string? Message = null,
    bool RootEntityPruned = false);

/// <summary>
/// General Entity give-up boundary used by wanted/removal workflows. It applies the same strict, retryable
/// acquisition teardown as ordinary monitor removal, including for a monitorless provider placeholder.
/// </summary>
public interface IEntityGiveUpService {
    /// <summary>Collapses one Entity subtree back to purely source-backed library state.</summary>
    Task<MonitorStopResult> GiveUpEntityAsync(Guid entityId, CancellationToken cancellationToken);
}

/// <summary>
/// Generalized unmonitor use case. It preflights the complete acquisition set before the first mutation,
/// claims every monitor in the Entity subtree, removes all acquisition/download state, suppresses the
/// explicitly toggled root, then commits the Entity cleanup. No media-kind branching belongs here.
/// </summary>
public sealed class EntityUnmonitorService(
    IEntityUnmonitorPersistence persistence,
    IAcquisitionRequestService acquisitions) : IEntityGiveUpService {
    /// <summary>Stops a monitor and collapses its target subtree back to source-backed Entity state.</summary>
    public async Task<MonitorStopResult> StopAsync(Guid monitorId, CancellationToken cancellationToken) {
        var scope = await persistence.ResolveAsync(monitorId, cancellationToken);
        return await StopScopeAsync(scope, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MonitorStopResult> GiveUpEntityAsync(Guid entityId, CancellationToken cancellationToken) {
        var scope = await persistence.ResolveForEntityAsync(entityId, cancellationToken);
        return await StopScopeAsync(scope, cancellationToken);
    }

    private async Task<MonitorStopResult> StopScopeAsync(
        EntityUnmonitorScope? scope,
        CancellationToken cancellationToken) {
        if (scope is null) {
            return new MonitorStopResult(Found: false, Stopped: false);
        }

        // Validate EVERY acquisition before claiming monitors, deleting transfers, suppressing identities,
        // or pruning Entities. A partially applied import blocks the whole operation rather than leaving a
        // parent half-unmonitored with some descendants already removed.
        foreach (var acquisitionId in scope.AcquisitionIds) {
            var eligibility = await acquisitions.GetRemovalEligibilityAsync(acquisitionId, cancellationToken);
            if (!eligibility.CanRemove) {
                return new MonitorStopResult(true, false, eligibility.Message);
            }
        }

        string? claimConflictMessage = null;
        var claimed = await persistence.ClaimAsync(
            scope,
            cancellationToken,
            async claimCancellationToken => {
                // The persistence adapter invokes this only after taking the Entity/monitor lifecycle
                // locks and revalidating the captured acquisition statuses. An import that won the lock
                // must therefore become visible here before suppression or teardown is published.
                foreach (var acquisitionId in scope.AcquisitionIds) {
                    var eligibility = await acquisitions.GetRemovalEligibilityAsync(
                        acquisitionId,
                        claimCancellationToken);
                    if (!eligibility.CanRemove) {
                        claimConflictMessage = eligibility.Message;
                        return false;
                    }
                }

                return true;
            });
        if (!claimed) {
            return new MonitorStopResult(
                true,
                false,
                claimConflictMessage
                    ?? "The monitor or acquisition changed while unmonitoring was being prepared. Refresh and try again.");
        }

        try {
            // Claim every acquisition after the monitor/suppression transaction and before starting remote
            // teardown. This closes the interval where a queued search/import could create new client or
            // file state after the monitor was frozen but before its acquisition reached durable Stopping.
            // Missing rows are already in the desired state; matching claims make retries idempotent.
            foreach (var acquisitionId in scope.AcquisitionIds) {
                await acquisitions.ClaimTeardownAsync(
                    acquisitionId,
                    AcquisitionTeardownIntent.Remove,
                    cancellationToken);
            }
        } catch (AcquisitionConfigurationException exception) {
            // Any claims already taken deliberately remain durable. The immutable monitor scope stays
            // Stopping as well, so retry can finish without background work re-entering a partial teardown.
            return new MonitorStopResult(true, false, exception.Message);
        }

        try {
            foreach (var acquisitionId in scope.AcquisitionIds) {
                // Missing after preflight is already the desired state. The acquisition is already claimed;
                // this idempotently removes client data and completes its hard-delete.
                await acquisitions.DeleteForUnmonitorAsync(acquisitionId, cancellationToken);
            }
        } catch (AcquisitionConfigurationException exception) {
            // The scope stays Stopping so a lifecycle race cannot resume downloads. A retry re-resolves the
            // remaining state and finishes cleanup once the competing import has settled.
            return new MonitorStopResult(true, false, exception.Message);
        }

        var rootEntityPruned = await persistence.CompleteAsync(scope, cancellationToken);
        return new MonitorStopResult(true, true, RootEntityPruned: rootEntityPruned);
    }
}
