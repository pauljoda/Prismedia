using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfAcquisitionStoreTests {
    [Fact]
    public async Task MarkCandidatesBlocklistedRejectsTheMatchingCandidate() {
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        AddCandidate(db, acquisitionId, infoHash: "hash", indexer: "Indexer", title: "Some Book (epub)", score: 100);
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        await store.MarkCandidatesBlocklistedAsync(acquisitionId, ReleaseIdentity.For("hash", "Indexer", "Some Book (epub)"), CancellationToken.None);

        var candidate = Assert.Single((await store.GetAsync(acquisitionId, CancellationToken.None))!.Candidates);
        Assert.False(candidate.Accepted);
        Assert.Contains(ReleaseRejectionReason.Blocklisted, candidate.Rejections);
    }

    [Fact]
    public async Task MarkCandidatesBlocklistedMarksDuplicateRowsForTheSameRelease() {
        // Two indexers returned the same torrent (same info hash). Blocklisting one must reject both,
        // so a duplicate doesn't stay selectable only to be refused at queue time.
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        AddCandidate(db, acquisitionId, infoHash: "DUP", indexer: "Indexer A", title: "Some Book A", score: 100);
        AddCandidate(db, acquisitionId, infoHash: "dup", indexer: "Indexer B", title: "Some Book B", score: 50);
        AddCandidate(db, acquisitionId, infoHash: "other", indexer: "Indexer C", title: "Different Book", score: 10);
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        // Identity is info-hash-first and case-insensitive, so "DUP"/"dup" collapse to the same identity.
        await store.MarkCandidatesBlocklistedAsync(acquisitionId, ReleaseIdentity.For("dup", null, null), CancellationToken.None);

        var candidates = (await store.GetAsync(acquisitionId, CancellationToken.None))!.Candidates;
        Assert.Equal(2, candidates.Count(c => !c.Accepted && c.Rejections.Contains(ReleaseRejectionReason.Blocklisted)));
        Assert.Single(candidates, c => c.Accepted); // the unrelated release stays selectable
    }

    [Fact]
    public async Task MarkCandidatesBlocklistedIsIdempotentOnTheReason() {
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        AddCandidate(db, acquisitionId, infoHash: null, indexer: "Indexer", title: "Some Book", score: 1);
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        var identity = ReleaseIdentity.For(null, "Indexer", "Some Book");

        await store.MarkCandidatesBlocklistedAsync(acquisitionId, identity, CancellationToken.None);
        await store.MarkCandidatesBlocklistedAsync(acquisitionId, identity, CancellationToken.None);

        var candidate = Assert.Single((await store.GetAsync(acquisitionId, CancellationToken.None))!.Candidates);
        Assert.Equal([ReleaseRejectionReason.Blocklisted], candidate.Rejections);
    }

    [Fact]
    public async Task MarkImportedWithQualityCapturesQualityAtomically() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow { Id = id, Status = AcquisitionStatus.Importing, Title = "B", ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        await store.MarkImportedWithQualityAsync(id, new BookQualityRank(BookSourceTier.Retail, BookFormatTier.Reflowable), "Imported.", CancellationToken.None);

        var row = await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == id);
        Assert.Equal(AcquisitionStatus.Imported, row.Status);
        Assert.Equal(BookSourceTier.Retail, row.OwnedSourceTier);
        Assert.Equal(BookFormatTier.Reflowable, row.OwnedFormatTier);
        Assert.True(row.UpgradeQualityCaptured);
    }

    [Fact]
    public async Task EnrichMetadataFillsGapsWithoutClobbering() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = id, Status = AcquisitionStatus.Pending, Title = "B", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            PosterUrl = null, Year = null, Description = null, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);

        await store.EnrichMetadataAsync(id, "a provider description", "http://cover", 2024, CancellationToken.None);
        var row = await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == id);
        Assert.Equal("http://cover", row.PosterUrl);   // gap filled
        Assert.Equal(2024, row.Year);                  // gap filled
        Assert.Equal("a provider description", row.Description); // gap filled

        // A second enrichment must not clobber anything now set (gap-only on every field, including description).
        await store.EnrichMetadataAsync(id, "a different, longer provider description", "http://other-cover", 1999, CancellationToken.None);
        var row2 = await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == id);
        Assert.Equal("http://cover", row2.PosterUrl);
        Assert.Equal(2024, row2.Year);
        Assert.Equal("a provider description", row2.Description);
    }

    [Fact]
    public async Task HintApplierDoesNotSeedTheEntityDescription() {
        // The entity description is owned by embedded file metadata + auto-identify; the hint applier must not
        // pre-empt them by seeding the request-time description onto the book.
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow { Id = acquisitionId, Status = AcquisitionStatus.Imported, Title = "B", ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now });
        db.BookDetails.Add(new BookDetailRow { EntityId = entityId, Format = BookFormat.Epub });
        db.AcquisitionImportHints.Add(new AcquisitionImportHintRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, SourcePath = "/media/books/Book", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            Description = "a request-time description", Consumed = false, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();

        await new AcquisitionHintApplier(db).ApplyAsync(entityId, "/media/books/Book/Title.epub", CancellationToken.None);

        Assert.False(await db.EntityDescriptions.AsNoTracking().AnyAsync(d => d.EntityId == entityId));
        Assert.True((await db.AcquisitionImportHints.AsNoTracking().FirstAsync()).Consumed); // hint still applied (ids/tier)
    }

    [Fact]
    public async Task HintApplierStampsOwnedSourceTierOnTheBook() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow { Id = acquisitionId, Status = AcquisitionStatus.Imported, Title = "B", ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now });
        db.BookDetails.Add(new BookDetailRow { EntityId = entityId, Format = BookFormat.Epub, SourceTier = BookSourceTier.Unknown });
        db.AcquisitionImportHints.Add(new AcquisitionImportHintRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, SourcePath = "/media/books/Book", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            OwnedSourceTier = BookSourceTier.Retail, OwnedFormatTier = BookFormatTier.Reflowable, Consumed = false, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var applied = await new AcquisitionHintApplier(db).ApplyAsync(entityId, "/media/books/Book/Title.epub", CancellationToken.None);

        Assert.True(applied);
        Assert.Equal(BookSourceTier.Retail, (await db.BookDetails.AsNoTracking().FirstAsync(d => d.EntityId == entityId)).SourceTier);
        Assert.True((await db.AcquisitionImportHints.AsNoTracking().FirstAsync()).Consumed);
    }

    [Fact]
    public async Task BindWantedBookAttachesTheImportedPathAndClearsWanted() {
        await using var db = CreateContext();
        var entityId = AddWantedEntity(db, EntityKindRegistry.Book.Code, "Elantris");
        AddHintWithEntity(db, entityId, "/media/books/Brandon Sanderson/Elantris (2005)/Elantris.epub");
        await db.SaveChangesAsync();

        var bound = await new AcquisitionHintApplier(db).BindWantedEntityAsync(EntityKind.Book,
            "/media/books/Brandon Sanderson/Elantris (2005)/Elantris.epub", CancellationToken.None);

        Assert.True(bound);
        var entity = await db.Entities.AsNoTracking().FirstAsync(row => row.Id == entityId);
        Assert.False(entity.IsWanted);
        var file = Assert.Single(await db.EntityFiles.AsNoTracking().Where(f => f.EntityId == entityId).ToArrayAsync());
        Assert.Equal(EntityFileRole.Source, file.Role);
        // Written exactly as the scan keys it, so the path-keyed upsert finds this entity (no duplicate).
        Assert.Equal("/media/books/Brandon Sanderson/Elantris (2005)/Elantris.epub", file.Path);
        Assert.Equal(Prismedia.Contracts.Media.MediaContentTypes.Epub, file.MimeType);
        // The hint stays unconsumed: the ordinary post-upsert apply still stamps ids and the source tier.
        Assert.False((await db.AcquisitionImportHints.AsNoTracking().FirstAsync()).Consumed);
    }

    [Fact]
    public async Task BindWantedBookToleratesADanglingEntityLink() {
        await using var db = CreateContext();
        AddHintWithEntity(db, Guid.NewGuid(), "/media/books/Author/Title/Title.epub");
        await db.SaveChangesAsync();

        Assert.False(await new AcquisitionHintApplier(db).BindWantedEntityAsync(EntityKind.Book,
            "/media/books/Author/Title/Title.epub", CancellationToken.None));
    }

    [Fact]
    public async Task BindWantedBookNeverRebindsAnEntityThatAlreadyHasASource() {
        await using var db = CreateContext();
        var entityId = AddWantedEntity(db, EntityKindRegistry.Book.Code, "Elantris");
        var now = DateTimeOffset.UtcNow;
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(), EntityId = entityId, Role = EntityFileRole.Source,
            Path = "/media/books/existing.epub", CreatedAt = now, UpdatedAt = now
        });
        AddHintWithEntity(db, entityId, "/media/books/Author/Title/Title.epub");
        await db.SaveChangesAsync();

        Assert.False(await new AcquisitionHintApplier(db).BindWantedEntityAsync(EntityKind.Book,
            "/media/books/Author/Title/Title.epub", CancellationToken.None));
        Assert.Single(await db.EntityFiles.AsNoTracking().Where(f => f.EntityId == entityId).ToArrayAsync());
    }

    [Fact]
    public async Task BindWantedAuthorAttachesTheFolderToTheWantedBooksParent() {
        await using var db = CreateContext();
        var authorId = AddWantedEntity(db, EntityKindRegistry.BookAuthor.Code, "Brandon Sanderson");
        var bookId = AddWantedEntity(db, EntityKindRegistry.Book.Code, "Elantris", parentEntityId: authorId);
        AddHintWithEntity(db, bookId, "/media/books/Brandon Sanderson/Elantris (2005)/Elantris.epub");
        await db.SaveChangesAsync();

        var bound = await new AcquisitionHintApplier(db).BindWantedParentAsync(EntityKind.BookAuthor,
            "/media/books/Brandon Sanderson", CancellationToken.None);

        Assert.True(bound);
        var author = await db.Entities.AsNoTracking().FirstAsync(row => row.Id == authorId);
        Assert.False(author.IsWanted);
        var file = Assert.Single(await db.EntityFiles.AsNoTracking().Where(f => f.EntityId == authorId).ToArrayAsync());
        Assert.Equal("/media/books/Brandon Sanderson", file.Path);
        // The book itself stays wanted until its own path binds.
        Assert.True((await db.Entities.AsNoTracking().FirstAsync(row => row.Id == bookId)).IsWanted);
    }

    [Fact]
    public async Task BindIgnoresHintsWithNoWantedEntityLink() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        db.AcquisitionImportHints.Add(new AcquisitionImportHintRow {
            Id = Guid.NewGuid(), AcquisitionId = Guid.NewGuid(), EntityId = null,
            SourcePath = "/media/books/Author/Title/Title.epub", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            Consumed = false, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();

        Assert.False(await new AcquisitionHintApplier(db).BindWantedEntityAsync(EntityKind.Book,
            "/media/books/Author/Title/Title.epub", CancellationToken.None));
    }

    private static Guid AddWantedEntity(PrismediaDbContext db, string kindCode, string title, Guid? parentEntityId = null) {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = id, KindCode = kindCode, Title = title, ParentEntityId = parentEntityId,
            IsWanted = true, CreatedAt = now, UpdatedAt = now
        });
        return id;
    }

    private static void AddHintWithEntity(PrismediaDbContext db, Guid entityId, string sourcePath) {
        var now = DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, Status = AcquisitionStatus.Imported, Title = "B", EntityId = entityId,
            ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now
        });
        db.AcquisitionImportHints.Add(new AcquisitionImportHintRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, EntityId = entityId, SourcePath = sourcePath,
            ExternalIdsJson = "{}", SourceUrlsJson = "[]", Consumed = false, CreatedAt = now, UpdatedAt = now
        });
    }

    private static void AddCandidate(PrismediaDbContext db, Guid acquisitionId, string? infoHash, string indexer, string title, double score) {
        var now = DateTimeOffset.UtcNow;
        if (db.Acquisitions.Local.All(a => a.Id != acquisitionId) && !db.Acquisitions.Any(a => a.Id == acquisitionId)) {
            db.Acquisitions.Add(new AcquisitionRow { Id = acquisitionId, Status = AcquisitionStatus.AwaitingSelection, Title = "B", ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now });
        }

        db.ReleaseCandidates.Add(new ReleaseCandidateRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, IndexerName = indexer, Title = title,
            InfoHash = infoHash, Accepted = true, Score = score, Protocol = DownloadProtocol.Torrent, RejectionsJson = "[]", CreatedAt = now
        });
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
