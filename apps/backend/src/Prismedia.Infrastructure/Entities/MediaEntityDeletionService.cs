using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs;
using Prismedia.Application.Requests;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// EF-backed implementation of <see cref="IMediaEntityDeletionService"/>: resolves the entity's
/// descendant tree and partitions it by direct monitor intent. Directly monitored targets plus their
/// required structural ancestors survive; unmonitored sibling
/// branches are removed, suppressed, and have their acquisition state torn down. Source paths are deleted
/// through managed storage, generated assets are reconciled per retained/removed branch, and affected scans
/// are queued to settle bookkeeping.
/// </summary>
public sealed class MediaEntityDeletionService(
    PrismediaDbContext db,
    IFilesPersistence roots,
    IManagedFileStorage storage,
    IWantedSuppressionStore suppressions,
    IAcquisitionRequestService acquisitions,
    IMonitoredEntityRecovery monitoredRecovery,
    IJobQueueService jobs,
    AssetPathService assets,
    IEntityHierarchyReader hierarchy,
    ILogger<MediaEntityDeletionService> logger,
    EntityAssetCleanupService? sharedAssetCleanup = null) : IMediaEntityDeletionService {
    private readonly EntityAssetCleanupService entityAssetCleanup =
        sharedAssetCleanup ?? new EntityAssetCleanupService(assets, logger);

    /// <summary>Whether the given kind code may be deleted (with files) through this service.</summary>
    public static bool IsDeletableKind(string? kind) =>
        !string.IsNullOrWhiteSpace(kind)
        && EntityKindRegistry.TryGet(kind, out var value)
        && EntityKindRegistry.Describe(value).SupportsFileDeletion;

    /// <inheritdoc />
    public async Task<MediaEntityBulkDeleteResult> DeleteManyAsync(
        IReadOnlyList<Guid> ids,
        bool deleteFiles,
        CancellationToken cancellationToken) {
        var selectedIds = ids.Distinct().ToArray();
        if (selectedIds.Length == 0) {
            return new MediaEntityBulkDeleteResult(0, 0, [], 0);
        }

        // Snapshot every ancestry chain before the first destructive call. Input order can never turn a
        // selected child into the first disk owner processed while its selected wrapper still exists.
        var selectedSet = selectedIds.ToHashSet();
        var ancestorsById = new Dictionary<Guid, IReadOnlySet<Guid>>();
        foreach (var selectedId in selectedIds) {
            ancestorsById[selectedId] = (await hierarchy.ListAncestorIdsAsync(
                selectedId,
                cancellationToken)).ToHashSet();
        }

        var roots = selectedIds
            .Where(id => !ancestorsById[id].Overlaps(selectedSet))
            .ToArray();
        var coveredCountByRoot = roots.ToDictionary(
            rootId => rootId,
            rootId => selectedIds.Count(id => id != rootId && ancestorsById[id].Contains(rootId)));

        var deleted = 0;
        var filesDeleted = 0;
        var reverted = 0;
        var failures = new List<MediaEntityBulkDeleteFailure>();
        foreach (var rootId in roots) {
            var result = await DeleteAsync(rootId, deleteFiles, cancellationToken);
            if (!result.Deleted) {
                failures.Add(new MediaEntityBulkDeleteFailure(
                    rootId,
                    result.Message ?? "The Entity could not be deleted."));
                continue;
            }

            deleted += 1 + coveredCountByRoot[rootId];
            filesDeleted += result.FilesDeleted;
            if (result.Reverted) {
                reverted++;
            }
        }

        return new MediaEntityBulkDeleteResult(deleted, filesDeleted, failures, reverted);
    }

    /// <inheritdoc />
    public async Task<MediaEntityDeleteResult> DeleteAsync(Guid id, bool deleteFiles, CancellationToken cancellationToken) {
        var entity = await db.Entities.AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new { row.KindCode, row.Title })
            .FirstOrDefaultAsync(cancellationToken);
        if (entity is null) {
            return new MediaEntityDeleteResult(
                false,
                "The entity no longer exists.",
                FailureKind: MediaEntityDeleteFailureKind.NotFound);
        }

        if (!IsDeletableKind(entity.KindCode)) {
            return new MediaEntityDeleteResult(
                false,
                $"Entities of kind '{entity.KindCode}' cannot be deleted this way.",
                FailureKind: MediaEntityDeleteFailureKind.NotDeletable);
        }

        if (!deleteFiles) {
            return new MediaEntityDeleteResult(
                false,
                "Library-only Entity removal is unsupported because the next library scan would rediscover files that remain on disk. Delete files to remove this Entity, or use Remove wanted / Unmonitor to stop acquisition without deleting media.",
                FailureKind: MediaEntityDeleteFailureKind.NotDeletable);
        }

        var ids = (await hierarchy.ListSubtreeIdsAsync(id, cancellationToken)).ToArray();
        var hasManagedSource = await db.EntityFiles.AsNoTracking().AnyAsync(
            file => ids.Contains(file.EntityId) && file.Role == EntityFileRole.Source,
            cancellationToken);
        var hasDeletingFilesMonitor = await db.Monitors.AsNoTracking().AnyAsync(
            monitor => monitor.EntityId != null
                && ids.Contains(monitor.EntityId.Value)
                && monitor.Status == MonitorStatus.DeletingFiles,
            cancellationToken);
        var hasEntityDeletionClaim = await db.Entities.AsNoTracking().AnyAsync(
            row => row.Id == id
                && row.LifecycleClaimKind == EntityLifecycleClaimKind.DeletingFiles,
            cancellationToken);
        var hasStoppingAcquisition = await db.Acquisitions.AsNoTracking().AnyAsync(
            acquisition => acquisition.EntityId != null
                && ids.Contains(acquisition.EntityId.Value)
                && acquisition.Status == AcquisitionStatus.Stopping,
            cancellationToken);
        var resumesManagedDeletion = hasEntityDeletionClaim
            || hasDeletingFilesMonitor
            || hasStoppingAcquisition;
        if (!hasManagedSource && !resumesManagedDeletion) {
            return new MediaEntityDeleteResult(
                false,
                "This Entity has no managed source files, so there is nothing on disk to delete.",
                FailureKind: MediaEntityDeleteFailureKind.NotDeletable);
        }

        var tree = await LoadTreeAsync(ids, cancellationToken);
        var identitiesByEntity = await LoadIdentitiesAsync(ids, cancellationToken);

        // Direct Active monitor targets are the only new retention authority; DeletingFiles preserves that
        // same immutable intent on retry. A parent/container monitor never blanket-owns descendants; child
        // selection is represented by real child monitors created by the shared monitoring UI.
        var targetedMonitors = await ListTargetedMonitorsAsync(ids, cancellationToken);
        if (targetedMonitors.Any(monitor => monitor.Status == MonitorStatus.Stopping)) {
            return Conflict(
                "This Entity is still being unmonitored. Finish or retry that cleanup before deleting its files.");
        }
        var plan = BuildDeletionPlan(tree, targetedMonitors);
        // Snapshot every acquisition before mutating monitors, disk, or Entity rows. Only directly monitored
        // targets reacquire their newest acquisition; everything else is teardown, including unmonitored
        // siblings and historical acquisitions attached to retained structural ancestors.
        var acquisitionIdsByEntity = new Dictionary<Guid, IReadOnlyList<Guid>>();
        foreach (var entityId in ids) {
            var entityAcquisitionIds = (await acquisitions.ListIdsForEntityAsync(entityId, cancellationToken))
                .Distinct()
                .ToArray();
            if (entityAcquisitionIds.Length > 0) {
                acquisitionIdsByEntity[entityId] = entityAcquisitionIds;
            }
        }

        var acquisitionIds = acquisitionIdsByEntity.Values.SelectMany(value => value).Distinct().ToArray();
        // The request service owns lifecycle legality, while this EF adapter also snapshots concrete status
        // for rows present in its unit of work. Fresh-claim revalidation uses both: an import that wins the
        // Entity lease race cannot hide behind an unchanged acquisition id.
        var acquisitionStatuses = await db.Acquisitions.AsNoTracking()
            .Where(row => acquisitionIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.Status, cancellationToken);
        var claimedReacquires = await db.Acquisitions.AsNoTracking()
            .Where(row => acquisitionIds.Contains(row.Id)
                && row.Status == AcquisitionStatus.Stopping
                && row.TeardownIntent == AcquisitionTeardownIntent.Reacquire)
            .Select(row => new {
                row.Id,
                row.EntityId,
                row.TeardownReplacementAcquisitionId
            })
            .ToArrayAsync(cancellationToken);
        var claimedReacquireIds = claimedReacquires.Select(row => row.Id).ToArray();
        var claimedReacquireIdSet = claimedReacquireIds.ToHashSet();
        var claimedReacquireTargetIds = claimedReacquires
            .Where(row => row.EntityId.HasValue)
            .Select(row => row.EntityId!.Value)
            .ToHashSet();
        var protectedReplacementIds = claimedReacquires
            .Where(row => row.TeardownReplacementAcquisitionId.HasValue)
            .Select(row => row.TeardownReplacementAcquisitionId!.Value)
            .ToHashSet();

        // Every direct monitor was frozen together before the first external effect. During resume, an
        // Active target normally means its claimed acquisition was replaced successfully. The exception is
        // the durable handoff window where the monitor already points at the replacement but the old
        // Stopping/Reacquire owner still exists. Keep that target in the operation and protect its linked
        // replacement so retry can finish the exact handoff instead of adopting or deleting newer work.
        var completedDirectTargetIds = resumesManagedDeletion
            ? targetedMonitors
                .Where(monitor => monitor.Status == MonitorStatus.Active
                    && !claimedReacquireTargetIds.Contains(monitor.TargetEntityId))
                .Select(monitor => monitor.TargetEntityId)
                .ToHashSet()
            : [];
        var outstandingDirectTargetIds = plan.DirectTargetIds
            .Where(targetId => !completedDirectTargetIds.Contains(targetId))
            .ToHashSet();
        var operationAcquisitionsByEntity = acquisitionIdsByEntity
            .Where(pair => !completedDirectTargetIds.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var reacquireByEntity = operationAcquisitionsByEntity
            .Where(pair => outstandingDirectTargetIds.Contains(pair.Key))
            .ToDictionary(
                pair => pair.Key,
                pair => {
                    var claimedId = pair.Value.FirstOrDefault(claimedReacquireIdSet.Contains);
                    return claimedId != Guid.Empty ? claimedId : pair.Value[0];
                });
        var reacquireIds = reacquireByEntity.Values.Distinct().ToArray();
        var reacquireIdSet = reacquireIds.ToHashSet();
        var deleteAcquisitionIds = operationAcquisitionsByEntity.Values
            .SelectMany(value => value)
            .Distinct()
            .Where(acquisitionId => !reacquireIdSet.Contains(acquisitionId)
                && !protectedReplacementIds.Contains(acquisitionId))
            .ToArray();

        // Preflight the COMPLETE operation set. A mixed tree must never delete an unmonitored sibling's
        // files and only then discover its acquisition cannot be removed safely.
        foreach (var acquisitionId in reacquireIds) {
            var eligibility = await acquisitions.GetReacquireEligibilityAsync(acquisitionId, cancellationToken);
            if (!eligibility.CanReacquire) {
                return Conflict(eligibility.Message ?? "A linked acquisition cannot be safely replaced right now.");
            }
        }
        foreach (var acquisitionId in deleteAcquisitionIds) {
            var eligibility = await acquisitions.GetRemovalEligibilityAsync(acquisitionId, cancellationToken);
            if (!eligibility.CanRemove) {
                return Conflict(eligibility.Message ?? "A linked acquisition cannot be safely removed right now.");
            }
        }

        // Deterministic disk ownership conflicts must be found before lifecycle claims. There is no useful
        // retry while another unrelated Entity owns the same folder/archive, so freezing monitors or
        // acquisitions here would only strand them in cleanup state.
        var physicalPreflight = await PreparePhysicalDeletionAsync(id, ids, cancellationToken);
        if (!physicalPreflight.Succeeded) {
            return Conflict(PhysicalDeletionConflictMessage(physicalPreflight.ToResult()));
        }

        // The stable Entity is the lifecycle owner even when the tree has no direct monitor. Explicit
        // request/monitor/provider mutations lock the same Entity ancestry and reject this durable claim.
        // Publish it only after deterministic preflight, then revalidate every captured scope before the
        // first monitor/acquisition mutation so a pre-claim winner is either included or rejected cleanly.
        var entityDeletionClaim = await ClaimEntityDeletionAsync(
            id,
            allowLegacyResume: resumesManagedDeletion,
            cancellationToken);
        if (entityDeletionClaim is null) {
            return Conflict(
                "This Entity or one of its parents is already being changed. No files were changed; refresh and retry Delete files.");
        }
        if (!entityDeletionClaim.Resumed
            && !await DeletionScopeStillMatchesAsync(
                id,
                ids,
                tree,
                targetedMonitors,
                acquisitionIdsByEntity,
                acquisitionStatuses,
                reacquireIds,
                deleteAcquisitionIds,
                physicalPreflight,
                cancellationToken)) {
            // A completely fresh claim has not changed monitor/acquisition state yet and is safe to
            // release. Existing partial lifecycle state deliberately retains the claim for crash retry.
            if (!resumesManagedDeletion) {
                await ReleaseEntityDeletionClaimAsync(
                    id,
                    entityDeletionClaim.OperationId,
                    cancellationToken);
            }
            return Conflict(
                "Entity monitoring, acquisition, hierarchy, or source ownership changed while Delete files was being claimed. No files were changed; refresh and retry.");
        }

        var directMonitorIds = targetedMonitors
            .Where(monitor => plan.DirectTargetIds.Contains(monitor.TargetEntityId)
                && (monitor.Status == MonitorStatus.DeletingFiles
                    || (!resumesManagedDeletion && monitor.Status == MonitorStatus.Active)))
            .Select(monitor => monitor.Id)
            .Distinct()
            .ToArray();
        if (!await ClaimDirectMonitorsAsync(directMonitorIds, cancellationToken)) {
            return Conflict(
                "A monitor changed while managed file deletion was being claimed. No files were changed; refresh and retry Delete files.");
        }

        // Claim every acquisition only after direct monitor intent is durably frozen. If one acquisition
        // claim loses a race, DeletingFiles preserves this exact plan for retry instead of recomputing a
        // different remove-vs-reacquire split around already-claimed acquisitions.
        try {
            foreach (var acquisitionId in reacquireIds) {
                await acquisitions.ClaimTeardownAsync(
                    acquisitionId,
                    AcquisitionTeardownIntent.Reacquire,
                    cancellationToken);
            }
            foreach (var acquisitionId in deleteAcquisitionIds) {
                await acquisitions.ClaimTeardownAsync(
                    acquisitionId,
                    AcquisitionTeardownIntent.Remove,
                    cancellationToken);
            }
        } catch (AcquisitionConfigurationException exception) {
            return Conflict(exception.Message);
        }

        // Remote transfers are part of the destructive boundary, not best-effort cleanup. Confirm the
        // complete operation set absent only after durable local claims prevent requeue. If confirmation or
        // disk fails, those claims remain retryable while files, source rows, Entities, and suppression stay.
        foreach (var acquisitionId in reacquireIds.Concat(deleteAcquisitionIds).Distinct()) {
            try {
                await acquisitions.ConfirmTransferRemovedAsync(acquisitionId, cancellationToken);
            } catch (AcquisitionConfigurationException exception) {
                return Conflict(exception.Message);
            }
        }

        // Keep active direct monitors and the monitor currently backing a reacquisition. Every monitor for
        // a removed/structural-only branch, and every superseded acquisition monitor, is teardown state.
        var keptMonitorIds = targetedMonitors
            .Where(monitor =>
                (IsFileDeletionIntentStatus(monitor.Status)
                    && plan.DirectTargetIds.Contains(monitor.TargetEntityId))
                || (monitor.AcquisitionId is { } acquisitionId && reacquireIdSet.Contains(acquisitionId)))
            .Select(monitor => monitor.Id)
            .ToHashSet();
        var doomedTargetedMonitorIds = targetedMonitors
            .Where(monitor => !keptMonitorIds.Contains(monitor.Id))
            .Select(monitor => monitor.Id)
            .ToArray();
        var keptMonitorIdArray = keptMonitorIds.ToArray();

        // Re-read the physical plan immediately before the irreversible boundary. A new outside owner or a
        // changed source path is a real TOCTOU conflict: claims now deliberately remain retryable, while no
        // disk or library rows are touched under a plan different from the one the user confirmed.
        var revalidatedPhysicalPlan = await PreparePhysicalDeletionAsync(id, ids, cancellationToken);
        if (!revalidatedPhysicalPlan.Succeeded) {
            return Conflict(PhysicalDeletionConflictMessage(revalidatedPhysicalPlan.ToResult()));
        }
        if (!physicalPreflight.HasSameTargets(revalidatedPhysicalPlan)) {
            return Conflict(
                "Managed source ownership changed while deletion was being claimed. No files or library rows were changed; refresh and retry Delete files.");
        }

        // Physical deletion is the irreversible boundary. Do it only after every acquisition and monitor is
        // durably frozen. A transient failure deliberately retains those local claims while leaving
        // library/source rows intact, so nothing can requeue and the user can resolve the path and retry.
        var physicalDeletion = await DeletePreparedSourcePathsAsync(
            revalidatedPhysicalPlan.Plan!,
            cancellationToken);
        if (!physicalDeletion.Succeeded) {
            return Conflict(PhysicalDeletionConflictMessage(physicalDeletion));
        }
        var filesDeleted = physicalDeletion.PathsDeleted;

        var doomedMonitors = await db.Monitors
            .Where(monitor => !keptMonitorIdArray.Contains(monitor.Id)
                && (doomedTargetedMonitorIds.Contains(monitor.Id)
                    || (monitor.AcquisitionId != null
                        && deleteAcquisitionIds.Contains(monitor.AcquisitionId.Value))))
            .ToArrayAsync(cancellationToken);
        if (doomedMonitors.Length > 0) {
            db.Monitors.RemoveRange(doomedMonitors);
            await db.SaveChangesAsync(cancellationToken);
        }
        if (deleteAcquisitionIds.Length > 0) {
            var pointerClearedAt = DateTimeOffset.UtcNow;
            if (db.Database.IsRelational()) {
                await db.Monitors
                    .Where(monitor => monitor.UpgradeChildAcquisitionId != null
                        && deleteAcquisitionIds.Contains(monitor.UpgradeChildAcquisitionId.Value))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(monitor => monitor.UpgradeChildAcquisitionId, (Guid?)null)
                        .SetProperty(monitor => monitor.UpdatedAt, pointerClearedAt), cancellationToken);
            } else {
                var upgradeOwners = await db.Monitors
                    .Where(monitor => monitor.UpgradeChildAcquisitionId != null
                        && deleteAcquisitionIds.Contains(monitor.UpgradeChildAcquisitionId.Value))
                    .ToArrayAsync(cancellationToken);
                foreach (var monitor in upgradeOwners) {
                    monitor.UpgradeChildAcquisitionId = null;
                    monitor.UpdatedAt = pointerClearedAt;
                }
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        foreach (var acquisitionId in deleteAcquisitionIds) {
            await acquisitions.CompleteTeardownAsync(
                acquisitionId,
                AcquisitionTeardownIntent.Remove,
                cancellationToken);
        }

        // Each removed branch root is blacklisted independently. Retained ancestors and monitored targets
        // are never suppressed, while an unmonitored sibling remains deleted even under a container watch.
        foreach (var branchRootId in plan.RemovedBranchRootIds) {
            if (!identitiesByEntity.TryGetValue(branchRootId, out var branchIdentities)
                || branchIdentities.Count == 0
                || !tree.TryGetValue(branchRootId, out var branch)) {
                continue;
            }
            await suppressions.SuppressAsync(branchIdentities, branch.Kind, branch.Title, cancellationToken);
        }

        var retainedIds = plan.RetainedIds.ToArray();
        var removedIds = plan.RemovedIds.ToArray();
        entityAssetCleanup.Cleanup(retainedIds, preserveArtwork: true);
        var removedArtworkPaths = await db.EntityFiles.AsNoTracking()
            .Where(file => removedIds.Contains(file.EntityId) && file.Role != EntityFileRole.Source)
            .Select(file => file.Path)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        entityAssetCleanup.Cleanup(removedIds, preserveArtwork: false, removedArtworkPaths);

        var playbackDerivedRoles = new[] {
            EntityFileRole.Preview, EntityFileRole.Sprite, EntityFileRole.Trickplay, EntityFileRole.Waveform,
        };
        var retainedSourceRows = await db.EntityFiles
            .Where(file => retainedIds.Contains(file.EntityId)
                && (file.Role == EntityFileRole.Source || playbackDerivedRoles.Contains(file.Role)))
            .ToArrayAsync(cancellationToken);
        db.EntityFiles.RemoveRange(retainedSourceRows);
        var retainedRows = await db.Entities.Where(row => retainedIds.Contains(row.Id)).ToArrayAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var row in retainedRows) {
            if (plan.WantedIds.Contains(row.Id)) {
                row.IsWanted = true;
            }
            row.UpdatedAt = now;
        }
        var removedRows = await db.Entities.Where(row => removedIds.Contains(row.Id)).ToArrayAsync(cancellationToken);
        db.Entities.RemoveRange(removedRows);
        await db.SaveChangesAsync(cancellationToken);

        // The old Imported row cannot be searched directly. Reacquire only direct targets; when no usable
        // acquisition exists, the registry-driven recovery port performs the correct leaf/container action.
        var entitiesWithReplacement = completedDirectTargetIds.ToHashSet();
        foreach (var acquisitionId in reacquireIds) {
            if (await acquisitions.ReacquireAsync(acquisitionId, cancellationToken) is { }) {
                foreach (var pair in reacquireByEntity.Where(pair => pair.Value == acquisitionId)) {
                    entitiesWithReplacement.Add(pair.Key);
                }
            } else {
                logger.LogWarning(
                    "MediaEntityDeletion: removed acquisition {AcquisitionId} without a replacement after reverting entity {EntityId} to wanted because a clean retry could not be created.",
                    acquisitionId, id);
            }
        }

        // ReacquireAsync atomically retargets each acquisition-linked DeletingFiles monitor and restores it
        // Active. What remains is Entity-only intent; re-enable those exact rows only after every claimed
        // acquisition has been replaced, then let shared recovery create any missing work.
        await RestoreDirectMonitorsAsync(directMonitorIds, CancellationToken.None);

        // The destructive boundary is now complete: disk/source rows are reconciled, retained monitor
        // intent is Active again, and reacquisition handoffs own their new work. Release the stable Entity
        // claim before ordinary monitored recovery re-enters the request pipeline.
        await ReleaseEntityDeletionClaimAsync(
            id,
            entityDeletionClaim.OperationId,
            CancellationToken.None);
        foreach (var targetId in plan.DirectTargetIds.Where(targetId => !entitiesWithReplacement.Contains(targetId))) {
            try {
                // Entity/source reconciliation is already committed and cannot be rolled back. Finish this
                // best-effort handoff independently of request cancellation; an Active monitor is durable
                // recovery intent and the scheduled sweep will retry if immediate maintenance is unavailable.
                await monitoredRecovery.MaintainAsync(targetId, CancellationToken.None);
            } catch (Exception exception) {
                logger.LogWarning(
                    exception,
                    "MediaEntityDeletion: immediate monitored recovery failed for Entity {EntityId} after deletion committed; the Active monitor will retry on its scheduled sweep.",
                    targetId);
            }
        }

        // Scans observe the reconciled model, never the physical/local half-state. Queue only after source
        // rows, Entities, acquisitions, monitor restoration, and recovery all completed successfully.
        await QueueReconciliationScansAsync(physicalDeletion.TouchedRoots);

        logger.LogInformation(
            "MediaEntityDeletion: reconciled \"{Title}\" ({Kind}) — {Retained} retained, {Removed} removed, {Files} on-disk paths removed.",
            entity.Title, entity.KindCode, retainedRows.Length, removedRows.Length, filesDeleted);
        return new MediaEntityDeleteResult(
            true,
            FilesDeleted: filesDeleted,
            Reverted: plan.DirectTargetIds.Count > 0);
    }

    private static MediaEntityDeleteResult Conflict(string message) =>
        new(false, message, FailureKind: MediaEntityDeleteFailureKind.Conflict);

    /// <summary>
    /// Publishes or resumes the durable file-deletion claim while holding the same monitor-chain then
    /// Entity-chain locks used by explicit request/monitor mutations. The operation id survives process
    /// failure and prevents an unrelated completion attempt from clearing another owner's claim.
    /// </summary>
    private async Task<EntityDeletionClaim?> ClaimEntityDeletionAsync(
        Guid entityId,
        bool allowLegacyResume,
        CancellationToken cancellationToken) {
        var lifecycleEntityIds = new[] { entityId }
            .Concat(await hierarchy.ListAncestorIdsAsync(entityId, cancellationToken))
            .Distinct()
            .ToArray();
        var monitorIds = await ListLifecycleMonitorIdsAsync(lifecycleEntityIds, cancellationToken);
        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational()) {
            transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        }

        try {
            var lockedMonitors = new List<MonitorRow>(monitorIds.Count);
            foreach (var monitorId in monitorIds.Order()) {
                var locked = db.Database.IsRelational()
                    && db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
                    ? await db.Monitors
                        .FromSqlInterpolated($"SELECT * FROM monitors WHERE id = {monitorId} FOR UPDATE")
                        .SingleOrDefaultAsync(cancellationToken)
                    : await db.Monitors.FirstOrDefaultAsync(
                        row => row.Id == monitorId,
                        cancellationToken);
                if (locked is not null) {
                    lockedMonitors.Add(locked);
                }
            }

            var lockedEntities = new List<EntityRow>(lifecycleEntityIds.Length);
            foreach (var lifecycleEntityId in lifecycleEntityIds.Order()) {
                var locked = db.Database.IsRelational()
                    && db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
                    ? await db.Entities
                        .FromSqlInterpolated($"SELECT * FROM entities WHERE id = {lifecycleEntityId} FOR UPDATE")
                        .SingleOrDefaultAsync(cancellationToken)
                    : await db.Entities.FirstOrDefaultAsync(
                        row => row.Id == lifecycleEntityId,
                        cancellationToken);
                if (locked is not null) {
                    lockedEntities.Add(locked);
                }
            }

            var root = lockedEntities.FirstOrDefault(row => row.Id == entityId);
            if (root is null
                || lockedEntities.Any(row => row.Id != entityId && row.LifecycleClaimKind != null)
                || root.LifecycleClaimKind is not (null or EntityLifecycleClaimKind.DeletingFiles)
                || lockedMonitors.Any(row => row.Status == MonitorStatus.Stopping)) {
                return null;
            }

            var resumed = root.LifecycleClaimKind == EntityLifecycleClaimKind.DeletingFiles;
            if (!resumed
                && !allowLegacyResume
                && lockedMonitors.Any(row => row.Status == MonitorStatus.DeletingFiles)) {
                return null;
            }

            // The Entity lock may have waited behind an explicit intent that published a new monitor.
            // Re-read after the serialization anchor and validate any newly committed chain row before
            // publishing the claim. A committed row is no longer holding the Entity lock, so this cannot
            // invert the live monitor->Entity lock order.
            var refreshedMonitorIds = await ListLifecycleMonitorIdsAsync(
                lifecycleEntityIds,
                cancellationToken);
            foreach (var monitorId in refreshedMonitorIds.Except(monitorIds).Order()) {
                var locked = db.Database.IsRelational()
                    && db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
                    ? await db.Monitors
                        .FromSqlInterpolated($"SELECT * FROM monitors WHERE id = {monitorId} FOR UPDATE")
                        .SingleOrDefaultAsync(cancellationToken)
                    : await db.Monitors.FirstOrDefaultAsync(
                        row => row.Id == monitorId,
                        cancellationToken);
                if (locked is not null) {
                    lockedMonitors.Add(locked);
                }
            }
            if (lockedMonitors.Any(row => row.Status == MonitorStatus.Stopping)
                || (!resumed
                    && !allowLegacyResume
                    && lockedMonitors.Any(row => row.Status == MonitorStatus.DeletingFiles))) {
                return null;
            }

            var operationId = root.LifecycleClaimId ?? Guid.NewGuid();
            if (!resumed || root.LifecycleClaimId is null) {
                var now = DateTimeOffset.UtcNow;
                root.LifecycleClaimKind = EntityLifecycleClaimKind.DeletingFiles;
                root.LifecycleClaimId = operationId;
                root.LifecycleClaimedAt ??= now;
                root.UpdatedAt = now;
                await db.SaveChangesAsync(cancellationToken);
            }

            if (transaction is not null) {
                await transaction.CommitAsync(cancellationToken);
            }
            return new EntityDeletionClaim(operationId, resumed);
        } finally {
            if (transaction is not null) {
                await transaction.DisposeAsync();
            }
        }
    }

    /// <summary>Monitor ids whose target is the Entity lifecycle target or one of its ancestors.</summary>
    private async Task<IReadOnlySet<Guid>> ListLifecycleMonitorIdsAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        var ids = entityIds.Distinct().ToArray();
        var acquisitionIds = await db.Acquisitions.AsNoTracking()
            .Where(row => row.EntityId != null && ids.Contains(row.EntityId.Value))
            .Select(row => row.Id)
            .ToArrayAsync(cancellationToken);
        return await db.Monitors.AsNoTracking()
            .Where(row => (row.EntityId != null && ids.Contains(row.EntityId.Value))
                || (row.AcquisitionId != null && acquisitionIds.Contains(row.AcquisitionId.Value)))
            .Select(row => row.Id)
            .ToHashSetAsync(cancellationToken);
    }

    /// <summary>Clears only the exact durable claim owned by this completed operation.</summary>
    private async Task ReleaseEntityDeletionClaimAsync(
        Guid entityId,
        Guid operationId,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            var released = await db.Entities
                .Where(row => row.Id == entityId
                    && row.LifecycleClaimKind == EntityLifecycleClaimKind.DeletingFiles
                    && row.LifecycleClaimId == operationId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.LifecycleClaimKind, (EntityLifecycleClaimKind?)null)
                    .SetProperty(row => row.LifecycleClaimId, (Guid?)null)
                    .SetProperty(row => row.LifecycleClaimedAt, (DateTimeOffset?)null)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken);
            if (released > 0) {
                // ExecuteUpdate intentionally bypasses the change tracker. This scoped DbContext already
                // tracks the claim owner from claim/reconciliation; synchronize it before immediate
                // monitored recovery re-enters Writer/monitor queries and identity-resolves that instance.
                var tracked = db.ChangeTracker.Entries<EntityRow>()
                    .FirstOrDefault(entry => entry.Entity.Id == entityId);
                if (tracked is not null) {
                    tracked.Entity.LifecycleClaimKind = null;
                    tracked.Entity.LifecycleClaimId = null;
                    tracked.Entity.LifecycleClaimedAt = null;
                    tracked.Entity.UpdatedAt = now;
                    tracked.State = EntityState.Unchanged;
                }
            }
            return;
        }

        var row = await db.Entities.FirstOrDefaultAsync(
            candidate => candidate.Id == entityId
                && candidate.LifecycleClaimKind == EntityLifecycleClaimKind.DeletingFiles
                && candidate.LifecycleClaimId == operationId,
            cancellationToken);
        if (row is null) {
            return;
        }
        row.LifecycleClaimKind = null;
        row.LifecycleClaimId = null;
        row.LifecycleClaimedAt = null;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Revalidates every scope input captured before the stable Entity claim. This is the last point where
    /// a fresh operation can release its claim without leaving any monitor/acquisition lifecycle mutation.
    /// </summary>
    private async Task<bool> DeletionScopeStillMatchesAsync(
        Guid rootEntityId,
        IReadOnlyCollection<Guid> expectedEntityIds,
        IReadOnlyDictionary<Guid, DeletionTreeEntity> expectedTree,
        IReadOnlyList<TargetedMonitor> expectedMonitors,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> expectedAcquisitionsByEntity,
        IReadOnlyDictionary<Guid, AcquisitionStatus> expectedAcquisitionStatuses,
        IReadOnlyCollection<Guid> expectedReacquireIds,
        IReadOnlyCollection<Guid> expectedRemovalIds,
        PhysicalDeletionPreparation expectedPhysicalPlan,
        CancellationToken cancellationToken) {
        var currentEntityIds = (await hierarchy.ListSubtreeIdsAsync(
            rootEntityId,
            cancellationToken)).ToArray();
        if (!currentEntityIds.ToHashSet().SetEquals(expectedEntityIds)) {
            return false;
        }

        var currentTree = await LoadTreeAsync(currentEntityIds, cancellationToken);
        if (currentTree.Count != expectedTree.Count
            || currentTree.Any(pair => !expectedTree.TryGetValue(pair.Key, out var expected)
                || expected != pair.Value)) {
            return false;
        }

        var currentMonitors = await ListTargetedMonitorsAsync(currentEntityIds, cancellationToken);
        if (!currentMonitors.ToHashSet().SetEquals(expectedMonitors)) {
            return false;
        }

        var currentAcquisitions = new HashSet<(Guid EntityId, Guid AcquisitionId)>();
        foreach (var entityId in currentEntityIds) {
            foreach (var acquisitionId in await acquisitions.ListIdsForEntityAsync(
                entityId,
                cancellationToken)) {
                currentAcquisitions.Add((entityId, acquisitionId));
            }
        }
        var expectedAcquisitions = expectedAcquisitionsByEntity
            .SelectMany(pair => pair.Value.Select(acquisitionId => (pair.Key, acquisitionId)))
            .ToHashSet();
        if (!currentAcquisitions.SetEquals(expectedAcquisitions)) {
            return false;
        }

        if (expectedAcquisitionStatuses.Count > 0) {
            var expectedStatusIds = expectedAcquisitionStatuses.Keys.ToArray();
            var currentStatuses = await db.Acquisitions.AsNoTracking()
                .Where(row => expectedStatusIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => row.Status, cancellationToken);
            if (currentStatuses.Count != expectedAcquisitionStatuses.Count
                || currentStatuses.Any(pair => !expectedAcquisitionStatuses.TryGetValue(
                    pair.Key,
                    out var expectedStatus) || expectedStatus != pair.Value)) {
                return false;
            }
        }

        // Status equality catches the concrete EF race; eligibility is the application-owned invariant and
        // also covers checkpoint/filesystem conditions that are not represented by the public status alone.
        foreach (var acquisitionId in expectedReacquireIds) {
            if (!(await acquisitions.GetReacquireEligibilityAsync(
                    acquisitionId,
                    cancellationToken)).CanReacquire) {
                return false;
            }
        }
        foreach (var acquisitionId in expectedRemovalIds) {
            if (!(await acquisitions.GetRemovalEligibilityAsync(
                    acquisitionId,
                    cancellationToken)).CanRemove) {
                return false;
            }
        }

        var currentPhysicalPlan = await PreparePhysicalDeletionAsync(
            rootEntityId,
            currentEntityIds,
            cancellationToken);
        return currentPhysicalPlan.Succeeded
            && expectedPhysicalPlan.HasSameTargets(currentPhysicalPlan);
    }

    private async Task<IReadOnlyDictionary<Guid, DeletionTreeEntity>> LoadTreeAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        var ids = entityIds.ToArray();
        var rows = await db.Entities.AsNoTracking()
            .Where(row => ids.Contains(row.Id))
            .Select(row => new { row.Id, row.ParentEntityId, row.KindCode, row.Title })
            .ToArrayAsync(cancellationToken);
        return rows.ToDictionary(
            row => row.Id,
            row => new DeletionTreeEntity(
                row.Id,
                row.ParentEntityId,
                EntityKindRegistry.Require(row.KindCode),
                row.Title));
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ExternalIdentity>>> LoadIdentitiesAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        var ids = entityIds.ToArray();
        var rows = await db.EntityExternalIds.AsNoTracking()
            .Where(row => ids.Contains(row.EntityId))
            .Select(row => new { row.EntityId, row.Provider, row.Value })
            .ToArrayAsync(cancellationToken);
        return rows
            .GroupBy(row => row.EntityId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ExternalIdentity>)group
                    .Select(row => new ExternalIdentity(row.Provider, row.Value))
                    .Distinct()
                    .ToArray());
    }

    private static DeletionPlan BuildDeletionPlan(
        IReadOnlyDictionary<Guid, DeletionTreeEntity> tree,
        IReadOnlyList<TargetedMonitor> targetedMonitors) {
        var directTargetIds = targetedMonitors
            .Where(monitor => IsFileDeletionIntentStatus(monitor.Status))
            .Select(monitor => monitor.TargetEntityId)
            .ToHashSet();
        var wantedIds = directTargetIds.ToHashSet();
        var retainedIds = wantedIds.ToHashSet();
        foreach (var wantedId in wantedIds) {
            var currentId = wantedId;
            var visited = new HashSet<Guid> { wantedId };
            while (tree.TryGetValue(currentId, out var current)
                && current.ParentEntityId is { } parentId
                && tree.ContainsKey(parentId)
                && visited.Add(parentId)) {
                retainedIds.Add(parentId);
                currentId = parentId;
            }
        }

        var removedIds = tree.Keys.Where(entityId => !retainedIds.Contains(entityId)).ToHashSet();
        var removedBranchRootIds = removedIds
            .Where(entityId => tree[entityId].ParentEntityId is not { } parentId || !removedIds.Contains(parentId))
            .ToHashSet();
        return new DeletionPlan(
            directTargetIds,
            wantedIds,
            retainedIds,
            removedIds,
            removedBranchRootIds);
    }

    private async Task<IReadOnlyList<TargetedMonitor>> ListTargetedMonitorsAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken) {
        var targetIds = entityIds.ToArray();
        var targetSet = targetIds.ToHashSet();
        var rows = await (
            from monitor in db.Monitors.AsNoTracking()
            join acquisition in db.Acquisitions.AsNoTracking()
                on monitor.AcquisitionId equals (Guid?)acquisition.Id into linkedAcquisitions
            from acquisition in linkedAcquisitions.DefaultIfEmpty()
            where (monitor.EntityId != null && targetIds.Contains(monitor.EntityId.Value))
                || (acquisition != null
                    && acquisition.EntityId != null
                    && targetIds.Contains(acquisition.EntityId.Value))
            select new {
                monitor.Id,
                monitor.Status,
                monitor.AcquisitionId,
                monitor.EntityId,
                AcquisitionEntityId = acquisition == null ? null : acquisition.EntityId
            })
            .ToArrayAsync(cancellationToken);
        return rows.Select(row => {
                var targetEntityId = new[] { row.EntityId, row.AcquisitionEntityId }
                    .First(candidate => candidate is { } value && targetSet.Contains(value))!.Value;
                return new TargetedMonitor(
                    row.Id,
                    row.Status,
                    targetEntityId,
                    row.AcquisitionId);
            })
            .ToArray();
    }

    private sealed record DeletionTreeEntity(
        Guid Id,
        Guid? ParentEntityId,
        EntityKind Kind,
        string Title);

    private sealed record TargetedMonitor(
        Guid Id,
        MonitorStatus Status,
        Guid TargetEntityId,
        Guid? AcquisitionId);

    private sealed record EntityDeletionClaim(Guid OperationId, bool Resumed);

    private sealed record DeletionPlan(
        IReadOnlySet<Guid> DirectTargetIds,
        IReadOnlySet<Guid> WantedIds,
        IReadOnlySet<Guid> RetainedIds,
        IReadOnlySet<Guid> RemovedIds,
        IReadOnlySet<Guid> RemovedBranchRootIds);

    private sealed record PhysicalDeletionFailure(string Path, string Reason);

    private sealed record PhysicalDeletionTarget(string Path, FileLibraryRoot Root);

    private sealed record PhysicalDeletionPlan(IReadOnlyList<PhysicalDeletionTarget> Targets) {
        public static PhysicalDeletionPlan Empty { get; } = new([]);
    }

    private sealed record PhysicalDeletionPreparation(
        PhysicalDeletionPlan? Plan,
        IReadOnlyList<PhysicalDeletionFailure> Failures) {
        public bool Succeeded => Plan is not null && Failures.Count == 0;

        public static PhysicalDeletionPreparation Success(PhysicalDeletionPlan plan) => new(plan, []);

        public static PhysicalDeletionPreparation Failure(
            IReadOnlyList<PhysicalDeletionFailure> failures) => new(null, failures);

        public bool HasSameTargets(PhysicalDeletionPreparation other) {
            if (Plan is null || other.Plan is null) {
                return false;
            }

            var expected = Plan.Targets
                .Select(target => (target.Root.Id, target.Path))
                .ToHashSet();
            return expected.SetEquals(other.Plan.Targets.Select(target => (target.Root.Id, target.Path)));
        }

        public PhysicalDeletionResult ToResult() => new(0, Failures, []);
    }

    private sealed record PhysicalDeletionResult(
        int PathsDeleted,
        IReadOnlyList<PhysicalDeletionFailure> Failures,
        IReadOnlyList<FileLibraryRoot> TouchedRoots) {
        public static PhysicalDeletionResult Success { get; } = new(0, [], []);
        public bool Succeeded => Failures.Count == 0;
    }

    private static bool IsFileDeletionIntentStatus(MonitorStatus status) =>
        status is MonitorStatus.Active or MonitorStatus.DeletingFiles;

    private static string PhysicalDeletionConflictMessage(PhysicalDeletionResult result) {
        var failures = string.Join(
            "; ",
            result.Failures.Select(failure => $"\"{failure.Path}\": {failure.Reason}"));
        var partial = result.PathsDeleted > 0
            ? $" {result.PathsDeleted} path(s) were already removed successfully; their database rows were preserved and retrying is safe."
            : string.Empty;
        return $"Managed file deletion did not complete, so no library rows were changed.{partial} Resolve these paths and retry: {failures}";
    }

    /// <summary>
    /// Freezes the direct monitor set idempotently. Relational updates claim only Active rows, then verify
    /// the complete immutable set is DeletingFiles before any external side effect.
    /// </summary>
    private async Task<bool> ClaimDirectMonitorsAsync(
        IReadOnlyCollection<Guid> monitorIds,
        CancellationToken cancellationToken) {
        if (monitorIds.Count == 0) {
            return true;
        }

        var ids = monitorIds.ToArray();
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            await db.Monitors
                .Where(row => ids.Contains(row.Id) && row.Status == MonitorStatus.Active)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, MonitorStatus.DeletingFiles)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken);
            return await db.Monitors.AsNoTracking().CountAsync(
                row => ids.Contains(row.Id) && row.Status == MonitorStatus.DeletingFiles,
                cancellationToken) == ids.Length;
        }

        var rows = await db.Monitors.Where(row => ids.Contains(row.Id)).ToArrayAsync(cancellationToken);
        if (rows.Length != ids.Length
            || rows.Any(row => row.Status is not (MonitorStatus.Active or MonitorStatus.DeletingFiles))) {
            return false;
        }
        foreach (var row in rows.Where(row => row.Status == MonitorStatus.Active)) {
            row.Status = MonitorStatus.DeletingFiles;
            row.UpdatedAt = now;
        }
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Re-enables only the direct monitor rows claimed by this completed managed deletion.</summary>
    private async Task RestoreDirectMonitorsAsync(
        IReadOnlyCollection<Guid> monitorIds,
        CancellationToken cancellationToken) {
        if (monitorIds.Count == 0) {
            return;
        }

        var ids = monitorIds.ToArray();
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            await db.Monitors
                .Where(row => ids.Contains(row.Id) && row.Status == MonitorStatus.DeletingFiles)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, MonitorStatus.Active)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken);
            return;
        }

        var rows = await db.Monitors
            .Where(row => ids.Contains(row.Id) && row.Status == MonitorStatus.DeletingFiles)
            .ToArrayAsync(cancellationToken);
        foreach (var row in rows) {
            row.Status = MonitorStatus.Active;
            row.UpdatedAt = now;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task QueueReconciliationScansAsync(
        IReadOnlyList<FileLibraryRoot> touchedRoots) {
        var scanRoots = touchedRoots
            .GroupBy(root => root.Id)
            .Select(group => group.First())
            .Where(root => root.Enabled)
            .ToArray();
        if (scanRoots.Length == 0) {
            return;
        }

        try {
            // Everything destructive has already committed. Caller cancellation or a transient queue
            // failure cannot truthfully turn the completed deletion into a failed HTTP operation; the
            // regular scan schedule can reconcile later, while the user receives the real success state.
            await LibraryScanJobs.QueueScansForKindsAsync(
                jobs,
                scanRoots.Any(root => root.ScanVideos),
                scanRoots.Any(root => root.ScanImages),
                scanRoots.Any(root => root.ScanAudio),
                scanRoots.Any(root => root.ScanBooks),
                CancellationToken.None);
        } catch (Exception exception) {
            logger.LogWarning(
                exception,
                "MediaEntityDeletion: files and Entity state were reconciled, but follow-up library scans could not be queued.");
        }
    }

    /// <summary>
    /// Resolves and validates the immutable physical deletion plan without touching disk or lifecycle
    /// state. Outside-owner candidates are bounded to the watched roots actually touched by this operation,
    /// avoiding an all-library Source-row materialization for a single book, episode, or image.
    /// </summary>
    private async Task<PhysicalDeletionPreparation> PreparePhysicalDeletionAsync(
        Guid targetEntityId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) {
        var entityIds = ids.ToArray();
        var paths = await db.EntityFiles.AsNoTracking()
            .Where(file => entityIds.Contains(file.EntityId) && file.Role == EntityFileRole.Source)
            .Select(file => file.Path)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (paths.Count == 0) {
            return PhysicalDeletionPreparation.Success(PhysicalDeletionPlan.Empty);
        }

        var deletionPaths = TopLevelPaths(paths.Select(EntitySourcePath.PhysicalOwner).Distinct().ToArray()).ToArray();
        var allRoots = await roots.ListRootsAsync(cancellationToken);
        var targets = new List<PhysicalDeletionTarget>();
        var rootFailures = new List<PhysicalDeletionFailure>();
        foreach (var path in deletionPaths) {
            var root = allRoots
                .Where(candidate => IsUnder(candidate.Path, path))
                .OrderByDescending(candidate => candidate.Path.Length)
                .FirstOrDefault();
            if (root is null) {
                rootFailures.Add(new PhysicalDeletionFailure(
                    path,
                    "the path is outside every watched library root"));
                continue;
            }

            targets.Add(new PhysicalDeletionTarget(path, root));
        }
        if (rootFailures.Count > 0) {
            return PhysicalDeletionPreparation.Failure(rootFailures);
        }

        // Load only the identity columns but do not SQL-prefix-filter them. PostgreSQL comparison is
        // always case-sensitive while a Windows media filesystem is not; filtering in SQL could omit a
        // case-variant outside owner and make destructive validation unsafe. The bounded in-memory pass
        // below applies the host's canonical FileSystemPathComparison to every Source owner.
        var outsideSources = await db.EntityFiles.AsNoTracking()
            .Where(file => !entityIds.Contains(file.EntityId) && file.Role == EntityFileRole.Source)
            .Select(file => new { file.EntityId, file.Path })
            .ToArrayAsync(cancellationToken);
        var ancestorIds = (await hierarchy.ListAncestorIdsAsync(targetEntityId, cancellationToken)).ToHashSet();
        var ownershipConflicts = new List<PhysicalDeletionFailure>();
        foreach (var deletionPath in deletionPaths) {
            foreach (var outside in outsideSources) {
                var outsidePath = EntitySourcePath.PhysicalOwner(outside.Path);
                if (IsUnder(deletionPath, outsidePath)) {
                    ownershipConflicts.Add(new PhysicalDeletionFailure(
                        deletionPath,
                        $"it also contains source media owned by Entity {outside.EntityId}"));
                    continue;
                }

                if (IsUnder(outsidePath, deletionPath) && !ancestorIds.Contains(outside.EntityId)) {
                    ownershipConflicts.Add(new PhysicalDeletionFailure(
                        deletionPath,
                        $"it overlaps the source folder owned by unrelated Entity {outside.EntityId}"));
                }
            }
        }
        if (ownershipConflicts.Count > 0) {
            return PhysicalDeletionPreparation.Failure(ownershipConflicts);
        }

        return PhysicalDeletionPreparation.Success(new PhysicalDeletionPlan(targets));
    }

    /// <summary>
    /// Permanently applies a preflighted physical plan. A path already gone counts as done; transient
    /// storage failures are returned as structured conflicts so Entity/source rows stay intact.
    /// </summary>
    private async Task<PhysicalDeletionResult> DeletePreparedSourcePathsAsync(
        PhysicalDeletionPlan plan,
        CancellationToken cancellationToken) {
        var deleted = 0;
        var touchedRoots = new List<FileLibraryRoot>();
        var failures = new List<PhysicalDeletionFailure>();
        foreach (var target in plan.Targets) {
            try {
                await storage.DeleteAsync(
                    new ResolvedFilePath(
                        target.Root,
                        Path.GetRelativePath(target.Root.Path, target.Path),
                        target.Path),
                    cancellationToken);
                deleted++;
                touchedRoots.Add(target.Root);
            } catch (OperationCanceledException) {
                throw;
            } catch (FileNotFoundException) {
                deleted++;
                touchedRoots.Add(target.Root);
            } catch (DirectoryNotFoundException) {
                deleted++;
                touchedRoots.Add(target.Root);
            } catch (FileOperationException exception) when (exception.Code == ApiProblemCodes.NotFound) {
                deleted++;
                touchedRoots.Add(target.Root);
            } catch (Exception ex) {
                logger.LogWarning(ex, "MediaEntityDeletion: failed to delete \"{Path}\".", target.Path);
                failures.Add(new PhysicalDeletionFailure(target.Path, ex.Message));
            }
        }

        return new PhysicalDeletionResult(deleted, failures, touchedRoots);
    }

    /// <summary>Drops paths contained in another listed path, so a folder delete isn't repeated for its children.</summary>
    private static IEnumerable<string> TopLevelPaths(IReadOnlyList<string> paths) {
        var kept = new List<string>();
        foreach (var path in paths.OrderBy(path => path.Length)) {
            if (!kept.Any(top => IsUnder(top, path))) {
                kept.Add(path);
                yield return path;
            }
        }
    }

    /// <summary>True when <paramref name="path"/> equals or lives under <paramref name="parent"/>.</summary>
    private static bool IsUnder(string parent, string path) =>
        FileSystemPathComparison.IsSameOrDescendant(parent, path);
}
