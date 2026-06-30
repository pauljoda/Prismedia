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
        var store = new EfAcquisitionStore(db);

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
        var store = new EfAcquisitionStore(db);

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
        var store = new EfAcquisitionStore(db);
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
        var store = new EfAcquisitionStore(db);

        await store.MarkImportedWithQualityAsync(id, new BookQualityRank(BookSourceTier.Retail, BookFormatTier.Reflowable), "Imported.", CancellationToken.None);

        var row = await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == id);
        Assert.Equal(AcquisitionStatus.Imported, row.Status);
        Assert.Equal(BookSourceTier.Retail, row.OwnedSourceTier);
        Assert.Equal(BookFormatTier.Reflowable, row.OwnedFormatTier);
        Assert.True(row.UpgradeQualityCaptured);
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
