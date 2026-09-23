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
        var expectedKind = rendition == BookRendition.Ebook ? EntityKind.Book : EntityKind.AudioTrack;
        if (rendition == BookRendition.Ebook && snapshot.Files.Count != 1
            || snapshot.Files.Any(file => file.Targets is not [{ EntityKind: var kind }] || kind != expectedKind))
            throw new ArgumentException("A Book manager request requires exact files and one matching target per file.");
        var mappedFiles = await mounts.InspectAsync(state.ConnectionId, snapshot.Files, token);
        var parts = new List<(ManagedLibraryFile File, ManagedFileTarget Target, string Path, string Extension, DateTimeOffset Written)>();
        for (var index = 0; index < snapshot.Files.Count; index++) {
            var file = snapshot.Files[index];
            var mapped = mappedFiles[index];
            if (mapped.LibraryRootId != state.LibraryRootId || mapped.LocalPath is null)
                throw new ArgumentException("A Book file is outside this request's mapped library.");
            if (!mapped.IsReadable || !mapped.SizeMatches)
                return new(false, mapped.Problem ?? "The manager reports a Book file that Prismedia cannot yet read.");
            var extension = Path.GetExtension(mapped.LocalPath);
            if (!(rendition == BookRendition.Ebook ? SupportedExtensions.Book : SupportedExtensions.Audiobook).Contains(extension))
                throw new ArgumentException("The manager's Book file has an unsupported rendition format.");
            parts.Add((file, file.Targets[0], mapped.LocalPath, extension, WrittenAt(mapped.LocalPath)));
        }
        if (parts.Select(part => part.Path).Distinct(FileSystemPathComparison.Comparer).Count() != parts.Count)
            throw new ArgumentException("The Book manager reported the same local file more than once.");
        if (rendition == BookRendition.Audiobook && parts.Select(part => Path.GetDirectoryName(part.Path))
            .Distinct(FileSystemPathComparison.Comparer).Count() != 1)
            throw new ArgumentException("The audiobook parts must belong to one mapped Book folder.");
        var partPaths = parts.Select(part => part.Path).ToArray();
        var pathLengths = partPaths.Select(path => path.Length).Distinct().ToArray();
        var existingPaths = await db.EntityFiles.AsNoTracking()
            .Where(source => source.Role == EntityFileRole.Source || source.Role == EntityFileRole.UnavailableSource)
            .Where(source => pathLengths.Contains(source.Path.Length))
            .Select(source => new { source.Id, source.EntityId, source.Path })
            .ToArrayAsync(token);
        var lifecycleIds = new List<Guid> { state.EntityId };
        foreach (var part in parts) {
            var existingSources = existingPaths.Where(source => FileSystemPathComparison.Equals(part.Path, source.Path)).ToArray();
            if (existingSources.Length > 1)
                throw new ArgumentException("Several local items claim this Book file. Review their associations.");
            if (existingSources is [{ } existingSource]) {
                lifecycleIds.Add(existingSource.EntityId);
                var parentId = await db.Entities.AsNoTracking().Where(row => row.Id == existingSource.EntityId)
                    .Select(row => row.ParentEntityId).SingleOrDefaultAsync(token);
                if (parentId is { } parent) lifecycleIds.Add(parent);
            }
        }
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (!await lifecycle.ExecuteManyAsync(lifecycleIds, async ct => {
            var current = await LockAsync(state.OperationId, state.Revision, ct);
            var boundary = await RequireBoundaryAsync(current.Operation, current.Plan, true, ct);
            foreach (var part in parts) {
                var expectedPath = ExternalLibraryPaths.Resolve(boundary.Mount.RemotePath, boundary.Mount.LocalPath, part.File.Path);
                if (expectedPath is null || !FileSystemPathComparison.Equals(part.Path, expectedPath))
                    throw new ArgumentException("A Book file's mapped path changed before import.");
            }
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
                RequireCompleteAudiobookFolder(partPaths, library.Path, ct);
            foreach (var part in parts) {
                await using var bytes = new FileStream(part.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                if (bytes.Length != part.File.SizeBytes || WrittenAt(part.Path) != part.Written)
                    throw new ArgumentException("A Book file changed during import verification. Refresh its evidence.");
            }

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
            var format = parts[0].Extension.ToLowerInvariant() switch {
                ".epub" => BookFormat.Epub,
                ".pdf" => BookFormat.Pdf,
                _ => BookFormat.Audio
            };
            if (detail is null) db.BookDetails.Add(new() { EntityId = book.Id, BookType = BookType.Novel, Format = format });
            else if (rendition == BookRendition.Ebook) detail.Format = format;

            var targets = new List<ManagedTargetBinding>();
            var selections = new List<ManagedBindingSelection>();
            for (var index = 0; index < parts.Count; index++) {
                var part = parts[index];
                var adopted = await AdoptScannedBookSourceAsync(book, part.Path, part.File.SizeBytes, rendition,
                    state.LibraryRootId, boundary.Mount.LocalPath, partPaths, now, ct);
                var sourceEntityId = adopted?.EntityId ?? book.Id;
                if (rendition == BookRendition.Audiobook && adopted is null) {
                    var folder = Path.GetDirectoryName(part.Path)
                        ?? throw new ArgumentException("The audiobook file has no mapped parent folder.");
                    var groupPath = FileSystemPathComparison.Equals(folder, boundary.Mount.LocalPath) ? part.Path : folder;
                    var folderCode = EntitySourceCode.Folder.ToCode();
                    var existingFolder = await db.EntitySources.SingleOrDefaultAsync(source =>
                        source.EntityId == book.Id && source.Code == folderCode, ct);
                    if (existingFolder is not null && !FileSystemPathComparison.Equals(existingFolder.Value, groupPath))
                        throw new ArgumentException("The Book already has a different audiobook folder. Review that association first.");
                    if (existingFolder is null && index == 0) db.EntitySources.Add(new() {
                        EntityId = book.Id, Code = folderCode, Value = groupPath, UpdatedAt = now
                    });
                    sourceEntityId = Guid.NewGuid();
                    db.Entities.Add(new() { Id = sourceEntityId, ParentEntityId = book.Id,
                        KindCode = EntityKind.AudioTrack.ToCode(), Title = part.Target.Title,
                        SortOrder = index, IsNsfw = book.IsNsfw, CreatedAt = now, UpdatedAt = now });
                    db.AudioTrackDetails.Add(new() { EntityId = sourceEntityId });
                    db.EntityLibraryRoots.Add(new() { EntityId = sourceEntityId, LibraryRootId = state.LibraryRootId });
                }

                var sourceId = adopted?.SourceFileId ?? Guid.NewGuid();
                var mime = part.Extension.ToLowerInvariant() switch {
                    ".epub" => MediaContentTypes.Epub,
                    ".pdf" => MediaContentTypes.Pdf,
                    ".mp3" => MediaContentTypes.AudioMpeg,
                    _ => MediaContentTypes.AudioMp4
                };
                if (adopted is null)
                    db.EntityFiles.Add(new() { Id = sourceId, EntityId = sourceEntityId, Role = EntityFileRole.Source,
                        Path = part.Path, MimeType = mime, SizeBytes = part.File.SizeBytes, CreatedAt = now, UpdatedAt = now });
                var identity = new ManagedTargetIdentity(part.Target.RemoteId, part.Target.EntityKind,
                    part.Target.SeasonNumber, part.Target.EpisodeNumber, part.Target.AbsoluteNumber, part.Target.IssueLabel);
                db.ManagedSourceBindings.Add(new() { Id = Guid.NewGuid(), HoldingId = holding.Id,
                    RemoteTargetId = identity.RemoteTargetId, Kind = identity.Kind, EntityId = sourceEntityId,
                    SourceFileId = sourceId, RemoteFileId = part.File.RemoteId, LocalPath = part.Path,
                    SizeBytes = part.File.SizeBytes, WrittenAt = part.Written, IsAvailable = true });
                targets.Add(new(identity, sourceEntityId));
                selections.Add(new(identity.RemoteTargetId, sourceEntityId, sourceId));
            }
            holding.TargetsJson = JsonSerializer.Serialize(targets, Json);
            holding.SelectionsJson = JsonSerializer.Serialize(selections, Json);
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

    private static void RequireCompleteAudiobookFolder(IReadOnlyList<string> paths, string libraryPath, CancellationToken token) {
        var folder = Path.GetDirectoryName(paths[0])
            ?? throw new ArgumentException("The audiobook file has no mapped parent folder.");
        // A file directly in the watched root is its own scanner group. In a subfolder,
        // every supported audio file must be represented by the manager's exact file list.
        if (FileSystemPathComparison.Equals(folder, libraryPath)) {
            if (paths.Count != 1)
                throw new ArgumentException("Files directly in the audiobook root are separate scanner works. Review this association.");
            return;
        }
        try {
            var options = new EnumerationOptions {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (var candidate in Directory.EnumerateFiles(folder, "*", options)) {
                token.ThrowIfCancellationRequested();
                if (!paths.Any(path => FileSystemPathComparison.Equals(candidate, path))
                    && SupportedExtensions.Audiobook.Contains(Path.GetExtension(candidate)))
                    throw new ArgumentException(
                        "The manager omitted audio files from its mapped book folder. Review every part before completing this request.");
            }
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            throw new ArgumentException("The mapped audiobook folder could not be inspected. Review its files before completing this request.", error);
        }
    }
}
