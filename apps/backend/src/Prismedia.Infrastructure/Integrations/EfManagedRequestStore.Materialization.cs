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
    public Task AcceptHoldingAsync(
        StoredManagedRequest work,
        ManagedItemSnapshot snapshot,
        CancellationToken token) =>
        AcceptHoldingAsync(work, snapshot, resolvedTargets: null, token);

    /// <inheritdoc />
    public async Task AcceptHoldingAsync(StoredManagedRequest work, ManagedItemSnapshot snapshot,
        IReadOnlyList<ManagedResolvedTarget>? resolvedTargets, CancellationToken token) {
        ManagedCreationEvidence.ValidateHolding(work.Plan.Creation.Work, snapshot);
        ManagedCreationEvidence.ValidateTargets(work.Plan.Creation.Work, resolvedTargets);
        ManagedCreationEvidence.ValidateComicTargets(work.Plan.Creation.Work, snapshot, resolvedTargets);
        var state = work.Operation.State;
        if (snapshot.Item.ProfileId != work.Plan.Creation.ProfileId)
            throw new ArgumentException("The existing or created holding has a different profile. Review it before delegating fulfillment.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var lifecycleIds = (work.Plan.Request.TargetEntityIds ?? []).Append(state.EntityId).ToArray();
        if (!await lifecycle.ExecuteManyAsync(lifecycleIds, async ct => {
            var current = await LockAsync(state.OperationId, state.Revision, ct);
            var target = await RequireBoundaryAsync(current.Operation, current.Plan, true, ct);
            if (ExternalLibraryPaths.Resolve(target.Mount.RemotePath, target.Mount.LocalPath, snapshot.Path) is null)
                throw new ArgumentException("The manager holding is outside this request's mapped root. Existing holdings are never moved implicitly.");
            var operation = new ManagedRequestOperation(current.Operation.State);
            operation.AcceptHolding(snapshot.Item.RemoteId);
            var item = new ManagedItemInput(snapshot.Item.EntityKind, snapshot.Item.RemoteId, current.Plan.Creation.Work.ExternalIds);
            var bindings = ResolveBindings(target, snapshot, resolvedTargets);
            var now = DateTimeOffset.UtcNow;
            if (current.Plan.ExistingHoldingId is { } existingHoldingId) {
                var holding = (await db.ManagedHoldings
                    .FromSqlInterpolated($"SELECT * FROM managed_holdings WHERE id = {existingHoldingId} FOR UPDATE")
                    .ToArrayAsync(ct)).SingleOrDefault()
                    ?? throw new ManagedRequestConflictException("The reviewed series holding no longer exists.");
                var retainedItem = JsonSerializer.Deserialize<ManagedItemInput>(holding.ItemJson, Json)!;
                var retained = JsonSerializer.Deserialize<ManagedTargetBinding[]>(holding.TargetsJson, Json)!;
                if (holding.ConnectionId != state.ConnectionId || holding.LibraryRootId != state.LibraryRootId
                    || holding.ReleasedAt is not null || holding.ReleaseOperationId is not null
                    || holding.Status is not (ManagedTrackingStatus.Tracking or ManagedTrackingStatus.WaitingForFiles)
                    || holding.RemoteId != snapshot.Item.RemoteId
                    || retainedItem.EntityKind != item.EntityKind
                    || item.ExpectedExternalIds.Any(pair => retainedItem.ExpectedExternalIds.GetValueOrDefault(pair.Key) != pair.Value)
                    || retained.Any(old => bindings.Any(added => old.Target.RemoteTargetId == added.Target.RemoteTargetId
                        || old.EntityId == added.EntityId)))
                    throw new ManagedRequestConflictException("The retained series target scope changed before expansion.");
                holding.TargetsJson = JsonSerializer.Serialize(retained.Concat(bindings)
                    .OrderBy(binding => binding.Target.RemoteTargetId, StringComparer.Ordinal), Json);
                holding.Status = ManagedTrackingStatus.WaitingForFiles;
                holding.Revision++;
                holding.LastCheckedAt = now;
                holding.NextCheckAt = now.AddMinutes(5);
                holding.Problem = null;
            } else {
                if (await db.ManagedHoldings.AnyAsync(holding => holding.Id == state.OperationId
                    || holding.ConnectionId == state.ConnectionId && holding.Kind == snapshot.Item.EntityKind && holding.RemoteId == snapshot.Item.RemoteId && holding.ReleasedAt == null, ct))
                    throw new ArgumentException("This remote holding is already associated with another local intent. Review the existing association.");
                db.ManagedHoldings.Add(new() { Id = state.OperationId, ConnectionId = state.ConnectionId, LibraryRootId = state.LibraryRootId,
                    Kind = item.EntityKind, RemoteId = item.RemoteId, Title = current.Plan.Title, ItemJson = JsonSerializer.Serialize(item, Json),
                    TargetsJson = JsonSerializer.Serialize(bindings, Json), Status = ManagedTrackingStatus.WaitingForFiles,
                    Revision = 1, LastCheckedAt = now, NextCheckAt = now.AddMinutes(5) });
            }
            await db.SaveChangesAsync(ct);
            await UpdateAsync(operation, state.Revision, null, ct);
        }, token)) throw new EntityLifecycleMutationConflictException(state.EntityId);
        await transaction.CommitAsync(token);
    }

    internal static IReadOnlyList<ManagedTargetBinding> ResolveBindings(
        ManagedRequestTarget target,
        ManagedItemSnapshot snapshot,
        IReadOnlyList<ManagedResolvedTarget>? resolvedTargets) {
        if (target.Targets is not { Count: > 0 }) {
            return [new(new(snapshot.Item.RemoteId, snapshot.Item.EntityKind, null, null, null), target.EntityId)];
        }

        var requested = target.Work.Targets ?? [];
        var resolved = resolvedTargets ?? [];
        var bindings = new List<ManagedTargetBinding>(target.Targets.Count);
        for (var index = 0; index < target.Targets.Count; index++) {
            var local = target.Targets[index];
            var request = requested[index];
            var remote = resolved.Single(candidate =>
                candidate.EntityKind == request.EntityKind
                && candidate.SeasonNumber == request.SeasonNumber
                && candidate.EpisodeNumber == request.EpisodeNumber
                && candidate.IssueLabel == request.IssueLabel
                && (candidate.AbsoluteNumber is null
                    || request.AbsoluteNumber is null
                    || candidate.AbsoluteNumber == request.AbsoluteNumber)
                && request.ExternalIds.All(pair => candidate.ExternalIds.GetValueOrDefault(pair.Key) == pair.Value));
            bindings.Add(new(
                new(remote.RemoteId, remote.EntityKind, remote.SeasonNumber, remote.EpisodeNumber, remote.AbsoluteNumber, remote.IssueLabel),
                local.EntityId));
        }
        return bindings;
    }

    /// <inheritdoc />
    public async Task<ManagedRequestMaterialization> MaterializeAsync(StoredManagedRequest work, ManagedItemSnapshot snapshot, CancellationToken token) {
        if (work.Plan.Creation.Work.EntityKind is EntityKind.VideoSeries or EntityKind.ComicSeries) {
            return await MaterializeEpisodesAsync(work, snapshot, token);
        }
        return await MaterializeMovieAsync(work, snapshot, token);
    }

    private async Task<ManagedRequestMaterialization> MaterializeMovieAsync(
        StoredManagedRequest work,
        ManagedItemSnapshot snapshot,
        CancellationToken token) {
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
            var expectedTarget = new ManagedTargetBinding(new(state.RemoteId!, snapshot.Item.EntityKind, null, null, null), state.EntityId);
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
            entity.IsWanted = false; entity.IsLibraryArchived = false; entity.UpdatedAt = now;
            if (await db.LibraryRoots.Where(root => root.Id == state.LibraryRootId).Select(root => root.IsNsfw).SingleAsync(ct)) entity.IsNsfw = true;
            if (!await db.EntityLibraryRoots.AnyAsync(root => root.EntityId == state.EntityId && root.LibraryRootId == state.LibraryRootId, ct))
                db.EntityLibraryRoots.Add(new() { EntityId = state.EntityId, LibraryRootId = state.LibraryRootId });
            db.EntityFiles.Add(new() { Id = sourceId, EntityId = state.EntityId, Role = EntityFileRole.Source, Path = path,
                SizeBytes = file.SizeBytes, CreatedAt = now, UpdatedAt = now });
            db.ManagedSourceBindings.Add(new() { Id = Guid.NewGuid(), HoldingId = holding.Id, RemoteTargetId = state.RemoteId!,
                Kind = expectedTarget.Target.Kind, EntityId = state.EntityId, SourceFileId = sourceId, RemoteFileId = file.RemoteId,
                LocalPath = path, SizeBytes = file.SizeBytes, WrittenAt = written, IsAvailable = true });
            holding.SelectionsJson = JsonSerializer.Serialize(new[] { new ManagedBindingSelection(state.RemoteId!, state.EntityId, sourceId) }, Json);
            holding.Status = ManagedTrackingStatus.Tracking; holding.Revision++; holding.LastCheckedAt = now; holding.NextCheckAt = now.AddMinutes(1); holding.Problem = null;
            var operation = new ManagedRequestOperation(current.Operation.State); operation.ConfirmFiles();
            await db.SaveChangesAsync(ct);
            await UpdateAsync(operation, state.Revision, null, ct);
            await queue.EnqueueAsync(EnqueueJobRequest.ForEntity(JobType.RefreshEntity, expectedTarget.Target.Kind, state.EntityId.ToString(), current.Plan.Title), ct);
        }, token)) throw new EntityLifecycleMutationConflictException(state.EntityId);
        await transaction.CommitAsync(token);
        return new(true);
    }

    private async Task<ManagedRequestMaterialization> MaterializeEpisodesAsync(
        StoredManagedRequest work,
        ManagedItemSnapshot snapshot,
        CancellationToken token) {
        ManagedCreationEvidence.ValidateHolding(work.Plan.Creation.Work, snapshot);
        var state = work.Operation.State;
        var isComic = work.Plan.Creation.Work.EntityKind == EntityKind.ComicSeries;
        var expectedKind = isComic ? EntityKind.ComicInstallment : EntityKind.VideoEpisode;
        if (state.Phase != ManagedRequestPhase.AwaitingFiles || snapshot.Item.RemoteId != state.RemoteId)
            throw new ArgumentException("The selected target file evidence does not belong to this accepted holding.");

        var holdingId = work.Plan.ExistingHoldingId ?? state.OperationId;
        var holdingRow = await db.ManagedHoldings.AsNoTracking()
            .SingleAsync(row => row.Id == holdingId, token);
        var union = JsonSerializer.Deserialize<ManagedTargetBinding[]>(holdingRow.TargetsJson, Json)
            ?? throw new InvalidDataException("Invalid managed target bindings.");
        var requestedEntityIds = (work.Plan.Request.TargetEntityIds ?? []).ToHashSet();
        var pinned = union.Where(binding => requestedEntityIds.Contains(binding.EntityId)).ToArray();
        if (pinned.Length == 0
            || pinned.Any(binding => binding.Target.Kind != expectedKind)
            || pinned.Select(binding => binding.Target.RemoteTargetId).Distinct(StringComparer.Ordinal).Count() != pinned.Length
            || pinned.Select(binding => binding.EntityId).Distinct().Count() != pinned.Length)
            throw new ArgumentException("The accepted targets are incomplete or ambiguous.");
        var pinnedByRemoteId = pinned.ToDictionary(
            binding => binding.Target.RemoteTargetId,
            StringComparer.Ordinal);

        var relevantFiles = new List<ManagedLibraryFile>();
        var observedTargetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in snapshot.Files) {
            var selected = file.Targets
                .Where(target => pinnedByRemoteId.ContainsKey(target.RemoteId))
                .ToArray();
            if (selected.Length == 0) continue;
            if (selected.Length != file.Targets.Count)
                throw new ArgumentException(
                    "A manager file mixes selected and unselected targets. Review its shared coverage before importing it.");
            foreach (var target in selected) {
                var expected = pinnedByRemoteId[target.RemoteId].Target;
                var observed = new ManagedTargetIdentity(
                    target.RemoteId,
                    target.EntityKind,
                    target.SeasonNumber,
                    target.EpisodeNumber,
                    target.AbsoluteNumber,
                    target.IssueLabel);
                if (observed != expected || !observedTargetIds.Add(target.RemoteId))
                    throw new ArgumentException(
                        "The manager returned changed or duplicate evidence for a selected target.");
            }
            relevantFiles.Add(file);
        }

        var existingBindings = await db.ManagedSourceBindings.AsNoTracking()
            .Where(binding => binding.HoldingId == holdingId)
            .ToArrayAsync(token);
        var fulfilledIds = existingBindings
            .Where(binding => pinnedByRemoteId.ContainsKey(binding.RemoteTargetId))
            .Select(binding => binding.RemoteTargetId)
            .ToHashSet(StringComparer.Ordinal);
        if (fulfilledIds.Count == pinned.Length)
            throw new ArgumentException("All selected targets were already materialized.");
        if (relevantFiles.Count == 0)
            return new(false, "Waiting for the manager to import selected target files.");

        var mappedFiles = await mounts.InspectAsync(state.ConnectionId, relevantFiles, token);
        if (mappedFiles.Count != relevantFiles.Count)
            throw new ArgumentException("The mapped target evidence is incomplete.");
        var candidates = new List<(ManagedLibraryFile Remote, MappedLibraryFile Mapped, string Path, DateTimeOffset Written)>();
        for (var index = 0; index < relevantFiles.Count; index++) {
            var remote = relevantFiles[index];
            var mapped = mappedFiles[index];
            if (!string.Equals(mapped.RemoteId, remote.RemoteId, StringComparison.Ordinal))
                throw new ArgumentException("The mapped target evidence changed order or identity.");
            if (remote.Targets.Any(target => fulfilledIds.Contains(target.RemoteId))) {
                if (remote.Targets.Any(target => !fulfilledIds.Contains(target.RemoteId)))
                    throw new ArgumentException(
                        "A manager file changed shared target coverage after partial materialization.");
                continue;
            }
            if (mapped.LibraryRootId != state.LibraryRootId || mapped.LocalPath is null)
                throw new ArgumentException("A selected target file is outside this request's mapped library.");
            if (!mapped.IsReadable || !mapped.SizeMatches) continue;
            if (!(isComic ? SupportedExtensions.ComicArchive : SupportedExtensions.Video).Contains(Path.GetExtension(mapped.LocalPath)))
                throw new ArgumentException("A selected target file has an unsupported format.");
            candidates.Add((remote, mapped, mapped.LocalPath, WrittenAt(mapped.LocalPath)));
        }
        if (candidates.Count == 0)
            return new(false, "The manager reports selected target files, but Prismedia cannot yet read their expected bytes.");

        var lifecycleIds = pinned.Select(binding => binding.EntityId).Append(state.EntityId).ToArray();
        var completed = false;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (!await lifecycle.ExecuteManyAsync(lifecycleIds, async ct => {
            var current = await LockAsync(state.OperationId, state.Revision, ct);
            var boundary = await RequireBoundaryAsync(current.Operation, current.Plan, true, ct);
            var holding = (await db.ManagedHoldings
                .FromSqlInterpolated($"SELECT * FROM managed_holdings WHERE id = {holdingId} FOR UPDATE")
                .ToArrayAsync(ct)).Single();
            var currentUnion = JsonSerializer.Deserialize<ManagedTargetBinding[]>(holding.TargetsJson, Json)!;
            var currentPinned = currentUnion.Where(binding => requestedEntityIds.Contains(binding.EntityId)).ToArray();
            if (holding.Status != ManagedTrackingStatus.WaitingForFiles
                || !currentPinned.OrderBy(binding => binding.Target.RemoteTargetId, StringComparer.Ordinal)
                    .SequenceEqual(pinned.OrderBy(binding => binding.Target.RemoteTargetId, StringComparer.Ordinal)))
                throw new ArgumentException("The accepted targets changed before file materialization.");

            var storedBindings = await db.ManagedSourceBindings
                .Where(binding => binding.HoldingId == holding.Id)
                .ToArrayAsync(ct);
            var storedTargetIds = storedBindings.Select(binding => binding.RemoteTargetId)
                .ToHashSet(StringComparer.Ordinal);
            var candidatePaths = candidates.Select(candidate => candidate.Path).Distinct(FileSystemPathComparison.Comparer).ToArray();
            var possiblePathOwners = await db.EntityFiles.AsNoTracking()
                .Where(source => source.Role == EntityFileRole.Source || source.Role == EntityFileRole.UnavailableSource)
                .Where(source => candidatePaths.Select(path => path.Length).Contains(source.Path.Length))
                .Select(source => source.Path)
                .ToArrayAsync(ct);
            if (possiblePathOwners.Any(existingPath => candidates.Any(candidate =>
                    FileSystemPathComparison.Equals(candidate.Path, existingPath))))
                throw new ArgumentException(
                    "A selected target source already belongs to another local item. Review its existing identity instead of duplicating it.");

            var now = DateTimeOffset.UtcNow;
            var importedEntityIds = new HashSet<Guid>();
            foreach (var candidate in candidates) {
                await using var bytes = new FileStream(
                    candidate.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4096,
                    true);
                if (bytes.Length != candidate.Remote.SizeBytes || WrittenAt(candidate.Path) != candidate.Written)
                    throw new ArgumentException(
                        "A selected target file changed during import verification. Refresh its evidence.");
                foreach (var remoteTarget in candidate.Remote.Targets) {
                    if (storedTargetIds.Contains(remoteTarget.RemoteId)) continue;
                    var target = pinnedByRemoteId[remoteTarget.RemoteId];
                    var sourceId = Guid.NewGuid();
                    var entity = await db.Entities.SingleAsync(row => row.Id == target.EntityId, ct);
                    entity.IsWanted = false;
                    entity.IsLibraryArchived = false;
                    entity.UpdatedAt = now;
                    if (await db.LibraryRoots.Where(root => root.Id == state.LibraryRootId)
                        .Select(root => root.IsNsfw).SingleAsync(ct)) entity.IsNsfw = true;
                    if (!await db.EntityLibraryRoots.AnyAsync(root =>
                        root.EntityId == target.EntityId && root.LibraryRootId == state.LibraryRootId, ct))
                        db.EntityLibraryRoots.Add(new() {
                            EntityId = target.EntityId,
                            LibraryRootId = state.LibraryRootId
                        });
                    db.EntityFiles.Add(new() {
                        Id = sourceId,
                        EntityId = target.EntityId,
                        Role = EntityFileRole.Source,
                        Path = candidate.Path,
                        SizeBytes = candidate.Remote.SizeBytes,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    db.ManagedSourceBindings.Add(new() {
                        Id = Guid.NewGuid(),
                        HoldingId = holding.Id,
                        RemoteTargetId = target.Target.RemoteTargetId,
                        Kind = target.Target.Kind,
                        SeasonNumber = target.Target.SeasonNumber,
                        EpisodeNumber = target.Target.EpisodeNumber,
                        AbsoluteNumber = target.Target.AbsoluteNumber,
                        IssueLabel = target.Target.IssueLabel,
                        EntityId = target.EntityId,
                        SourceFileId = sourceId,
                        RemoteFileId = candidate.Remote.RemoteId,
                        LocalPath = candidate.Path,
                        SizeBytes = candidate.Remote.SizeBytes,
                        WrittenAt = candidate.Written,
                        IsAvailable = true
                    });
                    storedTargetIds.Add(target.Target.RemoteTargetId);
                    importedEntityIds.Add(target.EntityId);
                }
            }

            completed = pinned.All(binding => storedTargetIds.Contains(binding.Target.RemoteTargetId));
            var allBindings = db.ManagedSourceBindings.Local
                .Where(binding => binding.HoldingId == holding.Id)
                .Concat(storedBindings)
                .DistinctBy(binding => binding.RemoteTargetId)
                .ToArray();
            holding.SelectionsJson = JsonSerializer.Serialize(allBindings.Select(binding =>
                new ManagedBindingSelection(binding.RemoteTargetId, binding.EntityId, binding.SourceFileId)), Json);
            var requestRoot = await db.Entities.SingleAsync(entity => entity.Id == state.EntityId, ct);
            requestRoot.IsLibraryArchived = false;
            holding.Status = completed && allBindings.Length == currentUnion.Length
                ? ManagedTrackingStatus.Tracking
                : ManagedTrackingStatus.WaitingForFiles;
            holding.Revision++;
            holding.LastCheckedAt = now;
            holding.NextCheckAt = now.AddMinutes(1);
            holding.Problem = null;
            if (completed) {
                var operation = new ManagedRequestOperation(current.Operation.State);
                operation.ConfirmFiles();
                await UpdateAsync(operation, state.Revision, null, ct);
            }
            await db.SaveChangesAsync(ct);
            foreach (var entityId in importedEntityIds) {
                await queue.EnqueueAsync(EnqueueJobRequest.ForEntity(
                    JobType.RefreshEntity,
                    expectedKind,
                    entityId.ToString(),
                    boundary.Title), ct);
            }
        }, token)) throw new EntityLifecycleMutationConflictException(state.EntityId);
        await transaction.CommitAsync(token);
        return completed
            ? new(true)
            : new(false, "Waiting for the remaining selected target files.");
    }

    private static DateTimeOffset WrittenAt(string path) {
        var ticks = File.GetLastWriteTimeUtc(path).Ticks;
        return new(ticks - ticks % 10, TimeSpan.Zero);
    }
}
