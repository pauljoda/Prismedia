using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Exercises the durable acquisition activity log store: append + newest-first listing, the limit clamp,
/// the per-entity filter, and the load-bearing property that a history entry SURVIVES the deletion of its
/// acquisition (the FK is SetNull). The in-memory provider does not enforce the FK cascade, so the survival
/// case is proved by writing an entry with a null acquisition id — the shape the SetNull FK produces in
/// Postgres once the acquisition is gone (see the model configuration comment on the FK).
/// </summary>
public sealed class EfAcquisitionHistoryStoreTests {
    [Fact]
    public async Task AddThenListReturnsTheEntry() {
        await using var db = CreateContext();
        var store = new EfAcquisitionHistoryStore(db);

        await store.AddAsync(new AcquisitionHistoryEntry(
            Guid.NewGuid(), Guid.NewGuid(), EntityKind.Movie, AcquisitionHistoryEvent.Grabbed,
            "Dune", "Dune.2021.2160p", "Prowlarr", "qBittorrent", "bluray-2160p", 500, "Sent to client."),
            CancellationToken.None);

        var entry = Assert.Single(await store.ListAsync(200, entityId: null, CancellationToken.None));
        Assert.Equal("Dune", entry.Title);
        Assert.Equal(AcquisitionHistoryEvent.Grabbed, entry.Event);
        Assert.Equal("Dune.2021.2160p", entry.ReleaseTitle);
        Assert.Equal("bluray-2160p", entry.QualityCode);
        Assert.Equal(500, entry.FormatScore);
    }

    [Fact]
    public async Task ListReturnsNewestFirst() {
        await using var db = CreateContext();
        var older = HistoryRow(AcquisitionHistoryEvent.Grabbed, DateTimeOffset.UtcNow.AddHours(-2));
        var newer = HistoryRow(AcquisitionHistoryEvent.Imported, DateTimeOffset.UtcNow);
        db.AcquisitionHistory.AddRange(older, newer);
        await db.SaveChangesAsync();

        var entries = await new EfAcquisitionHistoryStore(db).ListAsync(200, entityId: null, CancellationToken.None);

        Assert.Equal([newer.Id, older.Id], entries.Select(entry => entry.Id));
    }

    [Fact]
    public async Task ListClampsTheLimit() {
        await using var db = CreateContext();
        // Seed distinct timestamps so ordering is deterministic and Take() is meaningful.
        var baseTime = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++) {
            db.AcquisitionHistory.Add(HistoryRow(AcquisitionHistoryEvent.Grabbed, baseTime.AddSeconds(i)));
        }
        await db.SaveChangesAsync();
        var store = new EfAcquisitionHistoryStore(db);

        Assert.Equal(3, (await store.ListAsync(3, entityId: null, CancellationToken.None)).Count);
        // A non-positive limit falls back to the default (200), returning all five here.
        Assert.Equal(5, (await store.ListAsync(0, entityId: null, CancellationToken.None)).Count);
        Assert.Equal(5, (await store.ListAsync(-10, entityId: null, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task ListFiltersByEntity() {
        await using var db = CreateContext();
        var wanted = Guid.NewGuid();
        var other = Guid.NewGuid();
        db.AcquisitionHistory.AddRange(
            HistoryRow(AcquisitionHistoryEvent.Grabbed, DateTimeOffset.UtcNow, entityId: wanted),
            HistoryRow(AcquisitionHistoryEvent.Imported, DateTimeOffset.UtcNow, entityId: wanted),
            HistoryRow(AcquisitionHistoryEvent.Grabbed, DateTimeOffset.UtcNow, entityId: other));
        await db.SaveChangesAsync();

        var entries = await new EfAcquisitionHistoryStore(db).ListAsync(200, wanted, CancellationToken.None);

        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => Assert.Equal(wanted, entry.EntityId));
    }

    [Fact]
    public async Task EntryWithNullAcquisitionIdSurvivesAsTheAuditTrail() {
        // The durable-history crux: a Removed entry written with a null acquisition id (the shape the
        // SetNull FK leaves once the acquisition is hard-deleted) is still listed with its denormalized
        // title/kind/entity intact — the log outlives the acquisition it described.
        await using var db = CreateContext();
        var store = new EfAcquisitionHistoryStore(db);

        await store.AddAsync(new AcquisitionHistoryEntry(
            AcquisitionId: null, Guid.NewGuid(), EntityKind.Book, AcquisitionHistoryEvent.Removed,
            "A Deleted Book", Message: "Removed by user."),
            CancellationToken.None);

        var entry = Assert.Single(await store.ListAsync(200, entityId: null, CancellationToken.None));
        Assert.Null(entry.AcquisitionId);
        Assert.Equal("A Deleted Book", entry.Title);
        Assert.Equal(AcquisitionHistoryEvent.Removed, entry.Event);
    }

    private static AcquisitionHistoryRow HistoryRow(AcquisitionHistoryEvent @event, DateTimeOffset createdAt, Guid? entityId = null) =>
        new() {
            Id = Guid.NewGuid(),
            AcquisitionId = Guid.NewGuid(),
            EntityId = entityId,
            Kind = EntityKind.Book,
            Event = @event,
            Title = "Some Title",
            CreatedAt = createdAt
        };

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
