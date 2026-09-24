using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Combines separately scanned ebook and audiobook works when one connection tracks both Book renditions.</summary>
public sealed partial class EfManagedTrackingStore {
    #region Actions - Sibling Lookup

    private async Task<SiblingBookHolding?> FindSiblingBookAsync(Guid connectionId, ManagedItemInput item,
        CancellationToken token) {
        if (item.EntityKind != EntityKind.Book) {
            return null;
        }

        return await (from holding in db.ManagedHoldings.AsNoTracking()
                      join owner in db.FulfillmentReservations.AsNoTracking() on holding.Id equals owner.OwnerId
                      where holding.ConnectionId == connectionId && holding.Kind == EntityKind.Book
                          && holding.RemoteId == item.RemoteId && holding.BookRendition != item.BookRendition
                          && holding.Status != ManagedTrackingStatus.Released && owner.ReleasedAt == null
                      select new SiblingBookHolding(holding.Id, owner.EntityId,
                          holding.BookRendition!.Value, holding.ItemJson, holding.Status))
            .SingleOrDefaultAsync(token);
    }

    private async Task<Guid[]> AudioChildIdsAsync(Guid audioBookId, CancellationToken token) =>
        await db.Entities.AsNoTracking().Where(entity => entity.ParentEntityId == audioBookId)
            .Select(entity => entity.Id).OrderBy(id => id).ToArrayAsync(token);

    #endregion

    #region Actions - Book Combination

    private async Task CombineAudioIntoEbookAsync(Guid ebookBookId, Guid audioBookId, Guid[] expectedAudioChildIds,
        IReadOnlyList<Guid> selectedAudioEntityIds, CancellationToken token) {
        var books = await db.Entities.Where(entity => entity.Id == ebookBookId || entity.Id == audioBookId).ToArrayAsync(token);
        var ebook = books.SingleOrDefault(entity => entity.Id == ebookBookId);
        var audio = books.SingleOrDefault(entity => entity.Id == audioBookId);
        if (ebook is null || audio is null || ebook.KindCode != EntityKind.Book.ToCode()
            || audio.KindCode != EntityKind.Book.ToCode() || ebook.IsLibraryArchived || audio.IsLibraryArchived) {
            throw BookMergeReview();
        }

        var details = await db.BookDetails.AsNoTracking()
            .Where(detail => detail.EntityId == ebookBookId || detail.EntityId == audioBookId).ToArrayAsync(token);
        if (details.SingleOrDefault(detail => detail.EntityId == ebookBookId)?.Format is not (BookFormat.Epub or BookFormat.Pdf)
            || details.SingleOrDefault(detail => detail.EntityId == audioBookId)?.Format != BookFormat.Audio) {
            throw BookMergeReview();
        }

        var children = await db.Entities.Where(entity => entity.ParentEntityId == audioBookId)
            .OrderBy(entity => entity.Id).ToArrayAsync(token);
        if (children.Length == 0 || !children.Select(entity => entity.Id).SequenceEqual(expectedAudioChildIds)
            || children.Any(entity => entity.KindCode != EntityKind.AudioTrack.ToCode())
            || await db.Entities.AnyAsync(entity => entity.ParentEntityId == ebookBookId
                && entity.KindCode == EntityKind.AudioTrack.ToCode(), token)) {
            throw BookMergeReview();
        }

        var coveredIds = selectedAudioEntityIds.ToArray();
        if (!children.Select(entity => entity.Id).Order().SequenceEqual(coveredIds.Order())) {
            throw BookMergeReview();
        }

        if (await db.UserEntityStates.AnyAsync(state => state.EntityId == audioBookId, token)
            || await db.BookReadingChapters.AnyAsync(chapter => chapter.BookId == audioBookId, token)
            || await db.BookChapterAudioMappings.AnyAsync(mapping => mapping.BookId == audioBookId, token)
            || await db.Acquisitions.AnyAsync(acquisition => acquisition.EntityId == audioBookId, token)
            || await db.Monitors.AnyAsync(monitor => monitor.EntityId == audioBookId, token)
            || await db.EntityFiles.AnyAsync(file => file.EntityId == audioBookId, token)
            || await db.FulfillmentReservations.AnyAsync(owner => owner.EntityId == audioBookId
                && owner.ReleasedAt == null, token)) {
            throw BookMergeReview();
        }

        var folderCode = EntitySourceCode.Folder.ToCode();
        var audioFolder = await db.EntitySources.SingleOrDefaultAsync(source =>
            source.EntityId == audioBookId && source.Code == folderCode, token);
        if (audioFolder is null || await db.EntitySources.AnyAsync(source =>
                source.EntityId == ebookBookId && source.Code == folderCode, token)) {
            throw BookMergeReview();
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var child in children) {
            child.ParentEntityId = ebookBookId;
            child.UpdatedAt = now;
        }

        db.EntitySources.Remove(audioFolder);
        db.EntitySources.Add(new EntitySourceRow {
            EntityId = ebookBookId, Code = folderCode, Value = audioFolder.Value, UpdatedAt = now
        });
        audio.IsLibraryArchived = true;
        audio.UpdatedAt = now;
        ebook.UpdatedAt = now;
        await db.SaveChangesAsync(token);
        if (!await queue.HasPendingAsync(JobType.MapBookChapters, ebookBookId.ToString(), token)) {
            await queue.EnqueueAsync(EnqueueJobRequest.ForEntity(JobType.MapBookChapters,
                EntityKind.Book, ebookBookId.ToString(), ebook.Title), token);
        }
    }

    private async Task CombineEbookIntoAudioAsync(Guid audioBookId, Guid ebookBookId,
        IReadOnlyList<Guid> selectedEbookEntityIds, CancellationToken token) {
        if (selectedEbookEntityIds.Count != 1 || selectedEbookEntityIds[0] != ebookBookId) {
            throw BookMergeReview();
        }

        var books = await db.Entities.Where(entity => entity.Id == ebookBookId || entity.Id == audioBookId).ToArrayAsync(token);
        var ebook = books.SingleOrDefault(entity => entity.Id == ebookBookId);
        var audio = books.SingleOrDefault(entity => entity.Id == audioBookId);
        if (ebook is null || audio is null || ebook.KindCode != EntityKind.Book.ToCode()
            || audio.KindCode != EntityKind.Book.ToCode() || ebook.IsLibraryArchived || audio.IsLibraryArchived) {
            throw BookMergeReview();
        }

        var details = await db.BookDetails.Where(detail =>
            detail.EntityId == ebookBookId || detail.EntityId == audioBookId).ToArrayAsync(token);
        var ebookDetail = details.SingleOrDefault(detail => detail.EntityId == ebookBookId);
        var audioDetail = details.SingleOrDefault(detail => detail.EntityId == audioBookId);
        if (ebookDetail?.Format is not (BookFormat.Epub or BookFormat.Pdf)
            || audioDetail?.Format != BookFormat.Audio) {
            throw BookMergeReview();
        }

        var files = await db.EntityFiles.Where(file => file.EntityId == ebookBookId).ToArrayAsync(token);
        if (files.Length != 1 || await db.EntityFiles.AnyAsync(file => file.EntityId == audioBookId, token)
            || await db.Entities.AnyAsync(entity => entity.ParentEntityId == ebookBookId, token)
            || await db.UserEntityStates.AnyAsync(state => state.EntityId == ebookBookId, token)
            || await db.BookReadingChapters.AnyAsync(chapter => chapter.BookId == ebookBookId, token)
            || await db.BookChapterAudioMappings.AnyAsync(mapping => mapping.BookId == ebookBookId, token)
            || await db.Acquisitions.AnyAsync(acquisition => acquisition.EntityId == ebookBookId, token)
            || await db.Monitors.AnyAsync(monitor => monitor.EntityId == ebookBookId, token)
            || await db.FulfillmentReservations.AnyAsync(owner => owner.EntityId == ebookBookId
                && owner.ReleasedAt == null, token)) {
            throw BookMergeReview();
        }

        var now = DateTimeOffset.UtcNow;
        files[0].EntityId = audioBookId;
        audioDetail.BookType = ebookDetail.BookType;
        audioDetail.Format = ebookDetail.Format;
        audio.Title = ebook.Title;
        audio.UpdatedAt = now;
        ebook.IsLibraryArchived = true;
        ebook.UpdatedAt = now;
        var ebookRoot = await db.EntityLibraryRoots.SingleOrDefaultAsync(root => root.EntityId == ebookBookId, token);
        var audioRoot = await db.EntityLibraryRoots.SingleOrDefaultAsync(root => root.EntityId == audioBookId, token);
        if (ebookRoot is not null && audioRoot is not null) {
            audioRoot.LibraryRootId = ebookRoot.LibraryRootId;
        }

        await db.SaveChangesAsync(token);
        if (!await queue.HasPendingAsync(JobType.MapBookChapters, audioBookId.ToString(), token)) {
            await queue.EnqueueAsync(EnqueueJobRequest.ForEntity(JobType.MapBookChapters,
                EntityKind.Book, audioBookId.ToString(), audio.Title), token);
        }
    }

    #endregion

    #region Actions - Merge Review

    private static ArgumentException BookMergeReview() =>
        new("These Book records have additional files, activity, or changed associations. Review them separately before combining formats.");

    private static void VerifySiblingIdentity(ManagedItemInput item, SiblingBookHolding sibling) {
        var pinned = JsonSerializer.Deserialize<ManagedItemInput>(sibling.ItemJson, Json);
        if (pinned?.RemoteId != item.RemoteId || pinned.EntityKind != EntityKind.Book
            || pinned.BookRendition != sibling.Rendition
            || pinned.ExpectedExternalIds.Count != item.ExpectedExternalIds.Count
            || pinned.ExpectedExternalIds.Any(pair => item.ExpectedExternalIds.GetValueOrDefault(pair.Key) != pair.Value)
            || sibling.Status != ManagedTrackingStatus.Tracking) {
            throw BookMergeReview();
        }
    }

    #endregion

    private sealed record SiblingBookHolding(Guid Id, Guid WorkId, BookRendition Rendition, string ItemJson,
        ManagedTrackingStatus Status);
}
