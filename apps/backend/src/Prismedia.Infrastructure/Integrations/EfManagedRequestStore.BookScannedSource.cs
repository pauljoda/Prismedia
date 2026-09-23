using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Integrations;

public sealed partial class EfManagedRequestStore {
    private sealed record ScannedBookSource(Guid SourceFileId, Guid EntityId);

    private async Task<ScannedBookSource?> AdoptScannedBookSourceAsync(EntityRow book, string path,
        long sizeBytes, BookRendition rendition, Guid libraryRootId, string localRoot,
        DateTimeOffset now, CancellationToken token) {
        var candidates = await db.EntityFiles
            .Where(source => source.Role == EntityFileRole.Source || source.Role == EntityFileRole.UnavailableSource)
            .Where(source => source.Path.Length == path.Length)
            .ToArrayAsync(token);
        var matches = candidates.Where(source => FileSystemPathComparison.Equals(source.Path, path)).ToArray();
        if (matches.Length == 0) return null;
        if (matches is not [{ Role: EntityFileRole.Source } source]
            || source.SizeBytes is { } size && size != sizeBytes
            || await db.ManagedSourceBindings.AnyAsync(binding => binding.SourceFileId == source.Id, token))
            throw ScannedBookReview();
        var owner = await db.Entities.SingleAsync(entity => entity.Id == source.EntityId, token);
        if (rendition == BookRendition.Ebook) {
            if (owner.Id != book.Id) {
                if (source.Source != FileSourceKind.Scan.ToCode()
                    || owner.KindCode != EntityKind.Book.ToCode()
                    || await db.Entities.AnyAsync(entity => entity.ParentEntityId == owner.Id, token)
                    || await db.EntityFiles.AnyAsync(file => file.EntityId == owner.Id
                        && file.Id != source.Id && file.Role == EntityFileRole.Source, token)
                    || await db.BookDetails.Where(detail => detail.EntityId == owner.Id)
                        .Select(detail => detail.Format).SingleOrDefaultAsync(token) is not
                            (BookFormat.Epub or BookFormat.Pdf)) throw ScannedBookReview();
                await RequireUnclaimedScannedBookAsync(owner, book.Id, token);
                source.EntityId = book.Id;
                owner.IsLibraryArchived = true;
                owner.UpdatedAt = now;
            }
            source.SizeBytes = sizeBytes;
            source.UpdatedAt = now;
            return new(source.Id, book.Id);
        }

        if (owner.KindCode != EntityKind.AudioTrack.ToCode() || owner.ParentEntityId is not { } parentId
            || await db.EntityFiles.AnyAsync(file => file.EntityId == owner.Id
                && file.Id != source.Id && file.Role == EntityFileRole.Source, token))
            throw ScannedBookReview();
        if (parentId != book.Id) {
            var donor = await db.Entities.SingleAsync(entity => entity.Id == parentId, token);
            if (source.Source != FileSourceKind.Scan.ToCode()
                || donor.KindCode != EntityKind.Book.ToCode()
                || await db.BookDetails.Where(detail => detail.EntityId == donor.Id)
                    .Select(detail => detail.Format).SingleOrDefaultAsync(token) != BookFormat.Audio
                || await db.Entities.AnyAsync(entity => entity.ParentEntityId == donor.Id
                    && entity.Id != owner.Id, token)
                || await db.EntityFiles.AnyAsync(file => file.EntityId == donor.Id
                    && file.Role == EntityFileRole.Source, token)) throw ScannedBookReview();
            await RequireUnclaimedScannedBookAsync(donor, book.Id, token);
            if (owner.IsOrganized || await db.UserEntityStates.AnyAsync(state => state.EntityId == owner.Id, token)
                || await db.EntityConsumptionEvents.AnyAsync(entry => entry.EntityId == owner.Id, token)
                || await db.Acquisitions.AnyAsync(acquisition => acquisition.EntityId == owner.Id, token)
                || await db.Monitors.AnyAsync(monitor => monitor.EntityId == owner.Id, token)
                || await db.FulfillmentReservations.AnyAsync(reservation => reservation.EntityId == owner.Id
                    && reservation.ReleasedAt == null, token)) throw ScannedBookReview();
            var folderCode = EntitySourceCode.Folder.ToCode();
            var donorFolder = await db.EntitySources.SingleOrDefaultAsync(row =>
                row.EntityId == donor.Id && row.Code == folderCode, token);
            var folder = Path.GetDirectoryName(path) ?? throw ScannedBookReview();
            var expectedGroup = FileSystemPathComparison.Equals(folder, localRoot) ? path : folder;
            if (donorFolder is null || !FileSystemPathComparison.Equals(donorFolder.Value, expectedGroup))
                throw ScannedBookReview();
            var existingFolder = await db.EntitySources.SingleOrDefaultAsync(row =>
                row.EntityId == book.Id && row.Code == folderCode, token);
            if (existingFolder is not null && !FileSystemPathComparison.Equals(existingFolder.Value, expectedGroup))
                throw ScannedBookReview();
            if (existingFolder is null) db.EntitySources.Add(new() {
                EntityId = book.Id, Code = folderCode, Value = expectedGroup, UpdatedAt = now
            });
            db.EntitySources.Remove(donorFolder);
            owner.ParentEntityId = book.Id;
            owner.UpdatedAt = now;
            donor.IsLibraryArchived = true;
            donor.UpdatedAt = now;
        }
        var root = await db.EntityLibraryRoots.SingleOrDefaultAsync(row => row.EntityId == owner.Id, token);
        if (root is null) db.EntityLibraryRoots.Add(new() { EntityId = owner.Id, LibraryRootId = libraryRootId });
        else root.LibraryRootId = libraryRootId;
        source.SizeBytes = sizeBytes;
        source.UpdatedAt = now;
        return new(source.Id, owner.Id);
    }

    private async Task RequireUnclaimedScannedBookAsync(EntityRow donor, Guid targetBookId,
        CancellationToken token) {
        var exactWorkId = await db.EntityExternalIds.AsNoTracking()
            .Where(id => id.EntityId == targetBookId && id.Provider == ExternalIdProviders.OpenLibraryWork)
            .Select(id => id.Value).SingleAsync(token);
        if (donor.IsOrganized || donor.IsWanted || donor.IsLibraryArchived
            || await db.EntityExternalIds.AnyAsync(id => id.EntityId == donor.Id
                && (id.Provider != ExternalIdProviders.OpenLibraryWork || id.Value != exactWorkId), token)
            || await db.EntityMetadataFields.AnyAsync(field => field.EntityId == donor.Id && field.IsLocked, token)
            || await db.UserEntityStates.AnyAsync(state => state.EntityId == donor.Id, token)
            || await db.EntityConsumptionEvents.AnyAsync(entry => entry.EntityId == donor.Id, token)
            || await db.Acquisitions.AnyAsync(acquisition => acquisition.EntityId == donor.Id, token)
            || await db.Monitors.AnyAsync(monitor => monitor.EntityId == donor.Id, token)
            || await db.BookChapterAudioMappings.AnyAsync(mapping => mapping.BookId == donor.Id, token)
            || await db.FulfillmentReservations.AnyAsync(reservation => reservation.EntityId == donor.Id
                && reservation.ReleasedAt == null, token)) throw ScannedBookReview();
    }

    private static ArgumentException ScannedBookReview() => new(
        "The scanner already attached this Book file to a record with additional ownership or activity. Review that association before materialization.");
}
