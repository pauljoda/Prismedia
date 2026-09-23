using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Integrations;

public sealed partial class EfManagedRequestStore {
    private async Task<ManagedRequestMaterialization> MaterializeBookAsync(
        StoredManagedRequest work, ManagedItemSnapshot snapshot, CancellationToken token) {
        ManagedCreationEvidence.ValidateHolding(work.Plan.Creation.Work, snapshot);
        var state = work.Operation.State;
        var rendition = work.Plan.Creation.Work.BookRendition
            ?? throw new ArgumentException("The accepted Book request has no rendition.");
        if (state.Phase != ManagedRequestPhase.AwaitingFiles || snapshot.Item.RemoteId != state.RemoteId)
            throw new ArgumentException("The Book file evidence does not belong to this accepted request.");
        if (snapshot.Files.Count == 0) return new(false, "Waiting for the manager to import this Book rendition.");
        if (snapshot.Files is not [{ Targets: [{ } remoteTarget] }])
            throw new ArgumentException("A Book manager request requires one exact final file and target.");
        var file = snapshot.Files[0];
        var expectedKind = rendition == BookRendition.Ebook ? EntityKind.Book : EntityKind.AudioTrack;
        if (remoteTarget.EntityKind != expectedKind)
            throw new ArgumentException("The manager file belongs to another Book rendition.");
        var mapped = (await mounts.InspectAsync(state.ConnectionId, snapshot.Files, token)).Single();
        if (mapped.LibraryRootId != state.LibraryRootId || mapped.LocalPath is null)
            throw new ArgumentException("The Book file is outside this request's mapped library.");
        if (!mapped.IsReadable || !mapped.SizeMatches)
            return new(false, mapped.Problem ?? "The manager reports a Book file that Prismedia cannot yet read.");
        var path = mapped.LocalPath;
        var extension = Path.GetExtension(path);
        if (!(rendition == BookRendition.Ebook ? SupportedExtensions.Book : SupportedExtensions.Audiobook).Contains(extension))
            throw new ArgumentException("The manager's Book file has an unsupported rendition format.");
        var written = WrittenAt(path);
        var existingPaths = await db.EntityFiles.AsNoTracking()
            .Where(source => source.Role == EntityFileRole.Source || source.Role == EntityFileRole.UnavailableSource)
            .Where(source => source.Path.Length == path.Length)
            .Select(source => new { source.Id, source.EntityId, source.Path })
            .ToArrayAsync(token);
        var existingSources = existingPaths.Where(source => FileSystemPathComparison.Equals(path, source.Path)).ToArray();
        if (existingSources.Length > 1)
            throw new ArgumentException("Several local items claim this Book file. Review their associations.");
        var lifecycleIds = new List<Guid> { state.EntityId };
        if (existingSources is [{ } existingSource]) {
            lifecycleIds.Add(existingSource.EntityId);
            var parentId = await db.Entities.AsNoTracking().Where(row => row.Id == existingSource.EntityId)
                .Select(row => row.ParentEntityId).SingleOrDefaultAsync(token);
            if (parentId is { } parent) lifecycleIds.Add(parent);
        }
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (!await lifecycle.ExecuteManyAsync(lifecycleIds, async ct => {
            var current = await LockAsync(state.OperationId, state.Revision, ct);
            var boundary = await RequireBoundaryAsync(current.Operation, current.Plan, true, ct);
            var expectedPath = ExternalLibraryPaths.Resolve(boundary.Mount.RemotePath, boundary.Mount.LocalPath, file.Path);
            if (expectedPath is null || !FileSystemPathComparison.Equals(path, expectedPath))
                throw new ArgumentException("The Book file's mapped path changed before import.");
            var holding = (await db.ManagedHoldings
                .FromSqlInterpolated($"SELECT * FROM managed_holdings WHERE id = {state.OperationId} FOR UPDATE")
                .ToArrayAsync(ct)).Single();
            if (holding.Status != ManagedTrackingStatus.WaitingForFiles || holding.BookRendition != rendition
                || JsonSerializer.Deserialize<ManagedTargetBinding[]>(holding.TargetsJson, Json) is not { Length: 0 }
                || await db.ManagedSourceBindings.AnyAsync(binding => binding.HoldingId == holding.Id, ct))
                throw new ArgumentException("The Book request's retained file scope changed before materialization.");
            var library = await db.LibraryRoots.Where(root => root.Id == state.LibraryRootId)
                .Select(root => new { root.Path, root.IsNsfw }).SingleAsync(ct);
            if (rendition == BookRendition.Audiobook)
                RequireCompleteAudiobookFolder(path, library.Path, ct);
            await using var bytes = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            if (bytes.Length != file.SizeBytes || WrittenAt(path) != written)
                throw new ArgumentException("The Book file changed during import verification. Refresh its evidence.");

            var now = DateTimeOffset.UtcNow;
            var book = await db.Entities.SingleAsync(row => row.Id == state.EntityId, ct);
            book.IsWanted = false;
            book.IsLibraryArchived = false;
            book.UpdatedAt = now;
            if (library.IsNsfw) book.IsNsfw = true;
            var bookRoot = await db.EntityLibraryRoots.SingleOrDefaultAsync(row => row.EntityId == book.Id, ct);
            if (bookRoot is null)
                db.EntityLibraryRoots.Add(new() { EntityId = book.Id, LibraryRootId = state.LibraryRootId });
            else if (rendition == BookRendition.Ebook)
                bookRoot.LibraryRootId = state.LibraryRootId;
            var detail = await db.BookDetails.SingleOrDefaultAsync(row => row.EntityId == book.Id, ct);
            var format = extension.ToLowerInvariant() switch {
                ".epub" => BookFormat.Epub,
                ".pdf" => BookFormat.Pdf,
                _ => BookFormat.Audio
            };
            if (detail is null) db.BookDetails.Add(new() { EntityId = book.Id, BookType = BookType.Novel, Format = format });
            else if (rendition == BookRendition.Ebook) detail.Format = format;

            var adopted = await AdoptScannedBookSourceAsync(book, path, file.SizeBytes, rendition,
                state.LibraryRootId, boundary.Mount.LocalPath, now, ct);
            var sourceEntityId = adopted?.EntityId ?? book.Id;
            if (rendition == BookRendition.Audiobook && adopted is null) {
                var folder = Path.GetDirectoryName(path)
                    ?? throw new ArgumentException("The audiobook file has no mapped parent folder.");
                var groupPath = FileSystemPathComparison.Equals(folder, boundary.Mount.LocalPath) ? path : folder;
                var folderCode = EntitySourceCode.Folder.ToCode();
                var existingFolder = await db.EntitySources.SingleOrDefaultAsync(source =>
                    source.EntityId == book.Id && source.Code == folderCode, ct);
                if (existingFolder is not null && !FileSystemPathComparison.Equals(existingFolder.Value, groupPath))
                    throw new ArgumentException("The Book already has a different audiobook folder. Review that association first.");
                if (existingFolder is null) db.EntitySources.Add(new() {
                    EntityId = book.Id, Code = folderCode, Value = groupPath, UpdatedAt = now
                });
                sourceEntityId = Guid.NewGuid();
                db.Entities.Add(new() { Id = sourceEntityId, ParentEntityId = book.Id,
                    KindCode = EntityKind.AudioTrack.ToCode(), Title = remoteTarget.Title,
                    SortOrder = 0, IsNsfw = book.IsNsfw, CreatedAt = now, UpdatedAt = now });
                db.AudioTrackDetails.Add(new() { EntityId = sourceEntityId });
                db.EntityLibraryRoots.Add(new() { EntityId = sourceEntityId, LibraryRootId = state.LibraryRootId });
            }

            var sourceId = adopted?.SourceFileId ?? Guid.NewGuid();
            var mime = extension.ToLowerInvariant() switch {
                ".epub" => MediaContentTypes.Epub,
                ".pdf" => MediaContentTypes.Pdf,
                ".mp3" => MediaContentTypes.AudioMpeg,
                _ => MediaContentTypes.AudioMp4
            };
            if (adopted is null)
                db.EntityFiles.Add(new() { Id = sourceId, EntityId = sourceEntityId, Role = EntityFileRole.Source,
                    Path = path, MimeType = mime, SizeBytes = file.SizeBytes, CreatedAt = now, UpdatedAt = now });
            var identity = new ManagedTargetIdentity(remoteTarget.RemoteId, remoteTarget.EntityKind,
                remoteTarget.SeasonNumber, remoteTarget.EpisodeNumber, remoteTarget.AbsoluteNumber, remoteTarget.IssueLabel);
            db.ManagedSourceBindings.Add(new() { Id = Guid.NewGuid(), HoldingId = holding.Id,
                RemoteTargetId = identity.RemoteTargetId, Kind = identity.Kind, EntityId = sourceEntityId,
                SourceFileId = sourceId, RemoteFileId = file.RemoteId, LocalPath = path,
                SizeBytes = file.SizeBytes, WrittenAt = written, IsAvailable = true });
            holding.TargetsJson = JsonSerializer.Serialize(new[] { new ManagedTargetBinding(identity, sourceEntityId) }, Json);
            holding.SelectionsJson = JsonSerializer.Serialize(new[] {
                new ManagedBindingSelection(identity.RemoteTargetId, sourceEntityId, sourceId)
            }, Json);
            holding.Status = ManagedTrackingStatus.Tracking;
            holding.Revision++;
            holding.LastCheckedAt = now;
            holding.NextCheckAt = now.AddMinutes(1);
            holding.Problem = null;
            var operation = new ManagedRequestOperation(current.Operation.State);
            operation.ConfirmFiles();
            await db.SaveChangesAsync(ct);
            await UpdateAsync(operation, state.Revision, null, ct);
            await LibraryScanJobs.QueueScansForRootAsync(queue, state.LibraryRootId,
                current.Plan.Title, new(false, false, false, true, false), ct);
        }, token)) throw new EntityLifecycleMutationConflictException(state.EntityId);
        await transaction.CommitAsync(token);
        return new(true);
    }

    private static void RequireCompleteAudiobookFolder(string path, string libraryPath, CancellationToken token) {
        var folder = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("The audiobook file has no mapped parent folder.");
        // A file directly in the watched root is its own scanner group. In a subfolder, the
        // scanner treats every audio part as one Book; a one-file manager report cannot claim it.
        if (FileSystemPathComparison.Equals(folder, libraryPath)) return;
        try {
            var options = new EnumerationOptions {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (var candidate in Directory.EnumerateFiles(folder, "*", options)) {
                token.ThrowIfCancellationRequested();
                if (!FileSystemPathComparison.Equals(candidate, path)
                    && SupportedExtensions.Audiobook.Contains(Path.GetExtension(candidate)))
                    throw new ArgumentException(
                        "The manager reported one audiobook file, but its mapped book folder contains other audio files. Review every part before completing this request.");
            }
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            throw new ArgumentException("The mapped audiobook folder could not be inspected. Review its files before completing this request.", error);
        }
    }
}
