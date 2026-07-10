using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Requests;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// EF implementation of generalized Entity unmonitor cleanup. Scope follows only ParentEntityId and
/// acquisition EntityId links; it contains no movie/season/album/book branches.
/// </summary>
public sealed class EfEntityUnmonitorPersistence(
    PrismediaDbContext db,
    IEntityHierarchyReader hierarchy,
    EntityAssetCleanupService? assetCleanup = null,
    IWantedSuppressionStore? suppressionStore = null) : IEntityUnmonitorPersistence {
    private readonly IWantedSuppressionStore suppressions =
        suppressionStore ?? new EfWantedSuppressionStore(db);

    /// <inheritdoc />
    public async Task<EntityUnmonitorScope?> ResolveAsync(
        Guid monitorId,
        CancellationToken cancellationToken) {
        var monitor = await db.Monitors.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == monitorId, cancellationToken);
        if (monitor is null) {
            return null;
        }

        Guid? rootEntityId = monitor.EntityId;
        if (rootEntityId is null && monitor.AcquisitionId is { } acquisitionId) {
            rootEntityId = await db.Acquisitions.AsNoTracking()
                .Where(row => row.Id == acquisitionId)
                .Select(row => row.EntityId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var entityIds = rootEntityId is { } root
            ? await hierarchy.ListSubtreeIdsAsync(root, cancellationToken)
            : [];
        var acquisitionIds = entityIds.Count == 0
            ? new List<Guid>()
            : await db.Acquisitions.AsNoTracking()
                .Where(row => row.EntityId != null && entityIds.Contains(row.EntityId.Value))
                .OrderBy(row => row.CreatedAt)
                .Select(row => row.Id)
                .ToListAsync(cancellationToken);

        AddIfPresent(acquisitionIds, monitor.AcquisitionId);
        AddIfPresent(acquisitionIds, monitor.UpgradeChildAcquisitionId);
        await AddUpgradeDescendantsAsync(acquisitionIds, cancellationToken);
        // The exact monitor-linked acquisition is the last removal. For the legacy/ad-hoc case with no
        // Entity root, this preserves the only durable scope link until every upgrade child is gone.
        if (monitor.AcquisitionId is { } rootAcquisitionId && acquisitionIds.Remove(rootAcquisitionId)) {
            acquisitionIds.Add(rootAcquisitionId);
        }

        var monitorIds = await db.Monitors.AsNoTracking()
            .Where(row =>
                row.Id == monitorId
                || (row.EntityId != null && entityIds.Contains(row.EntityId.Value))
                || (row.AcquisitionId != null && acquisitionIds.Contains(row.AcquisitionId.Value))
                || (row.UpgradeChildAcquisitionId != null && acquisitionIds.Contains(row.UpgradeChildAcquisitionId.Value)))
            .Select(row => row.Id)
            .ToArrayAsync(cancellationToken);

        var rootSuppression = rootEntityId is { } rootId
            ? await ResolveRootSuppressionAsync(rootId, cancellationToken)
            : null;
        var acquisitionStatuses = await ResolveAcquisitionStatusesAsync(
            acquisitionIds,
            cancellationToken);
        return new EntityUnmonitorScope(
            monitorId,
            rootEntityId,
            entityIds,
            acquisitionIds,
            monitorIds,
            rootSuppression,
            AcquisitionStatuses: acquisitionStatuses);
    }

    /// <inheritdoc />
    public async Task<EntityUnmonitorScope?> ResolveForEntityAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var existingMonitorId = await (
            from monitor in db.Monitors.AsNoTracking()
            join acquisition in db.Acquisitions.AsNoTracking()
                on monitor.AcquisitionId equals acquisition.Id into linkedAcquisitions
            from acquisition in linkedAcquisitions.DefaultIfEmpty()
            where monitor.EntityId == entityId
                || (monitor.EntityId == null && acquisition != null && acquisition.EntityId == entityId)
            orderby monitor.EntityId == entityId descending, monitor.CreatedAt
            select (Guid?)monitor.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingMonitorId is { } monitorId) {
            return await ResolveAsync(monitorId, cancellationToken);
        }

        return await ResolveSyntheticEntityScopeAsync(entityId, Guid.NewGuid(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ClaimAsync(
        EntityUnmonitorScope scope,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<bool>>? revalidateRemovalEligibility = null) {
        var monitorLocks = await ResolveClaimMonitorLocksAsync(scope, cancellationToken);
        var lifecycleEntityIds = scope.RootEntityId is { } rootEntityId
            ? new[] { rootEntityId }
                .Concat(await hierarchy.ListAncestorIdsAsync(rootEntityId, cancellationToken))
                .Distinct()
                .ToArray()
            : [];
        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational()) {
            transaction = await db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.ReadCommitted,
                cancellationToken);
        }

        try {
            // Lock the complete captured monitor set plus every monitored ancestor in deterministic order.
            // Provider discovery holds its container monitor lock for the whole Entity mutation, so sync-
            // first becomes visible before this ReadCommitted re-resolution and claim-first makes the sync
            // wait until the child Stopping barrier is durable.
            var lockedRows = new List<MonitorRow>(monitorLocks.AllMonitorIds.Count);
            foreach (var lockId in monitorLocks.AllMonitorIds.Order()) {
                var locked = db.Database.IsRelational()
                    && db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
                    ? await db.Monitors
                        .FromSqlInterpolated($"SELECT * FROM monitors WHERE id = {lockId} FOR UPDATE")
                        .SingleOrDefaultAsync(cancellationToken)
                    : await db.Monitors.FirstOrDefaultAsync(row => row.Id == lockId, cancellationToken);
                if (locked is not null) {
                    lockedRows.Add(locked);
                }
            }
            if ((!scope.SyntheticMonitorAnchor && lockedRows.All(row => row.Id != scope.MonitorId))
                || lockedRows.Any(row => monitorLocks.AncestorMonitorIds.Contains(row.Id)
                    && row.Status is MonitorStatus.Stopping or MonitorStatus.DeletingFiles)) {
                return false;
            }

            // A monitorless placeholder has no monitor row that can serialize give-up with simultaneous
            // explicit intent. Lock the stable target + ancestor Entity chain after the monitor chain, the
            // same ordering used by request/monitor/delete, and reject a managed file-deletion owner.
            if (scope.RootEntityId is { } claimedRootEntityId) {
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
                if (lockedEntities.All(row => row.Id != claimedRootEntityId)
                    || lockedEntities.Any(row => row.LifecycleClaimKind != null)) {
                    return false;
                }
            }

            // Re-resolve the whole graph under the serializable claim, including upgrade descendants that
            // may not carry an EntityId. Comparing only direct subtree acquisitions would let a newly
            // spawned upgrade child slip past preflight and be torn down without an eligibility check.
            var current = scope.SyntheticMonitorAnchor && scope.RootEntityId is { } syntheticRootEntityId
                ? await ResolveSyntheticEntityScopeAsync(
                    syntheticRootEntityId,
                    scope.MonitorId,
                    cancellationToken)
                : await ResolveAsync(scope.MonitorId, cancellationToken);
            if (current is null || !await IsSafeClaimScopeAsync(scope, current, cancellationToken)) {
                return false;
            }

            // Eligibility can change without changing graph membership (most importantly when a queued
            // import claims Downloaded -> Importing). Re-run the application policy while the same Entity
            // and monitor locks are held, before publishing suppression or Stopping monitor state.
            if (revalidateRemovalEligibility is not null
                && !await revalidateRemovalEligibility(cancellationToken)) {
                return false;
            }

            var rows = await db.Monitors
                .Where(row => scope.MonitorIds.Contains(row.Id))
                .ToArrayAsync(cancellationToken);
            var claim = rows.FirstOrDefault(row => row.Id == scope.MonitorId);
            if ((!scope.SyntheticMonitorAnchor && claim is null)
                || rows.Any(row => row.Status == MonitorStatus.DeletingFiles)) {
                return false;
            }

            // Delete descendant/helper monitor rows first so the unique EntityId slot can become the
            // exact endpoint monitor's durable cleanup anchor. Both saves share one relational transaction.
            var superseded = rows.Where(row => row.Id != scope.MonitorId).ToArray();
            if (superseded.Length > 0) {
                db.Monitors.RemoveRange(superseded);
                await db.SaveChangesAsync(cancellationToken);
            }

            if (claim is null) {
                if (scope.RootEntityId is not { } claimedEntityId
                    || scope.RootSuppression is not { } claimedTarget) {
                    return false;
                }

                var now = DateTimeOffset.UtcNow;
                claim = new MonitorRow {
                    Id = scope.MonitorId,
                    EntityId = claimedEntityId,
                    Kind = claimedTarget.Kind,
                    Title = claimedTarget.Title,
                    Status = MonitorStatus.Stopping,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Monitors.Add(claim);
            }

            claim.EntityId = scope.RootEntityId;
            claim.Status = MonitorStatus.Stopping;
            claim.UpdatedAt = DateTimeOffset.UtcNow;
            if (scope.RootSuppression is { } target) {
                // Suppression shares this transaction and is published before ancestor row locks release.
                // A waiting provider sync therefore either sees the child Stopping barrier or, after
                // completion removes it, filters this durable root identity at its mutation boundary.
                await suppressions.SuppressAsync(
                    target.ExternalIdentities,
                    target.Kind,
                    target.Title,
                    cancellationToken);
            }
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) {
                await transaction.CommitAsync(cancellationToken);
            }

            return true;
        } catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation) {
            return false;
        } catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException postgres
            && postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation) {
            return false;
        } catch (DbUpdateConcurrencyException) {
            return false;
        } finally {
            if (transaction is not null) {
                await transaction.DisposeAsync();
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAsync(
        EntityUnmonitorScope scope,
        CancellationToken cancellationToken) {
        // Completion re-reads the current subtree so provider-only fileless descendants that raced the
        // claim are included. New acquisitions, direct monitors, source files, and their ancestor closure
        // remain explicit retention authority inside PruneAcquisitionOnlyBranchesAsync.
        var entityIds = await ResolveCompletionEntityIdsAsync(scope, cancellationToken);
        var monitorIds = scope.MonitorIds.ToHashSet();
        var monitors = await db.Monitors
            .Where(row => monitorIds.Contains(row.Id))
            .ToArrayAsync(cancellationToken);
        db.Monitors.RemoveRange(monitors);

        var prunedAssets = entityIds.Count > 0
            ? await PruneAcquisitionOnlyBranchesAsync(scope, entityIds, cancellationToken)
            : null;

        await db.SaveChangesAsync(cancellationToken);
        if (prunedAssets is not null) {
            // Filesystem cleanup follows the successful DB commit. A best-effort cache failure must not
            // resurrect acquisition-only Entities, and retained source-backed closure is never included.
            assetCleanup?.Cleanup(
                prunedAssets.EntityIds,
                preserveArtwork: false,
                prunedAssets.RecordedAssetPaths);
        }

        return scope.RootEntityId is { } rootEntityId
            && !await db.Entities.AsNoTracking().AnyAsync(
                entity => entity.Id == rootEntityId,
                cancellationToken);
    }

    /// <summary>
    /// Builds a monitorless Entity give-up scope around a stable not-yet-persisted stopping anchor. Passing
    /// the same anchor id during claim makes graph revalidation comparable without publishing an Active
    /// monitor that background work could observe.
    /// </summary>
    private async Task<EntityUnmonitorScope?> ResolveSyntheticEntityScopeAsync(
        Guid rootEntityId,
        Guid syntheticMonitorId,
        CancellationToken cancellationToken) {
        var rootSuppression = await ResolveRootSuppressionAsync(rootEntityId, cancellationToken);
        if (rootSuppression is null) {
            return null;
        }

        var entityIds = await hierarchy.ListSubtreeIdsAsync(rootEntityId, cancellationToken);
        if (!entityIds.Contains(rootEntityId)) {
            return null;
        }

        var acquisitionIds = await db.Acquisitions.AsNoTracking()
            .Where(row => row.EntityId != null && entityIds.Contains(row.EntityId.Value))
            .OrderBy(row => row.CreatedAt)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);
        await AddUpgradeDescendantsAsync(acquisitionIds, cancellationToken);

        var monitorIds = await db.Monitors.AsNoTracking()
            .Where(row => (row.EntityId != null && entityIds.Contains(row.EntityId.Value))
                || (row.AcquisitionId != null && acquisitionIds.Contains(row.AcquisitionId.Value))
                || (row.UpgradeChildAcquisitionId != null
                    && acquisitionIds.Contains(row.UpgradeChildAcquisitionId.Value)))
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);
        AddIfPresent(monitorIds, syntheticMonitorId);
        var acquisitionStatuses = await ResolveAcquisitionStatusesAsync(
            acquisitionIds,
            cancellationToken);

        return new EntityUnmonitorScope(
            syntheticMonitorId,
            rootEntityId,
            entityIds,
            acquisitionIds,
            monitorIds,
            rootSuppression,
            SyntheticMonitorAnchor: true,
            AcquisitionStatuses: acquisitionStatuses);
    }

    /// <summary>
    /// Monitor rows whose locks form the child-off/discovery serialization boundary. Captured descendants
    /// protect their own syncs; monitored ancestors protect provider materialization into the child scope.
    /// </summary>
    private async Task<ClaimMonitorLocks> ResolveClaimMonitorLocksAsync(
        EntityUnmonitorScope scope,
        CancellationToken cancellationToken) {
        if (scope.RootEntityId is not { } rootEntityId) {
            return new ClaimMonitorLocks(scope.MonitorIds.ToHashSet(), new HashSet<Guid>());
        }

        var ancestorEntityIds = await hierarchy.ListAncestorIdsAsync(rootEntityId, cancellationToken);
        if (ancestorEntityIds.Count == 0) {
            return new ClaimMonitorLocks(scope.MonitorIds.ToHashSet(), new HashSet<Guid>());
        }

        var ancestorIds = ancestorEntityIds.ToArray();
        var ancestorAcquisitionIds = await db.Acquisitions.AsNoTracking()
            .Where(row => row.EntityId != null && ancestorIds.Contains(row.EntityId.Value))
            .Select(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var ancestorMonitorIds = await db.Monitors.AsNoTracking()
            .Where(row => (row.EntityId != null && ancestorIds.Contains(row.EntityId.Value))
                || (row.AcquisitionId != null && ancestorAcquisitionIds.Contains(row.AcquisitionId.Value)))
            .Select(row => row.Id)
            .ToHashSetAsync(cancellationToken);
        var allMonitorIds = scope.MonitorIds.ToHashSet();
        allMonitorIds.UnionWith(ancestorMonitorIds);
        return new ClaimMonitorLocks(allMonitorIds, ancestorMonitorIds);
    }

    /// <summary>
    /// Allows only provider-style Entity expansion between preflight and claim. Acquisition/monitor drift,
    /// a removed captured Entity, or a newly source-backed Entity still conflicts because it represents
    /// state that was never preflighted. Fileless descendants with no explicit intent are safe to absorb.
    /// </summary>
    private async Task<bool> IsSafeClaimScopeAsync(
        EntityUnmonitorScope initial,
        EntityUnmonitorScope current,
        CancellationToken cancellationToken) {
        if (current.RootEntityId != initial.RootEntityId
            || !current.AcquisitionIds.ToHashSet().SetEquals(initial.AcquisitionIds)
            || !current.MonitorIds.ToHashSet().SetEquals(initial.MonitorIds)) {
            return false;
        }

        if (initial.AcquisitionStatuses is not null
            && (current.AcquisitionStatuses is null
                || current.AcquisitionStatuses.Count != initial.AcquisitionStatuses.Count
                || initial.AcquisitionStatuses.Any(expected =>
                    !current.AcquisitionStatuses.TryGetValue(expected.Key, out var status)
                    || status != expected.Value))) {
            return false;
        }

        var currentEntityIds = current.EntityIds.ToHashSet();
        if (!initial.EntityIds.All(currentEntityIds.Contains)) {
            return false;
        }

        var addedEntityIds = currentEntityIds.Except(initial.EntityIds).ToArray();
        return addedEntityIds.Length == 0
            || !await db.EntityFiles.AsNoTracking().AnyAsync(
                row => addedEntityIds.Contains(row.EntityId) && row.Role == EntityFileRole.Source,
                cancellationToken);
    }

    /// <summary>Captures the lifecycle state whose removal policy was checked before the claim.</summary>
    private async Task<IReadOnlyDictionary<Guid, AcquisitionStatus>> ResolveAcquisitionStatusesAsync(
        IReadOnlyCollection<Guid> acquisitionIds,
        CancellationToken cancellationToken) {
        if (acquisitionIds.Count == 0) {
            return new Dictionary<Guid, AcquisitionStatus>();
        }

        var ids = acquisitionIds.Distinct().ToArray();
        return await db.Acquisitions.AsNoTracking()
            .Where(row => ids.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.Status, cancellationToken);
    }

    /// <summary>Captured scope plus the root's current descendants, with stable first-seen ordering.</summary>
    private async Task<IReadOnlyList<Guid>> ResolveCompletionEntityIdsAsync(
        EntityUnmonitorScope scope,
        CancellationToken cancellationToken) {
        if (scope.RootEntityId is not { } rootEntityId) {
            return scope.EntityIds;
        }

        var result = scope.EntityIds.ToList();
        var seen = result.ToHashSet();
        foreach (var entityId in await hierarchy.ListSubtreeIdsAsync(rootEntityId, cancellationToken)) {
            if (seen.Add(entityId)) {
                result.Add(entityId);
            }
        }
        return result;
    }

    /// <summary>Expands acquisition ids through the upgrade parent link without a depth bound.</summary>
    private async Task AddUpgradeDescendantsAsync(
        List<Guid> acquisitionIds,
        CancellationToken cancellationToken) {
        var visited = acquisitionIds.ToHashSet();
        IReadOnlyList<Guid> frontier = acquisitionIds.ToArray();
        while (frontier.Count > 0) {
            var parents = frontier.ToArray();
            var children = await db.Acquisitions.AsNoTracking()
                .Where(row => row.UpgradeOfAcquisitionId != null && parents.Contains(row.UpgradeOfAcquisitionId.Value))
                .Select(row => row.Id)
                .ToArrayAsync(cancellationToken);
            var next = new List<Guid>();
            foreach (var child in children) {
                if (!visited.Add(child)) {
                    continue;
                }

                acquisitionIds.Add(child);
                next.Add(child);
            }

            frontier = next;
        }
    }

    private async Task<UnmonitorSuppressionTarget?> ResolveRootSuppressionAsync(
        Guid rootEntityId,
        CancellationToken cancellationToken) {
        var entity = await db.Entities.AsNoTracking()
            .Where(row => row.Id == rootEntityId)
            .Select(row => new { row.Id, row.KindCode, row.Title })
            .FirstOrDefaultAsync(cancellationToken);
        if (entity is null || !EntityKindRegistry.TryGet(entity.KindCode, out var kind)) {
            return null;
        }

        var identities = await db.EntityExternalIds.AsNoTracking()
            .Where(row => row.EntityId == rootEntityId)
            .Select(row => new { row.Provider, row.Value })
            .ToArrayAsync(cancellationToken);
        return new UnmonitorSuppressionTarget(
            entity.Id,
            kind,
            entity.Title,
            identities.Select(row => new ExternalIdentity(row.Provider, row.Value)).ToArray());
    }

    /// <summary>
    /// Deletes acquisition-only branches regardless of stale Wanted flags. A source-backed Entity,
    /// concurrent post-claim intent, and the ancestors needed to connect either are retained. Only the
    /// source/structural closure owned by this unmonitor is normalized to non-Wanted; a concurrent-intent
    /// target and its ancestor closure preserve their current Wanted state.
    /// </summary>
    private async Task<PrunedEntityAssets?> PruneAcquisitionOnlyBranchesAsync(
        EntityUnmonitorScope scope,
        IReadOnlyList<Guid> entityIds,
        CancellationToken cancellationToken) {
        var rows = await db.Entities
            .Where(row => entityIds.Contains(row.Id))
            .ToArrayAsync(cancellationToken);
        if (rows.Length == 0) {
            return null;
        }

        var sourceIds = await db.EntityFiles.AsNoTracking()
            .Where(row => entityIds.Contains(row.EntityId) && row.Role == EntityFileRole.Source)
            .Select(row => row.EntityId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        // The final library graph is the source-backed closure, not the IsWanted flag. A failed/import
        // race can leave a fileless acquisition-created row with IsWanted=false; retaining by that stale
        // flag would preserve exactly the erroneous state unmonitoring promises to clear.
        var retained = sourceIds.ToHashSet();

        // Preserve any captured Entity that gained a new acquisition after the immutable scope was
        // claimed. That explicit intent belongs to a newer lifecycle operation and cannot be normalized.
        var concurrentAcquisitionEntityIds = await db.Acquisitions.AsNoTracking()
            .Where(row => row.EntityId != null
                && entityIds.Contains(row.EntityId.Value)
                && !scope.AcquisitionIds.Contains(row.Id))
            .Select(row => row.EntityId!.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var concurrentIntentRetained = concurrentAcquisitionEntityIds.ToHashSet();

        // A post-claim direct monitor is equally explicit new intent even when it has no acquisition yet.
        // Preserve its target and closure; completion only owns monitor ids captured in the immutable scope.
        var capturedMonitorIds = scope.MonitorIds.ToArray();
        var concurrentMonitorEntityIds = await (
            from monitor in db.Monitors.AsNoTracking()
            join acquisition in db.Acquisitions.AsNoTracking()
                on monitor.AcquisitionId equals acquisition.Id into linkedAcquisitions
            from acquisition in linkedAcquisitions.DefaultIfEmpty()
            let targetEntityId = monitor.EntityId
                ?? (acquisition == null ? null : acquisition.EntityId)
            where !capturedMonitorIds.Contains(monitor.Id)
                && targetEntityId != null
                && entityIds.Contains(targetEntityId.Value)
            select targetEntityId.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        concurrentIntentRetained.UnionWith(concurrentMonitorEntityIds);

        var parentsOfNewChildren = await db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId != null
                && entityIds.Contains(row.ParentEntityId.Value)
                && !entityIds.Contains(row.Id))
            .Select(row => row.ParentEntityId!.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        retained.UnionWith(parentsOfNewChildren);

        var byId = rows.ToDictionary(row => row.Id);
        // New explicit intent wins over this older cleanup claim. Preserve its current Wanted state on
        // the target and captured ancestors; otherwise a concurrent child request can survive while its
        // graph is silently rewritten as ordinary on-disk structure.
        var concurrentFrontier = concurrentIntentRetained.ToArray();
        foreach (var id in concurrentFrontier) {
            var current = byId.GetValueOrDefault(id)?.ParentEntityId;
            while (current is { } parentId
                && byId.TryGetValue(parentId, out var parent)
                && concurrentIntentRetained.Add(parentId)) {
                current = parent.ParentEntityId;
            }
        }
        retained.UnionWith(concurrentIntentRetained);

        var frontier = retained.ToArray();
        foreach (var id in frontier) {
            var current = byId.GetValueOrDefault(id)?.ParentEntityId;
            while (current is { } parentId && byId.TryGetValue(parentId, out var parent) && retained.Add(parentId)) {
                current = parent.ParentEntityId;
            }
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var retainedWanted in rows.Where(row =>
            row.IsWanted
            && retained.Contains(row.Id)
            && !concurrentIntentRetained.Contains(row.Id))) {
            retainedWanted.IsWanted = false;
            retainedWanted.UpdatedAt = now;
        }

        var removedIds = rows
            .Where(row => !retained.Contains(row.Id))
            .Select(row => row.Id)
            .ToArray();
        var recordedAssetPaths = removedIds.Length == 0
            ? Array.Empty<string>()
            : await db.EntityFiles.AsNoTracking()
                .Where(row => removedIds.Contains(row.EntityId) && row.Role != EntityFileRole.Source)
                .Select(row => row.Path)
                .Distinct()
                .ToArrayAsync(cancellationToken);
        db.Entities.RemoveRange(rows.Where(row => !retained.Contains(row.Id)));
        return removedIds.Length == 0
            ? null
            : new PrunedEntityAssets(removedIds, recordedAssetPaths);
    }

    /// <summary>Filesystem assets captured before cascading Entity-file rows are committed away.</summary>
    private sealed record PrunedEntityAssets(
        IReadOnlyCollection<Guid> EntityIds,
        IReadOnlyCollection<string> RecordedAssetPaths);

    private sealed record ClaimMonitorLocks(
        IReadOnlySet<Guid> AllMonitorIds,
        IReadOnlySet<Guid> AncestorMonitorIds);

    private static void AddIfPresent(ICollection<Guid> ids, Guid? value) {
        if (value is { } id && !ids.Contains(id)) {
            ids.Add(id);
        }
    }
}
