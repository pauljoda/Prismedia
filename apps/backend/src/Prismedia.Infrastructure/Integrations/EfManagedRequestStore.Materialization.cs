using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Integrations;

public sealed partial class EfManagedRequestStore {
    /// <inheritdoc />
    public async Task ValidateHoldingAsync(StoredManagedRequest work, ManagedItemSnapshot snapshot, CancellationToken token) {
        ManagedCreationEvidence.ValidateHolding(work.Plan.Creation.Work, snapshot);
        if (snapshot.Item.RemoteId != work.Operation.State.RemoteId) throw new ArgumentException("The remote holding identity changed.");
        var target = await RequireBoundaryAsync(work.Operation, work.Plan, true, token);
        if (ExternalLibraryPaths.Resolve(target.Mount.RemotePath, target.Mount.LocalPath, snapshot.Path) is null)
            throw new ArgumentException("The holding moved outside the request's mapped library. Review its boundary before issuing controls.");
    }

    /// <inheritdoc />
    public async Task AcceptHoldingAsync(StoredManagedRequest work, ManagedItemSnapshot snapshot, CancellationToken token) {
        ManagedCreationEvidence.ValidateHolding(work.Plan.Creation.Work, snapshot);
        var state = work.Operation.State;
        if (snapshot.Item.ProfileId != work.Plan.Creation.ProfileId)
            throw new ArgumentException("The existing or created movie has a different profile. Review it before delegating fulfillment.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (!await lifecycle.ExecuteAsync(state.EntityId, async ct => {
            var current = await LockAsync(state.OperationId, state.Revision, ct);
            var target = await RequireBoundaryAsync(current.Operation, current.Plan, true, ct);
            if (ExternalLibraryPaths.Resolve(target.Mount.RemotePath, target.Mount.LocalPath, snapshot.Path) is null)
                throw new ArgumentException("The manager holding is outside this request's mapped root. Existing holdings are never moved implicitly.");
            if (await db.ManagedHoldings.AnyAsync(holding => holding.Id == state.OperationId
                || holding.ConnectionId == state.ConnectionId && holding.Kind == snapshot.Item.EntityKind && holding.RemoteId == snapshot.Item.RemoteId, ct))
                throw new ArgumentException("This remote holding is already associated with another local intent. Review the existing association.");
            var operation = new ManagedRequestOperation(current.Operation.State);
            operation.AcceptHolding(snapshot.Item.RemoteId);
            var item = new ManagedItemInput(snapshot.Item.EntityKind, snapshot.Item.RemoteId, current.Plan.Creation.Work.ExternalIds);
            var binding = new ManagedTargetBinding(new(snapshot.Item.RemoteId, EntityKind.Movie, null, null, null), state.EntityId);
            var now = DateTimeOffset.UtcNow;
            db.ManagedHoldings.Add(new() { Id = state.OperationId, ConnectionId = state.ConnectionId, LibraryRootId = state.LibraryRootId,
                Kind = item.EntityKind, RemoteId = item.RemoteId, Title = current.Plan.Title, ItemJson = JsonSerializer.Serialize(item, Json),
                TargetsJson = JsonSerializer.Serialize(new[] { binding }, Json), Status = ManagedTrackingStatus.WaitingForFiles,
                Revision = 1, LastCheckedAt = now, NextCheckAt = now.AddMinutes(5) });
            await db.SaveChangesAsync(ct);
            await UpdateAsync(operation, state.Revision, null, ct);
        }, token)) throw new EntityLifecycleMutationConflictException(state.EntityId);
        await transaction.CommitAsync(token);
    }

    /// <inheritdoc />
    public async Task<ManagedRequestMaterialization> MaterializeAsync(StoredManagedRequest work, ManagedItemSnapshot snapshot, CancellationToken token) {
        ManagedCreationEvidence.ValidateHolding(work.Plan.Creation.Work, snapshot);
        var state = work.Operation.State;
        if (state.Phase != ManagedRequestPhase.AwaitingFiles || snapshot.Item.RemoteId != state.RemoteId)
            throw new ArgumentException("The final file evidence does not belong to this accepted holding.");
        if (snapshot.Files.Count == 0) return new(false, "Waiting for the manager to import a final file.");
        if (snapshot.Files.Count != 1) throw new ArgumentException("A movie request requires one exact final file association.");
        var file = snapshot.Files[0];
        var mapped = (await mounts.InspectAsync(state.ConnectionId, snapshot.Files, token)).Single();
        if (mapped.LibraryRootId != state.LibraryRootId || mapped.LocalPath is null)
            throw new ArgumentException("The final file is outside this request's mapped library.");
        if (!mapped.IsReadable || !mapped.SizeMatches) return new(false, mapped.Problem ?? "The manager reports a file but Prismedia cannot yet read its expected bytes.");
        var path = mapped.LocalPath;
        if (!SupportedExtensions.Video.Contains(Path.GetExtension(path))) throw new ArgumentException("The manager's final file is not a supported video source.");
        var written = WrittenAt(path);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (!await lifecycle.ExecuteAsync(state.EntityId, async ct => {
            var current = await LockAsync(state.OperationId, state.Revision, ct);
            var target = await RequireBoundaryAsync(current.Operation, current.Plan, true, ct);
            var expectedPath = ExternalLibraryPaths.Resolve(target.Mount.RemotePath, target.Mount.LocalPath, file.Path);
            if (expectedPath is null || !FileSystemPathComparison.Equals(path, expectedPath))
                throw new ArgumentException("The final file's mapped path changed before import.");
            var holding = (await db.ManagedHoldings.FromSqlInterpolated($"SELECT * FROM managed_holdings WHERE id = {state.OperationId} FOR UPDATE").ToArrayAsync(ct)).Single();
            var pinned = JsonSerializer.Deserialize<ManagedTargetBinding[]>(holding.TargetsJson, Json)!;
            var expectedTarget = new ManagedTargetBinding(new(state.RemoteId!, EntityKind.Movie, null, null, null), state.EntityId);
            if (holding.Status != ManagedTrackingStatus.WaitingForFiles || pinned.Length != 1 || pinned[0] != expectedTarget
                || await db.ManagedSourceBindings.AnyAsync(binding => binding.HoldingId == holding.Id, ct))
                throw new ArgumentException("The accepted wanted target changed before file materialization.");
            var paths = await db.EntityFiles.AsNoTracking().Where(source => source.Role == EntityFileRole.Source
                || source.Role == EntityFileRole.UnavailableSource).Where(source => source.Path.Length == path.Length).Select(source => source.Path).ToArrayAsync(ct);
            if (paths.Any(other => FileSystemPathComparison.Equals(path, other)))
                throw new ArgumentException("The final source already belongs to another local item. Review its existing identity instead of duplicating it.");
            // Keep a read handle through the transaction and recheck timestamp and path evidence before
            // publishing availability. Later replacements continue through the established tracker.
            await using var bytes = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            if (bytes.Length != file.SizeBytes || WrittenAt(path) != written)
                throw new ArgumentException("The final file changed during import verification. Refresh its evidence.");
            var now = DateTimeOffset.UtcNow; var sourceId = Guid.NewGuid();
            var entity = await db.Entities.SingleAsync(row => row.Id == state.EntityId, ct);
            entity.IsWanted = false; entity.UpdatedAt = now;
            if (await db.LibraryRoots.Where(root => root.Id == state.LibraryRootId).Select(root => root.IsNsfw).SingleAsync(ct)) entity.IsNsfw = true;
            if (!await db.EntityLibraryRoots.AnyAsync(root => root.EntityId == state.EntityId && root.LibraryRootId == state.LibraryRootId, ct))
                db.EntityLibraryRoots.Add(new() { EntityId = state.EntityId, LibraryRootId = state.LibraryRootId });
            db.EntityFiles.Add(new() { Id = sourceId, EntityId = state.EntityId, Role = EntityFileRole.Source, Path = path,
                SizeBytes = file.SizeBytes, CreatedAt = now, UpdatedAt = now });
            db.ManagedSourceBindings.Add(new() { Id = Guid.NewGuid(), HoldingId = holding.Id, RemoteTargetId = state.RemoteId!,
                Kind = EntityKind.Movie, EntityId = state.EntityId, SourceFileId = sourceId, RemoteFileId = file.RemoteId,
                LocalPath = path, SizeBytes = file.SizeBytes, WrittenAt = written, IsAvailable = true });
            holding.SelectionsJson = JsonSerializer.Serialize(new[] { new ManagedBindingSelection(state.RemoteId!, state.EntityId, sourceId) }, Json);
            holding.Status = ManagedTrackingStatus.Tracking; holding.Revision++; holding.LastCheckedAt = now; holding.NextCheckAt = now.AddMinutes(5); holding.Problem = null;
            var operation = new ManagedRequestOperation(current.Operation.State); operation.ConfirmFiles();
            await db.SaveChangesAsync(ct);
            await UpdateAsync(operation, state.Revision, null, ct);
            await queue.EnqueueAsync(EnqueueJobRequest.ForEntity(JobType.RefreshEntity, EntityKind.Movie, state.EntityId.ToString(), current.Plan.Title), ct);
        }, token)) throw new EntityLifecycleMutationConflictException(state.EntityId);
        await transaction.CommitAsync(token);
        return new(true);
    }

    private static DateTimeOffset WrittenAt(string path) {
        var ticks = File.GetLastWriteTimeUtc(path).Ticks;
        return new(ticks - ticks % 10, TimeSpan.Zero);
    }
}
