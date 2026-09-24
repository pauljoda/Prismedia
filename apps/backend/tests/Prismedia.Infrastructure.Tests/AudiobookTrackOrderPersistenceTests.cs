using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// An audiobook's track order is decided by one rule over all of its tracks: embedded track numbers
/// when every file has a distinct one, otherwise natural file-name order. Probing one track and
/// rescanning must never flip the stored order between the two.
/// </summary>
public sealed class AudiobookTrackOrderPersistenceTests {
    [Fact]
    public async Task ProbeAndScanApplyOneOrderRuleWithoutFlipping() {
        await using var db = new PrismediaDbContext(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"audiobook-track-order-{Guid.NewGuid():N}")
            .Options);
        var bookId = AddEntity(db, EntityKind.Book, "Book", null, null);
        var one = AddTrack(db, bookId, "/b/1.mp3", 0);
        var two = AddTrack(db, bookId, "/b/2.mp3", 1);
        var ten = AddTrack(db, bookId, "/b/10.mp3", 2);
        await db.SaveChangesAsync();
        var persistence = new LibraryScanPersistenceService(db);

        // One tagged file is not enough to overrule file-name order.
        await persistence.UpsertAudioTrackTagsAsync(one, null, null, "Coda", 3, CancellationToken.None);
        Assert.Equal([one, two, ten], await OrderAsync(db, bookId));

        await persistence.UpsertAudioTrackTagsAsync(two, null, null, "Middle", 2, CancellationToken.None);
        await persistence.UpsertAudioTrackTagsAsync(ten, null, null, "Opening", 1, CancellationToken.None);
        Assert.Equal([ten, two, one], await OrderAsync(db, bookId));
        var detail = await db.AudioTrackDetails.AsNoTracking().SingleAsync(row => row.EntityId == ten);
        Assert.Equal("Opening", detail.EmbeddedTitle);
        Assert.NotNull(detail.TagsRecordedAt);

        // A rescan writes file-name order first; the shared rule puts tag order straight back.
        foreach (var (track, index) in new[] { (one, 0), (two, 1), (ten, 2) }) {
            (await db.Entities.SingleAsync(row => row.Id == track)).SortOrder = index;
        }
        await db.SaveChangesAsync();
        await persistence.ApplyAudiobookTrackOrderAsync(bookId, CancellationToken.None);
        Assert.Equal([ten, two, one], await OrderAsync(db, bookId));
    }

    private static async Task<Guid[]> OrderAsync(PrismediaDbContext db, Guid bookId) =>
        await db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId == bookId)
            .OrderBy(row => row.SortOrder)
            .Select(row => row.Id)
            .ToArrayAsync();

    private static Guid AddTrack(PrismediaDbContext db, Guid bookId, string path, int sortOrder) {
        var id = AddEntity(db, EntityKind.AudioTrack, Path.GetFileNameWithoutExtension(path), bookId, sortOrder);
        db.AudioTrackDetails.Add(new AudioTrackDetailRow { EntityId = id });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = id,
            Role = EntityFileRole.Source,
            Path = path,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private static Guid AddEntity(PrismediaDbContext db, EntityKind kind, string title, Guid? parentId, int? sortOrder) {
        var id = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind.ToCode(),
            Title = title,
            ParentEntityId = parentId,
            SortOrder = sortOrder,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }
}
