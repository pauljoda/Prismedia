using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class ScanMetadataProtectionTests {
    [Theory]
    [InlineData(EntityKind.Book)]
    [InlineData(EntityKind.ComicInstallment)]
    [InlineData(EntityKind.Image)]
    public async Task RescansPreserveProtectedTitlesWhileUpdatingOtherScanFacts(EntityKind kind) {
        await using var db = Context(); var root = await Root(db); var scan = new LibraryScanPersistenceService(db);
        var id = await Upsert(scan, kind, root, "First scan", false);
        var entity = await db.Entities.FindAsync(id); entity!.Title = "My chosen title";
        var evidence = await Protect(db, id, MetadataPatchField.Title, false);
        var again = await Upsert(scan, kind, root, "Changed embedded or filename title", true);
        Assert.Equal(id, again); Assert.Equal("My chosen title", (await db.Entities.FindAsync(id))!.Title);
        Assert.True((await db.Entities.FindAsync(id))!.IsNsfw);
        Assert.Equal(evidence, (await db.EntityMetadataFields.FindAsync(id, MetadataPatchField.Title))!.Evidence());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualDescriptionClearIsNotRefilledByEmbeddedBookMetadata(bool retainedEmptyRow) {
        await using var db = Context(); var root = await Root(db); var scan = new LibraryScanPersistenceService(db);
        var id = await Upsert(scan, EntityKind.Book, root, "Book", false);
        if (retainedEmptyRow) db.EntityDescriptions.Add(new() { EntityId = id, Value = "", UpdatedAt = DateTimeOffset.UtcNow });
        var evidence = await Protect(db, id, MetadataPatchField.Description, true);
        await scan.ApplyBookFileMetadataAsync(id, new BookFileMetadata { Summary = "Embedded summary", PageCount = 42 }, false, default);
        Assert.True(string.IsNullOrEmpty((await db.EntityDescriptions.FindAsync(id))?.Value));
        Assert.Equal(42, (await db.BookDetails.FindAsync(id))!.PageCount);
        Assert.Equal(evidence, (await db.EntityMetadataFields.FindAsync(id, MetadataPatchField.Description))!.Evidence());
    }

    [Fact]
    public async Task SidecarBatchHonorsTitleAndDescriptionLocksTogether() {
        await using var db = Context(); var id = Guid.NewGuid();
        db.Entities.Add(new() { Id = id, KindCode = EntityKind.Video.ToCode(), Title = "Fallback" });
        await db.SaveChangesAsync();
        await Protect(db, id, MetadataPatchField.Title, false); await Protect(db, id, MetadataPatchField.Description, true);
        await new LibraryScanPersistenceService(db).ApplyVideoSidecarMetadataBatchAsync([
            new(id, new VideoSidecarMetadata { Title = "Sidecar title", Description = "Sidecar summary" }, "Fallback", false)
        ], default);
        Assert.Equal("Fallback", (await db.Entities.FindAsync(id))!.Title);
        Assert.Null(await db.EntityDescriptions.FindAsync(id));
    }

    [Fact]
    public async Task AConcurrentManualTitleEditRequiresAFreshScanWithoutLosingTheUserValue() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var scanDb = database.CreateContext(); var root = await Root(scanDb);
        var id = await Upsert(new(scanDb), EntityKind.Book, root, "Old title", false);
        var fired = false;
        var lease = new BoundaryLease(async () => {
            if (fired) return; fired = true;
            await using var userDb = database.CreateContext();
            (await userDb.Entities.FindAsync(id))!.Title = "Concurrent manual title";
            await Protect(userDb, id, MetadataPatchField.Title, false);
        });
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Upsert(new(scanDb, lifecycle: lease), EntityKind.Book, root, "Stale scanned title", true));
        await using var check = database.CreateContext();
        Assert.Equal("Concurrent manual title", (await check.Entities.FindAsync(id))!.Title);
        Assert.True((await check.EntityMetadataFields.FindAsync(id, MetadataPatchField.Title))!.IsLocked);
        await Upsert(new(check), EntityKind.Book, root, "Retry scanned title", true);
        Assert.Equal("Concurrent manual title", (await check.Entities.FindAsync(id))!.Title);
        Assert.True((await check.Entities.FindAsync(id))!.IsNsfw);
    }

    [Fact]
    public async Task DescriptionProtectionIsReloadedAfterTheScanRead() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var scanDb = database.CreateContext(); var root = await Root(scanDb);
        var id = await Upsert(new(scanDb), EntityKind.Book, root, "Book", false);
        scanDb.EntityMetadataFields.Add(new() { EntityId = id, Field = MetadataPatchField.Description });
        await scanDb.SaveChangesAsync();
        var lease = new BoundaryLease(async () => {
            await using var userDb = database.CreateContext();
            await Protect(userDb, id, MetadataPatchField.Description, true);
        });
        await new LibraryScanPersistenceService(scanDb, lifecycle: lease).ApplyBookFileMetadataAsync(id,
            new BookFileMetadata { Summary = "Stale embedded summary", PageCount = 42 }, false, default);
        await using var check = database.CreateContext();
        Assert.Null(await check.EntityDescriptions.FindAsync(id));
        Assert.True((await check.EntityMetadataFields.FindAsync(id, MetadataPatchField.Description))!.IsLocked);
        Assert.Equal(42, (await check.BookDetails.FindAsync(id))!.PageCount);
    }

    [Fact]
    public async Task ScansRecordActualChangesAndUnlockingAllowsNewEvidence() {
        await using var db = Context(); var root = await Root(db); var scan = new LibraryScanPersistenceService(db);
        var id = await Upsert(scan, EntityKind.Book, root, "First scan", false);
        var original = (await db.EntityMetadataFields.FindAsync(id, MetadataPatchField.Title))!.Evidence();
        Assert.Equal(MetadataValueOrigin.Scan, original.Origin);
        await Upsert(scan, EntityKind.Book, root, "First scan", true);
        Assert.Equal(original, (await db.EntityMetadataFields.FindAsync(id, MetadataPatchField.Title))!.Evidence());
        var title = (await db.EntityMetadataFields.FindAsync(id, MetadataPatchField.Title))!;
        title.Apply(title.Evidence().WrittenByProvider("metadata-fixture", 0.9m, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        await Upsert(scan, EntityKind.Book, root, "Changed scan", false);
        Assert.Equal(MetadataValueOrigin.Scan, title.Origin); Assert.Null(title.ProviderId); Assert.Null(title.Confidence);
        await Protect(db, id, MetadataPatchField.Description, true);
        var description = (await db.EntityMetadataFields.FindAsync(id, MetadataPatchField.Description))!;
        description.Apply(description.Evidence().WithLock(false)); await db.SaveChangesAsync();
        await scan.ApplyBookFileMetadataAsync(id, new BookFileMetadata { Summary = "Now allowed" }, false, default);
        Assert.Equal("Now allowed", (await db.EntityDescriptions.FindAsync(id))!.Value);
        Assert.Equal(MetadataValueOrigin.Scan, description.Origin); Assert.False(description.IsCleared);
    }

    private static PrismediaDbContext Context() => new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task<Guid> Root(PrismediaDbContext db) {
        var id = Guid.NewGuid(); db.LibraryRoots.Add(new() { Id = id, Path = "/scan-fixture", Enabled = true });
        await db.SaveChangesAsync(); return id;
    }
    private static async Task<Guid> Upsert(LibraryScanPersistenceService scan, EntityKind kind, Guid root, string title, bool nsfw) {
        if (kind == EntityKind.Book) return await scan.UpsertSingleFileBookAsync("/scan-fixture/book.epub", title, root, nsfw, BookType.Novel, BookFormat.Epub, "application/epub+zip", null, null, default);
        if (kind == EntityKind.Image) return await scan.UpsertImageAsync("/scan-fixture/image.jpg", title, root, null, 100, 0, nsfw, default);
        var series = await scan.UpsertComicSeriesAsync("/scan-fixture/series", "Series", root, false, default);
        return await scan.UpsertComicInstallmentAsync("/scan-fixture/series/issue.cbz", title, root, series, 0, 1, "1", ComicInstallmentKind.Issue, 100, nsfw, null, default);
    }
    private static async Task<MetadataFieldEvidence> Protect(PrismediaDbContext db, Guid id, MetadataPatchField field, bool cleared) {
        var row = await db.EntityMetadataFields.FindAsync(id, field);
        if (row is null) { row = new() { EntityId = id, Field = field }; db.EntityMetadataFields.Add(row); }
        row.Apply(row.Evidence().WrittenByUser(cleared, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(); return row.Evidence();
    }
    private sealed class BoundaryLease(Func<Task> before) : IEntityLifecycleMutationLease {
        public async Task<bool> ExecuteAsync(Guid entityId, Func<CancellationToken, Task> mutation, CancellationToken token) {
            await before(); await mutation(token); return true;
        }
    }
}
