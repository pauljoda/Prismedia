using Prismedia.Application.Jobs;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// The narrow acquisition seam entity/request workflows need: start and inspect acquisitions for wanted
/// entities, tear them down when the want is removed, or replace an imported row with a clean reacquisition
/// after its files are deleted. Implemented by <see cref="AcquisitionService"/> so those workflows do not
/// couple to the full acquisition API service.
/// </summary>
public interface IAcquisitionRequestService {
    /// <summary>
    /// Resumes a release-gated acquisition when its date is ready and its exact monitor remains active.
    /// Returns true only when automatic search work was scheduled.
    /// </summary>
    Task<bool> ResumeReleasedAsync(Guid acquisitionId, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    /// <summary>Persists a new acquisition and enqueues the background search job that fills in candidates.</summary>
    Task<AcquisitionSummary> CreateAndSearchAsync(AcquisitionCreateRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a new acquisition and schedules its search in the supplied graph scope. A non-null
    /// <paramref name="parentContext"/> keeps the search and all descendants in the caller's graph;
    /// otherwise <paramref name="origin"/> selects the pool for a new root graph.
    /// </summary>
    Task<AcquisitionSummary> CreateAndSearchAsync(
        AcquisitionCreateRequest request,
        JobContext? parentContext,
        JobGraphOrigin origin,
        CancellationToken cancellationToken) =>
        CreateAndSearchAsync(request, cancellationToken);

    /// <summary>
    /// Persists and schedules an acquisition while the caller already holds the target Entity lifecycle
    /// lease. Request commit uses this seam inside its wider suppression/monitor intent transaction so it
    /// does not recursively reacquire the same locks. Other callers must use <see cref="CreateAndSearchAsync"/>.
    /// The default keeps narrow test adapters source-compatible.
    /// </summary>
    Task<AcquisitionSummary> CreateAndSearchWithinEntityLifecycleAsync(
        AcquisitionCreateRequest request,
        CancellationToken cancellationToken) =>
        CreateAndSearchAsync(request, cancellationToken);

    /// <summary>
    /// True when actionable acquisition work already targets the Entity. Imported and Cancelled rows are
    /// terminal history, not open requests: a fileless monitored Entity must be able to start fresh work.
    /// </summary>
    Task<bool> AnyOpenForEntityAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>True when actionable work already targets the same Entity rendition.</summary>
    Task<bool> AnyOpenForEntityAsync(
        Guid entityId,
        BookRendition? bookRendition,
        CancellationToken cancellationToken) =>
        AnyOpenForEntityAsync(entityId, cancellationToken);

    /// <summary>
    /// Covers a missing child with its existing actionable acquisition and republishes barren pre-download
    /// work when it is safe to do so. Active transfers and manual-import states remain untouched; terminal
    /// history returns false so the caller can create fresh work.
    /// </summary>
    Task<bool> EnsureOpenEntitySearchAsync(
        Guid entityId,
        BookRendition? bookRendition,
        CancellationToken cancellationToken) =>
        Task.FromResult(false);

    /// <summary>Republishes an existing search in the supplied inherited or root graph scope.</summary>
    Task<bool> EnsureOpenEntitySearchAsync(
        Guid entityId,
        BookRendition? bookRendition,
        JobContext? parentContext,
        JobGraphOrigin origin,
        CancellationToken cancellationToken) =>
        EnsureOpenEntitySearchAsync(entityId, bookRendition, cancellationToken);

    /// <summary>
    /// Returns the requested Entity ids that already have actionable work for the same rendition.
    /// Production adapters use one bounded query; the default preserves narrow test adapters.
    /// </summary>
    async Task<IReadOnlySet<Guid>> FilterOpenEntityIdsAsync(
        IReadOnlyCollection<Guid> entityIds,
        BookRendition? bookRendition,
        CancellationToken cancellationToken) {
        var result = new HashSet<Guid>();
        foreach (var entityId in entityIds.Distinct()) {
            if (await AnyOpenForEntityAsync(entityId, bookRendition, cancellationToken)) {
                result.Add(entityId);
            }
        }

        return result;
    }

    /// <summary>
    /// Every acquisition owned by this library Entity, including upgrade descendants whose stable
    /// ownership is inherited through <c>UpgradeOfAcquisitionId</c>. Destructive Entity lifecycles must
    /// close this complete graph so an in-flight replacement transfer cannot leak.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>
    /// Read-only preflight for destructive removal. An active or partially applied import rejects the
    /// operation; every other lifecycle state can be cancelled and removed. Missing rows are already
    /// removed and therefore eligible.
    /// </summary>
    Task<AcquisitionRemovalEligibility> GetRemovalEligibilityAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads whether an imported acquisition can be replaced after its owned files are deleted. This check
    /// has no persistence, queue, monitor, download-client, or filesystem side effects, so destructive entity
    /// workflows can validate their complete replacement set before mutating anything.
    /// </summary>
    Task<AcquisitionReacquireEligibility> GetReacquireEligibilityAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Durably claims an acquisition for the given destructive completion intent and cancels every exact
    /// queued/running acquisition job. An existing matching claim is an idempotent success; a competing
    /// intent or lifecycle change is an actionable conflict. False means the acquisition is already gone.
    /// </summary>
    Task<bool> ClaimTeardownAsync(
        Guid id,
        AcquisitionTeardownIntent intent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Durably takes over any local acquisition lifecycle for an explicit full Entity deletion, cancels its
    /// jobs, and clears partial import artifacts. Unlike ordinary unmonitor/removal, this is the user's
    /// escape hatch from stale importing, awaiting-selection, or competing teardown state.
    /// </summary>
    Task<bool> ClaimEntityRemovalTeardownAsync(Guid id, CancellationToken cancellationToken) =>
        ClaimTeardownAsync(id, AcquisitionTeardownIntent.Remove, cancellationToken);

    /// <summary>
    /// Strictly removes the recorded remote transfer and its data, or confirms it is already absent,
    /// without changing local acquisition, monitor, job, or history state. Missing/unreachable client
    /// configuration and client failures are actionable conflicts. Use this when a workflow promises that
    /// no remotely owned transfer will remain, such as unmonitoring or reacquiring managed content.
    /// </summary>
    Task ConfirmTransferRemovedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Best-effort transfer cleanup for explicit full Entity deletion. Delete files is fundamentally a
    /// local library operation: an unavailable or already-lost download client must not strand managed
    /// files, Entity rows, monitors, or jobs in teardown state.
    /// </summary>
    async Task DiscardTransferForEntityDeletionAsync(Guid id, CancellationToken cancellationToken) {
        try {
            await ConfirmTransferRemovedAsync(id, cancellationToken);
        } catch (AcquisitionConfigurationException) {
            // The durable local deletion claim is the authority. Remote cleanup is opportunistic here.
        }
    }

    /// <summary>
    /// Hard-deletes a durably claimed acquisition after external/file work completed. The persisted intent
    /// must match, preventing a retry or competing workflow from changing reacquisition into removal.
    /// </summary>
    Task<bool> CompleteTeardownAsync(
        Guid id,
        AcquisitionTeardownIntent intent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes an acquisition entirely: best-effort deletes its torrent (and data) from the client, then
    /// hard-deletes the record. <paramref name="preserveWantedLoop"/> (the user-facing Downloads remove)
    /// keeps a monitor watching the acquisition alive by re-pointing it at a fresh pending clone; internal
    /// teardown paths leave it off — they are dismantling the loop on purpose.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken, bool preserveWantedLoop = false);

    /// <summary>
    /// Strict teardown used by explicit unmonitoring. A recorded client item must be confirmed absent or
    /// removed with its data before the acquisition row is hard-deleted; client/config failures leave the
    /// acquisition durable for a retry.
    /// </summary>
    Task<bool> DeleteForUnmonitorAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces an imported acquisition whose owned files were deliberately deleted with a clean retry,
    /// re-points its monitor, and starts a release search immediately. The linked entity must already be a
    /// fileless wanted placeholder; returns the replacement acquisition id. If the clean clone cannot be
    /// created, the now-invalid imported row and its per-item monitor are removed so callers can never retain
    /// an Imported state for files that no longer exist, and null is returned. This is intentionally separate
    /// from <see cref="DeleteAsync"/> so removing a row from Downloads keeps its normal monitor cadence instead
    /// of immediately re-grabbing it.
    /// </summary>
    Task<Guid?> ReacquireAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Read-only verdict for replacing one imported acquisition with a clean search after its files are deleted.
/// </summary>
/// <param name="CanReacquire">True only when the acquisition is currently safe to supersede.</param>
/// <param name="Message">Actionable reason when reacquisition is not currently safe.</param>
public sealed record AcquisitionReacquireEligibility(bool CanReacquire, string? Message = null);

/// <summary>Read-only verdict for safely cancelling and hard-deleting one acquisition.</summary>
/// <param name="CanRemove">Whether removal can proceed without crossing an active/partial import.</param>
/// <param name="Message">Actionable reason when the acquisition must finish recovery first.</param>
public sealed record AcquisitionRemovalEligibility(bool CanRemove, string? Message = null);

