using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Acquisition;
using System.Text.Json;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Integrations;

public sealed partial class EfManagedTrackingStore {
    private static readonly TimeSpan TrackingInterval = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public async Task ApplyAsync(ManagedTrackingWork work, ManagedTrackingObservation observation,
        IReadOnlyList<ManagedFileBinding>? adoption, IReadOnlyList<ManagedSourceChange> changes, CancellationToken token) {
        var ids = (adoption ?? work.Tracking.Bindings).SelectMany(file => file.Entities).Select(owner => owner.EntityId).Distinct().ToArray();
        var reservationIds = ids.Length == 0 ? [] : await new ManagedReservationScopeResolver(db).ResolveAsync(ids, work.Tracking.Item, token);
        var requestEntityId = await db.ManagedRequests.AsNoTracking()
            .Where(request => request.Id == work.Tracking.Id)
            .Select(request => (Guid?)request.EntityId)
            .SingleOrDefaultAsync(token);
        var restoredEntityIds = ids.Append(requestEntityId ?? Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (!await lifecycle.ExecuteManyAsync(ids.Concat(reservationIds).Distinct().ToArray(), async leaseToken => {
            if (ids.Length > 0 && !reservationIds.ToHashSet().SetEquals(
                    await new ManagedReservationScopeResolver(db).ResolveAsync(ids, work.Tracking.Item, leaseToken)))
                throw new ArgumentException("The selected book work changed during reconciliation.");
            var row = await RequireRevisionAsync(work.Tracking.Id, work.Tracking.Revision, leaseToken);
            var mount = await db.ExternalLibraryMounts.AsNoTracking().SingleAsync(mount => mount.ConnectionId == row.ConnectionId && mount.LibraryRootId == row.LibraryRootId, leaseToken);
            foreach (var file in observation.Files) {
                if (file.LocalPath.Length > 0 && !FileSystemPathComparison.IsSameOrDescendant(
                    CompletedPayloadFileSystem.CanonicalPath(mount.LocalPath), CompletedPayloadFileSystem.CanonicalPath(file.LocalPath)))
                    throw new ArgumentException("A mapped source now escapes its established library boundary.");
                VerifyUnchangedBytes(file);
            }
            if (adoption is not null) {
                var current = await SourcesAsync(observation.Files.Select(file => file.LocalPath).ToArray(), leaseToken);
                var checkedPlan = ManagedSourceAdoption.Plan(observation.Files, work.Selections, current);
                if (checkedPlan.ReviewReason is not null) throw new ArgumentException(checkedPlan.ReviewReason);
                row.TargetsJson = JsonSerializer.Serialize(checkedPlan.Bindings.SelectMany(file => file.Entities)
                    .Select(owner => new ManagedTargetBinding(owner.Target, owner.EntityId)).ToArray(), Json);
                var checkedOwners = await new ManagedReservationScopeResolver(db).ResolveAsync(ids, work.Tracking.Item, leaseToken);
                if (!checkedOwners.SequenceEqual(reservationIds))
                    throw new ArgumentException("The selected book work changed during source adoption.");
                await RequireUnownedAsync(ids, work.Tracking.Item.EntityKind, work.Tracking.Item.BookRendition,
                    work.Tracking.Item.ExpectedExternalIds, leaseToken);
                foreach (var id in checkedOwners)
                    await new EfFulfillmentReservationStore(db).ReserveAsync(row.Id, FulfillmentOwnerKind.ConnectedLibrary,
                        row.ConnectionId, id, work.Tracking.Item.BookRendition, leaseToken);
                foreach (var file in checkedPlan.Bindings) foreach (var owner in file.Entities) {
                    db.ManagedSourceBindings.Add(new() { Id = Guid.NewGuid(), HoldingId = row.Id, EntityId = owner.EntityId,
                        SourceFileId = owner.SourceFileId, RemoteTargetId = owner.Target.RemoteTargetId, Kind = owner.Target.Kind,
                        SeasonNumber = owner.Target.SeasonNumber, EpisodeNumber = owner.Target.EpisodeNumber, AbsoluteNumber = owner.Target.AbsoluteNumber,
                        IssueLabel = owner.Target.IssueLabel,
                        RemoteFileId = file.RemoteFileId, LocalPath = file.LocalPath, SizeBytes = file.SizeBytes, WrittenAt = file.WrittenAt, IsAvailable = true });
                }
            } else {
                foreach (var change in changes) await ApplyChangeAsync(row.Id, change, leaseToken);
            }
            var retainedTargets = JsonSerializer.Deserialize<ManagedTargetBinding[]>(row.TargetsJson, Json)!
                .OrderBy(binding => binding.Target.RemoteTargetId, StringComparer.Ordinal)
                .ToArray();
            var persistedAssociations = await db.ManagedSourceBindings.AsNoTracking()
                .Where(binding => binding.HoldingId == row.Id)
                .ToArrayAsync(leaseToken);
            var persistedAssociationIds = persistedAssociations.Select(binding => binding.Id).ToHashSet();
            var associatedTargets = persistedAssociations
                .Concat(db.ManagedSourceBindings.Local.Where(binding => binding.HoldingId == row.Id
                    && !persistedAssociationIds.Contains(binding.Id)))
                .Select(binding => new ManagedTargetBinding(
                    new(binding.RemoteTargetId, binding.Kind, binding.SeasonNumber,
                        binding.EpisodeNumber, binding.AbsoluteNumber, binding.IssueLabel),
                    binding.EntityId))
                .OrderBy(binding => binding.Target.RemoteTargetId, StringComparer.Ordinal)
                .ToArray();
            if (retainedTargets.Select(binding => binding.Target.RemoteTargetId).Distinct(StringComparer.Ordinal).Count() != retainedTargets.Length
                || associatedTargets.Select(binding => binding.Target.RemoteTargetId).Distinct(StringComparer.Ordinal).Count() != associatedTargets.Length
                || associatedTargets.Any(binding => !retainedTargets.Contains(binding)))
                throw new ArgumentException("The holding's source associations no longer match its retained target identities.");
            row.Revision++;
            row.Status = associatedTargets.Length == retainedTargets.Length
                ? ManagedTrackingStatus.Tracking
                : ManagedTrackingStatus.WaitingForFiles;
            row.Problem = null;
            row.LastCheckedAt = DateTimeOffset.UtcNow; row.NextCheckAt = row.LastCheckedAt.Value.Add(TrackingInterval);
            await db.SaveChangesAsync(leaseToken);
        }, token)) throw new EntityLifecycleMutationConflictException(ids.FirstOrDefault());
        if (restoredEntityIds.Length > 0) {
            await db.Entities.Where(entity => restoredEntityIds.Contains(entity.Id) && entity.IsLibraryArchived)
                .ExecuteUpdateAsync(set => set.SetProperty(entity => entity.IsLibraryArchived, false), token);
        }
        await transaction.CommitAsync(token);
    }

    /// <inheritdoc />
    public async Task ConfirmRemovalAsync(ManagedTrackingWork work, string problem, CancellationToken token) {
        var targetIds = work.Tracking.Targets.Select(target => target.EntityId).Distinct().ToArray();
        if (targetIds.Length == 0) throw new ArgumentException("The retained holding has no target identities.");
        var reservationIds = await new ManagedReservationScopeResolver(db).ResolveAsync(targetIds, work.Tracking.Item, token);
        var requestEntityId = await db.ManagedRequests.AsNoTracking()
            .Where(request => request.Id == work.Tracking.Id)
            .Select(request => (Guid?)request.EntityId)
            .SingleOrDefaultAsync(token);
        var lifecycleIds = targetIds.Concat(reservationIds)
            .Concat(requestEntityId is { } rootId ? [rootId] : []).Distinct().ToArray();
        var safeProblem = problem[..Math.Min(4096, problem.Length)];
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (!await lifecycle.ExecuteManyAsync(lifecycleIds, async leaseToken => {
            if (!reservationIds.ToHashSet().SetEquals(
                    await new ManagedReservationScopeResolver(db).ResolveAsync(targetIds, work.Tracking.Item, leaseToken)))
                throw new ArgumentException("The selected book work changed during removal review.");
            var requestRow = await db.ManagedRequests
                .FromSqlInterpolated($"SELECT * FROM managed_requests WHERE id = {work.Tracking.Id} FOR UPDATE")
                .AsNoTracking()
                .SingleOrDefaultAsync(leaseToken);
            var row = await RequireRevisionAsync(work.Tracking.Id, work.Tracking.Revision, leaseToken);
            var now = DateTimeOffset.UtcNow;

            if (requestRow is not null) {
                var requestState = JsonSerializer.Deserialize<ManagedRequestState>(requestRow.StateJson, Json)
                    ?? throw new InvalidDataException("Invalid managed request state.");
                if (requestState.Revision != requestRow.Revision || requestState.Phase != requestRow.Phase
                    || requestState.OperationId != row.Id || requestState.ConnectionId != row.ConnectionId
                    || requestState.RemoteId != row.RemoteId)
                    throw new ConnectionConflictException();
                var operation = new ManagedRequestOperation(requestState);
                operation.ConfirmRemoteRemoval();
                var stateJson = JsonSerializer.Serialize(operation.State, Json);
                if (await db.ManagedRequests.Where(request => request.Id == requestRow.Id && request.Revision == requestRow.Revision)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(request => request.StateJson, stateJson)
                        .SetProperty(request => request.Phase, operation.State.Phase)
                        .SetProperty(request => request.Revision, operation.State.Revision)
                        .SetProperty(request => request.UpdatedAt, now)
                        .SetProperty(request => request.NextCheckAt, now.Add(TrackingInterval))
                        .SetProperty(request => request.Problem, safeProblem), leaseToken) != 1)
                    throw new ConnectionConflictException();

                var relatedRows = await db.ManagedRequests
                    .FromSqlInterpolated($"SELECT * FROM managed_requests WHERE connection_id = {row.ConnectionId} AND entity_id = {requestRow.EntityId} AND id <> {row.Id} FOR UPDATE")
                    .AsNoTracking().ToArrayAsync(leaseToken);
                foreach (var related in relatedRows.OrderBy(request => request.Id)) {
                    var plan = JsonSerializer.Deserialize<ManagedRequestPlan>(related.PlanJson, Json);
                    if (plan?.ExistingHoldingId != row.Id) continue;
                    var relatedState = JsonSerializer.Deserialize<ManagedRequestState>(related.StateJson, Json)
                        ?? throw new InvalidDataException("Invalid managed request state.");
                    if (relatedState.Revision != related.Revision || relatedState.Phase != related.Phase)
                        throw new ConnectionConflictException();
                    var relatedOperation = new ManagedRequestOperation(relatedState);
                    if (ManagedRequestPhaseDefinition.For(relatedState.Phase).HoldsRemoteIdentity)
                        relatedOperation.ConfirmRemoteRemoval();
                    else if (relatedOperation.IsActive)
                        relatedOperation.RequireReview();
                    else continue;
                    var relatedJson = JsonSerializer.Serialize(relatedOperation.State, Json);
                    if (await db.ManagedRequests.Where(request => request.Id == related.Id && request.Revision == related.Revision)
                        .ExecuteUpdateAsync(set => set
                            .SetProperty(request => request.StateJson, relatedJson)
                            .SetProperty(request => request.Phase, relatedOperation.State.Phase)
                            .SetProperty(request => request.Revision, relatedOperation.State.Revision)
                            .SetProperty(request => request.UpdatedAt, now)
                            .SetProperty(request => request.NextCheckAt,
                                relatedOperation.IsActive && !relatedOperation.State.ReviewRequired
                                    ? now.Add(TrackingInterval)
                                    : (DateTimeOffset?)null)
                            .SetProperty(request => request.Problem, safeProblem), leaseToken) != 1)
                        throw new ConnectionConflictException();
                }
            }

            var bindings = await db.ManagedSourceBindings
                .Where(binding => binding.HoldingId == row.Id)
                .ToArrayAsync(leaseToken);
            var sourceIds = bindings.Select(binding => binding.SourceFileId).Distinct().ToArray();
            var sources = await db.EntityFiles.Where(source => sourceIds.Contains(source.Id)).ToArrayAsync(leaseToken);
            var restored = new HashSet<Guid>();
            foreach (var binding in bindings) {
                var readable = StillReadable(binding);
                binding.IsAvailable = readable;
                var source = sources.SingleOrDefault(file => file.Id == binding.SourceFileId && file.EntityId == binding.EntityId);
                if (source is null || source.Role is not (EntityFileRole.Source or EntityFileRole.UnavailableSource))
                    throw new ArgumentException("An established local source changed ownership. Review this association.");
                if (readable && source.Role == EntityFileRole.UnavailableSource) restored.Add(source.EntityId);
                source.Role = readable ? EntityFileRole.Source : EntityFileRole.UnavailableSource;
            }

            var owningStatuses = AcquisitionStatusDefinition.OwningFulfillment;
            var entities = await db.Entities.Where(entity => lifecycleIds.Contains(entity.Id)).ToArrayAsync(leaseToken);
            var archiveTargetIds = new HashSet<Guid>();
            foreach (var entity in entities.Where(entity => targetIds.Contains(entity.Id))) {
                var hasPlayableSource = sources.Any(file => file.EntityId == entity.Id && file.Role == EntityFileRole.Source)
                    || await db.EntityFiles.AnyAsync(file => file.EntityId == entity.Id
                        && file.Role == EntityFileRole.Source && !sourceIds.Contains(file.Id), leaseToken);
                var ownerEntityId = work.Tracking.Item.EntityKind == EntityKind.Book ? reservationIds[0] : entity.Id;
                var hasCurrentOwner = await db.FulfillmentReservations.AnyAsync(owner => owner.EntityId == ownerEntityId
                    && owner.OwnerId == row.Id && owner.BookRendition == work.Tracking.Item.BookRendition
                    && owner.ReleasedAt == null, leaseToken);
                var hasOtherOwner = await db.FulfillmentReservations.AnyAsync(owner => owner.EntityId == entity.Id
                    && owner.OwnerId != row.Id && owner.ReleasedAt == null, leaseToken);
                var hasNativeOwner = await db.Monitors.AnyAsync(monitor => monitor.EntityId == entity.Id, leaseToken)
                    || await db.Acquisitions.AnyAsync(acquisition => acquisition.EntityId == entity.Id
                        && owningStatuses.Contains(acquisition.Status), leaseToken);
                if (hasPlayableSource) {
                    entity.IsLibraryArchived = false;
                    entity.UpdatedAt = now;
                    continue;
                }
                if (!hasCurrentOwner || hasOtherOwner || hasNativeOwner) continue;
                entity.IsWanted = false;
                entity.IsLibraryArchived = true;
                entity.UpdatedAt = now;
                archiveTargetIds.Add(entity.Id);
            }
            var parentRootId = requestEntityId ?? (work.Tracking.Item.BookRendition == BookRendition.Audiobook
                ? reservationIds[0] : (Guid?)null);
            if (parentRootId is { } requestRootId && !targetIds.Contains(requestRootId)) {
                var root = entities.Single(entity => entity.Id == requestRootId);
                if (archiveTargetIds.Count == targetIds.Length
                    && !await HasRetainedLibraryPresenceAsync(requestRootId, row.Id, sourceIds, sources, owningStatuses, leaseToken)) {
                    root.IsWanted = false;
                    root.IsLibraryArchived = true;
                    root.UpdatedAt = now;
                } else if (await HasPlayableSourceInTreeAsync(requestRootId, sourceIds, sources, leaseToken)) {
                    root.IsLibraryArchived = false;
                    root.UpdatedAt = now;
                }
            }

            row.Revision++;
            row.Status = ManagedTrackingStatus.Removed;
            row.Problem = safeProblem;
            row.LastCheckedAt = now;
            row.NextCheckAt = now.Add(TrackingInterval);
            await db.SaveChangesAsync(leaseToken);
            foreach (var entity in entities.Where(entity => restored.Contains(entity.Id)))
                await queue.EnqueueAsync(EnqueueJobRequest.ForEntity(JobType.RefreshEntity,
                    EntityKindRegistry.Require(entity.KindCode), entity.Id.ToString(), entity.Title), leaseToken);
        }, token)) throw new EntityLifecycleMutationConflictException(lifecycleIds[0]);
        await transaction.CommitAsync(token);
    }

    private async Task<bool> HasRetainedLibraryPresenceAsync(
        Guid rootEntityId,
        Guid removedOwnerId,
        IReadOnlyCollection<Guid> reconciledSourceIds,
        IReadOnlyCollection<EntityFileRow> reconciledSources,
        IReadOnlyCollection<AcquisitionStatus> owningStatuses,
        CancellationToken token) {
        var treeIds = new HashSet<Guid> { rootEntityId };
        var frontier = new[] { rootEntityId };
        while (frontier.Length > 0) {
            var children = await db.Entities.AsNoTracking()
                .Where(entity => entity.ParentEntityId != null && frontier.Contains(entity.ParentEntityId.Value))
                .Select(entity => entity.Id)
                .ToArrayAsync(token);
            frontier = children.Where(treeIds.Add).ToArray();
        }

        var entityIds = treeIds.ToArray();
        if (await HasPlayableSourceAsync(entityIds, reconciledSourceIds, reconciledSources, token)) {
            return true;
        }

        if (await db.FulfillmentReservations.AsNoTracking().AnyAsync(owner => entityIds.Contains(owner.EntityId)
            && owner.OwnerId != removedOwnerId && owner.ReleasedAt == null, token)) {
            return true;
        }

        return await db.Monitors.AsNoTracking().AnyAsync(monitor =>
                monitor.EntityId != null && entityIds.Contains(monitor.EntityId.Value)
                || monitor.AcquisitionId != null && db.Acquisitions.Any(acquisition => acquisition.Id == monitor.AcquisitionId
                    && acquisition.EntityId != null && entityIds.Contains(acquisition.EntityId.Value)), token)
            || await db.Acquisitions.AsNoTracking().AnyAsync(acquisition => acquisition.EntityId != null
                && entityIds.Contains(acquisition.EntityId.Value)
                && owningStatuses.Contains(acquisition.Status), token);
    }

    private async Task<bool> HasPlayableSourceInTreeAsync(
        Guid rootEntityId,
        IReadOnlyCollection<Guid> reconciledSourceIds,
        IReadOnlyCollection<EntityFileRow> reconciledSources,
        CancellationToken token) {
        var treeIds = new HashSet<Guid> { rootEntityId };
        var frontier = new[] { rootEntityId };
        while (frontier.Length > 0) {
            var children = await db.Entities.AsNoTracking()
                .Where(entity => entity.ParentEntityId != null && frontier.Contains(entity.ParentEntityId.Value))
                .Select(entity => entity.Id)
                .ToArrayAsync(token);
            frontier = children.Where(treeIds.Add).ToArray();
        }
        return await HasPlayableSourceAsync(treeIds.ToArray(), reconciledSourceIds, reconciledSources, token);
    }

    private async Task<bool> HasPlayableSourceAsync(
        IReadOnlyCollection<Guid> entityIds,
        IReadOnlyCollection<Guid> reconciledSourceIds,
        IReadOnlyCollection<EntityFileRow> reconciledSources,
        CancellationToken token) {
        var ids = entityIds.ToHashSet();
        return reconciledSources.Any(source => ids.Contains(source.EntityId) && source.Role == EntityFileRole.Source)
            || await db.EntityFiles.AsNoTracking().AnyAsync(source => entityIds.Contains(source.EntityId)
                && source.Role == EntityFileRole.Source && !reconciledSourceIds.Contains(source.Id), token);
    }

    private async Task ApplyChangeAsync(Guid holdingId, ManagedSourceChange change, CancellationToken token) {
        var fileIds = change.Previous.Entities.Select(owner => owner.SourceFileId).ToArray();
        var ownerIds = change.Previous.Entities.Select(owner => owner.EntityId).ToArray();
        var sources = await db.EntityFiles.Where(file => fileIds.Contains(file.Id)).ToArrayAsync(token);
        if (sources.Length != fileIds.Length || sources.Any(source => source.Path != change.Previous.LocalPath
            || !change.Previous.Entities.Any(owner => owner.SourceFileId == source.Id && owner.EntityId == source.EntityId)
            || source.Role is not (EntityFileRole.Source or EntityFileRole.UnavailableSource)))
            throw new ArgumentException("An established local source changed ownership. Review this association.");
        var paths = new[] { change.Previous.LocalPath, change.Current?.LocalPath ?? change.Previous.LocalPath };
        var otherOwners = await db.EntityFiles.AnyAsync(file => paths.Contains(file.Path)
            && (file.Role == EntityFileRole.Source || file.Role == EntityFileRole.UnavailableSource) && !fileIds.Contains(file.Id), token);
        if (otherOwners) throw new ArgumentException("A replacement source already belongs to another local entity.");
        // Temporarily restore the source role inside this transaction so the existing source-generation
        // boundary invalidates technical data and generated assets for every retained owner together.
        foreach (var source in sources) source.Role = EntityFileRole.Source;
        await db.SaveChangesAsync(token);
        var comic = change.Previous.Entities.All(owner => owner.Target.Kind == EntityKind.ComicInstallment);
        if (!comic && change.Previous.Entities.Any(owner => owner.Target.Kind == EntityKind.ComicInstallment))
            throw new ArgumentException("A comic source shares bytes with another media kind. Review its ownership before rebinding.");
        var book = change.Previous.Entities.All(owner => owner.Target.Kind is EntityKind.Book or EntityKind.AudioTrack);
        if (!book && change.Previous.Entities.Any(owner => owner.Target.Kind is EntityKind.Book or EntityKind.AudioTrack))
            throw new ArgumentException("A book source shares bytes with another media kind. Review its ownership before rebinding.");
        var replacementPath = change.Current?.LocalPath ?? change.Previous.LocalPath;
        var changed = comic
            ? await videos.RebindConnectedComicSourceAsync(change.Previous.LocalPath, replacementPath, token)
            : book
            ? await videos.RebindConnectedBookSourceAsync(change.Previous.LocalPath, replacementPath, token)
            : await videos.RebindPlayableVideoSourceAsync(change.Previous.LocalPath, replacementPath, token);
        if (!changed.ToHashSet().SetEquals(ownerIds)) throw new ArgumentException("The established source coverage changed during reconciliation.");
        var bindings = await db.ManagedSourceBindings.Where(binding => binding.HoldingId == holdingId && fileIds.Contains(binding.SourceFileId)).ToArrayAsync(token);
        foreach (var binding in bindings) {
            binding.IsAvailable = change.Current is not null;
            if (change.Current is { } current) {
                binding.RemoteFileId = current.RemoteFileId; binding.LocalPath = current.LocalPath;
                binding.SizeBytes = current.SizeBytes; binding.WrittenAt = current.WrittenAt;
            }
        }
        if (change.Current is null) foreach (var source in sources) source.Role = EntityFileRole.UnavailableSource;
        await db.SaveChangesAsync(token);
        if (change.Current is not null) foreach (var owner in change.Previous.Entities)
            await queue.EnqueueAsync(EnqueueJobRequest.ForEntity(JobType.RefreshEntity, owner.Target.Kind, owner.EntityId.ToString(), null), token);
    }

    /// <inheritdoc />
    public async Task RecordProblemAsync(Guid id, long revision, ManagedTrackingStatus status, string problem, CancellationToken token) {
        // Rollback of a failed mutation must not leave tracked writes that a status save can accidentally commit.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var bindings = await db.ManagedSourceBindings.Where(binding => binding.HoldingId == id).ToArrayAsync(token);
        var ownerIds = bindings.Select(binding => binding.EntityId).Distinct().ToArray();
        if (ownerIds.Length == 0) {
            var targetJson = await db.ManagedHoldings.AsNoTracking().Where(row => row.Id == id)
                .Select(row => row.TargetsJson).SingleAsync(token);
            ownerIds = (JsonSerializer.Deserialize<ManagedTargetBinding[]>(targetJson, Json)
                ?? throw new InvalidDataException("Invalid managed target bindings."))
                .Select(target => target.EntityId).Distinct().ToArray();
        }
        var itemJson = await db.ManagedHoldings.AsNoTracking().Where(row => row.Id == id)
            .Select(row => row.ItemJson).SingleAsync(token);
        var item = JsonSerializer.Deserialize<ManagedItemInput>(itemJson, Json)
            ?? throw new InvalidDataException("Invalid managed holding identity.");
        var reservationIds = ownerIds.Length == 0 ? [] : await new ManagedReservationScopeResolver(db).ResolveAsync(ownerIds, item, token);
        var lifecycleIds = ownerIds.Concat(reservationIds).Distinct().ToArray();
        if (!await lifecycle.ExecuteManyAsync(lifecycleIds, async leaseToken => {
            if (ownerIds.Length > 0 && !reservationIds.ToHashSet().SetEquals(
                    await new ManagedReservationScopeResolver(db).ResolveAsync(ownerIds, item, leaseToken)))
                throw new ArgumentException("The selected book work changed during tracking review.");
            var row = await db.ManagedHoldings.SingleAsync(row => row.Id == id, leaseToken);
            if (row.Revision != revision) return;
            if (bindings.Length > 0) {
                var fileIds = bindings.Select(binding => binding.SourceFileId).ToArray();
                var sources = await db.EntityFiles.Where(file => fileIds.Contains(file.Id)).ToArrayAsync(leaseToken);
                foreach (var binding in bindings) {
                    if (status != ManagedTrackingStatus.NeedsReview && StillReadable(binding)) continue;
                    binding.IsAvailable = false;
                    var source = sources.SingleOrDefault(file => file.Id == binding.SourceFileId && file.EntityId == binding.EntityId);
                    if (source?.Role == EntityFileRole.Source) source.Role = EntityFileRole.UnavailableSource;
                }
            }
            row.Revision++; row.Status = status; row.Problem = problem[..Math.Min(4096, problem.Length)];
            row.LastCheckedAt = DateTimeOffset.UtcNow; row.NextCheckAt = row.LastCheckedAt.Value.Add(TrackingInterval);
            await db.SaveChangesAsync(leaseToken);
        }, token)) throw new EntityLifecycleMutationConflictException(lifecycleIds.FirstOrDefault());
        await transaction.CommitAsync(token);
    }

    /// <inheritdoc />
    public async Task RecordReappearanceAsync(Guid id, long revision, string problem, CancellationToken token) {
        var safeProblem = problem[..Math.Min(4096, problem.Length)];
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var bindings = await db.ManagedSourceBindings.AsNoTracking()
            .Where(binding => binding.HoldingId == id)
            .ToArrayAsync(token);
        var targetIds = await db.ManagedHoldings.AsNoTracking().Where(row => row.Id == id)
            .Select(row => row.TargetsJson).SingleAsync(token);
        var retainedTargets = JsonSerializer.Deserialize<ManagedTargetBinding[]>(targetIds, Json)
            ?? throw new InvalidDataException("Invalid managed target bindings.");
        var ownerIds = retainedTargets.Select(target => target.EntityId)
            .Concat(bindings.Select(binding => binding.EntityId)).Distinct().ToArray();
        var itemJson = await db.ManagedHoldings.AsNoTracking().Where(row => row.Id == id)
            .Select(row => row.ItemJson).SingleAsync(token);
        var item = JsonSerializer.Deserialize<ManagedItemInput>(itemJson, Json)
            ?? throw new InvalidDataException("Invalid managed holding identity.");
        var reservationIds = ownerIds.Length == 0 ? [] : await new ManagedReservationScopeResolver(db).ResolveAsync(ownerIds, item, token);
        var lifecycleIds = ownerIds.Concat(reservationIds).Distinct().ToArray();
        if (!await lifecycle.ExecuteManyAsync(lifecycleIds, async leaseToken => {
            if (ownerIds.Length > 0 && !reservationIds.ToHashSet().SetEquals(
                    await new ManagedReservationScopeResolver(db).ResolveAsync(ownerIds, item, leaseToken)))
                throw new ArgumentException("The selected book work changed during reappearance review.");
            var row = await db.ManagedHoldings.SingleAsync(row => row.Id == id, leaseToken);
            if (row.Revision != revision) return;
            row.Revision++;
            // A reappearing remote ID is evidence for review, not evidence that the prior confirmed
            // removal has been reversed. Keep the terminal marker so release remains available and
            // no later refresh can silently re-adopt files or manager controls.
            row.Status = ManagedTrackingStatus.Removed;
            row.Problem = safeProblem;
            row.LastCheckedAt = DateTimeOffset.UtcNow;
            row.NextCheckAt = row.LastCheckedAt.Value.Add(TrackingInterval);
            await db.SaveChangesAsync(leaseToken);
        }, token)) throw new EntityLifecycleMutationConflictException(lifecycleIds.FirstOrDefault());
        await transaction.CommitAsync(token);
    }

    private async Task<ManagedHoldingRow> RequireRevisionAsync(Guid id, long revision, CancellationToken token) {
        var row = await db.ManagedHoldings.SingleAsync(row => row.Id == id, token);
        if (row.Revision != revision) throw new ConnectionConflictException();
        return row;
    }

    private static bool StillReadable(ManagedSourceBindingRow binding) {
        try {
            VerifyUnchangedBytes(new(binding.RemoteFileId, binding.LocalPath, binding.SizeBytes, binding.WrittenAt, true, []));
            return true;
        } catch (ArgumentException) { return false; }
    }

    private async Task RequireUnownedAsync(Guid[] ids, EntityKind holdingKind, BookRendition? rendition,
        IReadOnlyDictionary<string, string> expectedIdentities, CancellationToken token) {
        if (await db.ManagedSourceBindings.AnyAsync(binding => ids.Contains(binding.EntityId), token))
            throw new ArgumentException("One of these local entities is already linked to a connected holding.");
        var all = ids.ToHashSet(); var frontier = ids;
        while (frontier.Length > 0) {
            var parents = await db.Entities.Where(entity => frontier.Contains(entity.Id) && entity.ParentEntityId != null).Select(entity => entity.ParentEntityId!.Value).ToArrayAsync(token);
            frontier = parents.Where(all.Add).ToArray();
        }
        var scope = all.ToArray();
        var owning = AcquisitionStatusDefinition.OwningFulfillment;
        // A series and its episodes can use the same namespace with different IDs. Compare the
        // holding's pinned identity only to local entities representing that same kind of work.
        var holdingKindCode = holdingKind.ToCode();
        var localIds = await db.EntityExternalIds.AsNoTracking().Where(identity => scope.Contains(identity.EntityId)
            && db.Entities.Any(entity => entity.Id == identity.EntityId && entity.KindCode == holdingKindCode)).ToArrayAsync(token);
        if (localIds.Any(identity => expectedIdentities.TryGetValue(identity.Provider, out var expected) && identity.Value != expected))
            throw new ArgumentException("The selected local scope has conflicting provider identities. Review its metadata before linking.");
        if (holdingKind == EntityKind.Book) {
            if (rendition is null || await db.Monitors.AnyAsync(monitor => monitor.EntityId != null
                    && scope.Contains(monitor.EntityId.Value) && monitor.Kind == EntityKind.Book
                    && (monitor.BookRendition ?? BookRendition.Ebook) == rendition, token)
                || await db.Acquisitions.AnyAsync(acquisition => acquisition.EntityId != null
                    && scope.Contains(acquisition.EntityId.Value) && acquisition.Kind == EntityKind.Book
                    && (acquisition.BookRendition ?? BookRendition.Ebook) == rendition
                    && owning.Contains(acquisition.Status), token))
                throw new ArgumentException("This book rendition has a native acquisition owner. Resolve it before linking a manager.");
            return;
        }
        if (await db.Monitors.AnyAsync(monitor => monitor.EntityId != null && scope.Contains(monitor.EntityId.Value), token)
            || await db.Acquisitions.AnyAsync(acquisition => acquisition.EntityId != null && scope.Contains(acquisition.EntityId.Value)
                && owning.Contains(acquisition.Status), token))
            throw new ArgumentException("This scope has a native monitoring or acquisition owner. Resolve its ownership before linking a manager.");
    }
}
