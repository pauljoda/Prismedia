using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfAcquisitionBlocklistStoreTests {
    [Fact]
    public async Task AddThenGetIdentitiesContainsTheIdentity() {
        await using var db = CreateContext();
        var store = new EfAcquisitionBlocklistStore(db);
        var identity = ReleaseIdentity.For("hashA", "Indexer", "Some Book");

        await store.AddAsync(new BlocklistAddRequest(identity, BlocklistReason.Failed, "Some Book", "Indexer", "hashA", null, "removed"), CancellationToken.None);

        var identities = await store.GetIdentitiesAsync(CancellationToken.None);
        Assert.Contains(identity, identities);
    }

    [Fact]
    public async Task AddIsIdempotentOnTheIdentity() {
        await using var db = CreateContext();
        var store = new EfAcquisitionBlocklistStore(db);
        var identity = ReleaseIdentity.For("hashA", "Indexer", "Some Book");

        await store.AddAsync(new BlocklistAddRequest(identity, BlocklistReason.Failed, "Some Book", "Indexer", "hashA", null, null), CancellationToken.None);
        await store.AddAsync(new BlocklistAddRequest(identity, BlocklistReason.Manual, "Some Book", "Indexer", "hashA", null, "again"), CancellationToken.None);

        var entries = await store.ListAsync(CancellationToken.None);
        var entry = Assert.Single(entries);
        // The original reason is kept; the second add is a no-op.
        Assert.Equal(BlocklistReason.Failed, entry.Reason);
    }

    [Fact]
    public async Task ListReturnsNewestFirst() {
        await using var db = CreateContext();
        var older = new AcquisitionBlocklistRow {
            Id = Guid.NewGuid(), Identity = "title:i|older", Reason = BlocklistReason.Failed, CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };
        var newer = new AcquisitionBlocklistRow {
            Id = Guid.NewGuid(), Identity = "title:i|newer", Reason = BlocklistReason.Manual, CreatedAt = DateTimeOffset.UtcNow
        };
        db.AcquisitionBlocklist.AddRange(older, newer);
        await db.SaveChangesAsync();

        var entries = await new EfAcquisitionBlocklistStore(db).ListAsync(CancellationToken.None);
        Assert.Equal([newer.Id, older.Id], entries.Select(entry => entry.Id));
    }

    [Fact]
    public async Task ListIncludesTheAssociatedWorkFromTheCurrentAcquisition() {
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            EntityId = entityId,
            Kind = EntityKind.AudioLibrary,
            Status = AcquisitionStatus.Imported,
            Title = "Hamilton",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.AcquisitionBlocklist.Add(new AcquisitionBlocklistRow {
            Id = Guid.NewGuid(),
            Identity = "title:indexer|hamilton.flac",
            Reason = BlocklistReason.Failed,
            Title = "Hamilton.FLAC",
            IndexerName = "Indexer",
            AcquisitionId = acquisitionId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var entry = Assert.Single(await new EfAcquisitionBlocklistStore(db).ListAsync(CancellationToken.None));
        Assert.Equal(entityId, entry.EntityId);
        Assert.Equal(EntityKind.AudioLibrary, entry.EntityKind);
        Assert.Equal("Hamilton", entry.EntityTitle);
    }

    [Fact]
    public async Task ListUsesDurableHistoryWhenTheAcquisitionWasRemoved() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        db.AcquisitionHistory.Add(new AcquisitionHistoryRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Kind = EntityKind.AudioLibrary,
            Event = AcquisitionHistoryEvent.Blocklisted,
            Title = "Hamilton",
            ReleaseTitle = "Hamilton.FLAC",
            IndexerName = "Indexer",
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.AcquisitionBlocklist.Add(new AcquisitionBlocklistRow {
            Id = Guid.NewGuid(),
            Identity = "title:indexer|hamilton.flac",
            Reason = BlocklistReason.Failed,
            Title = "Hamilton.FLAC",
            IndexerName = "Indexer",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var entry = Assert.Single(await new EfAcquisitionBlocklistStore(db).ListAsync(CancellationToken.None));
        Assert.Equal(entityId, entry.EntityId);
        Assert.Equal(EntityKind.AudioLibrary, entry.EntityKind);
        Assert.Equal("Hamilton", entry.EntityTitle);
    }

    [Fact]
    public async Task DeleteRemovesEntryAndReportsMissing() {
        await using var db = CreateContext();
        var store = new EfAcquisitionBlocklistStore(db);
        var row = new AcquisitionBlocklistRow { Id = Guid.NewGuid(), Identity = "title:i|x", Reason = BlocklistReason.Failed, CreatedAt = DateTimeOffset.UtcNow };
        db.AcquisitionBlocklist.Add(row);
        await db.SaveChangesAsync();

        Assert.True(await store.DeleteAsync(row.Id, CancellationToken.None));
        Assert.False(await store.DeleteAsync(row.Id, CancellationToken.None));
        Assert.Empty(await store.GetIdentitiesAsync(CancellationToken.None));
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
