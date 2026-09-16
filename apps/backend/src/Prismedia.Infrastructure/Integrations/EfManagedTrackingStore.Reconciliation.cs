using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Integrations;

public sealed partial class EfManagedTrackingStore {
    /// <inheritdoc />
    public async Task ApplyAsync(ManagedTrackingWork work, ManagedTrackingObservation observation,
        IReadOnlyList<ManagedFileBinding>? adoption, IReadOnlyList<ManagedSourceChange> changes, CancellationToken token) {
        var ids = (adoption ?? work.Tracking.Bindings).SelectMany(file => file.Entities).Select(owner => owner.EntityId).Distinct().ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (!await lifecycle.ExecuteManyAsync(ids, async leaseToken => {
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
                await RequireUnownedAsync(ids, work.Tracking.Item.ExpectedExternalIds, leaseToken);
                foreach (var file in checkedPlan.Bindings) foreach (var owner in file.Entities) {
                    db.ManagedSourceBindings.Add(new() { Id = Guid.NewGuid(), HoldingId = row.Id, EntityId = owner.EntityId,
                        SourceFileId = owner.SourceFileId, RemoteTargetId = owner.Target.RemoteTargetId, Kind = owner.Target.Kind,
                        SeasonNumber = owner.Target.SeasonNumber, EpisodeNumber = owner.Target.EpisodeNumber, AbsoluteNumber = owner.Target.AbsoluteNumber,
                        RemoteFileId = file.RemoteFileId, LocalPath = file.LocalPath, SizeBytes = file.SizeBytes, WrittenAt = file.WrittenAt, IsAvailable = true });
                }
            } else {
                foreach (var change in changes) await ApplyChangeAsync(row.Id, change, leaseToken);
            }
            row.Revision++; row.Status = ManagedTrackingStatus.Tracking; row.Problem = null;
            row.LastCheckedAt = DateTimeOffset.UtcNow; row.NextCheckAt = row.LastCheckedAt.Value.AddMinutes(5);
            await db.SaveChangesAsync(leaseToken);
        }, token)) throw new EntityLifecycleMutationConflictException(ids.FirstOrDefault());
        await transaction.CommitAsync(token);
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
        var changed = await videos.RebindPlayableVideoSourceAsync(change.Previous.LocalPath,
            change.Current?.LocalPath ?? change.Previous.LocalPath, token);
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
        if (!await lifecycle.ExecuteManyAsync(bindings.Select(binding => binding.EntityId).ToArray(), async leaseToken => {
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
            row.LastCheckedAt = DateTimeOffset.UtcNow; row.NextCheckAt = row.LastCheckedAt.Value.AddMinutes(5);
            await db.SaveChangesAsync(leaseToken);
        }, token)) throw new EntityLifecycleMutationConflictException(bindings.First().EntityId);
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

    private async Task RequireUnownedAsync(Guid[] ids, IReadOnlyDictionary<string, string> expectedIdentities, CancellationToken token) {
        if (await db.ManagedSourceBindings.AnyAsync(binding => ids.Contains(binding.EntityId), token))
            throw new ArgumentException("One of these local entities is already linked to a connected holding.");
        var all = ids.ToHashSet(); var frontier = ids;
        while (frontier.Length > 0) {
            var parents = await db.Entities.Where(entity => frontier.Contains(entity.Id) && entity.ParentEntityId != null).Select(entity => entity.ParentEntityId!.Value).ToArrayAsync(token);
            frontier = parents.Where(all.Add).ToArray();
        }
        var scope = all.ToArray();
        var localIds = await db.EntityExternalIds.AsNoTracking().Where(identity => scope.Contains(identity.EntityId)).ToArrayAsync(token);
        if (localIds.Any(identity => expectedIdentities.TryGetValue(identity.Provider, out var expected) && identity.Value != expected))
            throw new ArgumentException("The selected local scope has conflicting provider identities. Review its metadata before linking.");
        if (await db.Monitors.AnyAsync(monitor => monitor.EntityId != null && scope.Contains(monitor.EntityId.Value), token)
            || await db.Acquisitions.AnyAsync(acquisition => acquisition.EntityId != null && scope.Contains(acquisition.EntityId.Value)
                && acquisition.Status != AcquisitionStatus.Imported && acquisition.Status != AcquisitionStatus.Cancelled && acquisition.Status != AcquisitionStatus.Failed, token))
            throw new ArgumentException("This scope has a native monitoring or acquisition owner. Resolve its ownership before linking a manager.");
    }
}
