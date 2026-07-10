using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Covers the season-pack completeness gate in the due sweep. Gaps surface as
/// <see cref="Prismedia.Application.Acquisition.DueMonitor.MissingChildFallback"/> dues (the handler then
/// requests each missing episode individually). Once complete, transient acquisition bookkeeping is
/// detached while the stable Entity monitor remains active until the user explicitly turns it off.
/// </summary>
public sealed class EfMonitorStoreSeasonFallbackTests {
    [Fact]
    public async Task ImportedSeasonWithWantedEpisodesIsDueForMissingChildFallback() {
        await using var db = CreateContext();
        var (store, seasonEntityId, _) = await SeedImportedSeasonAsync(db, wantedEpisodes: 2, hintConsumed: true);

        var due = await store.ListDueMonitorsAsync(360, CancellationToken.None);

        var fallback = Assert.Single(due);
        Assert.True(fallback.MissingChildFallback);
        Assert.Equal(seasonEntityId, fallback.EntityId);
        // The monitor must NOT fulfill while the season has holes — the handler fulfills after covering them.
        Assert.Equal(MonitorStatus.Active, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task ImportedSeasonAwaitingScanReconcileIsNotDueAndStaysActive() {
        // An unconsumed import hint means the scan hasn't bound the pack's files yet — episode
        // completeness can't be judged, so the sweep neither fulfills nor emits fallback work.
        await using var db = CreateContext();
        var (store, _, _) = await SeedImportedSeasonAsync(db, wantedEpisodes: 2, hintConsumed: false);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Active, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task ImportedSeasonWithNoWantedEpisodesDetachesAcquisitionButKeepsEntityIntentActive() {
        await using var db = CreateContext();
        var (store, _, _) = await SeedImportedSeasonAsync(db, wantedEpisodes: 0, hintConsumed: true);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        var monitor = Assert.Single(await store.ListAsync(CancellationToken.None));
        Assert.Equal(MonitorStatus.Active, monitor.Status);
        Assert.Null(monitor.AcquisitionId);
    }

    [Fact]
    public async Task MissingChildFallbackHonorsTheSearchInterval() {
        await using var db = CreateContext();
        var (store, _, monitorId) = await SeedImportedSeasonAsync(db, wantedEpisodes: 1, hintConsumed: true);
        await store.MarkSearchedAsync(monitorId, CancellationToken.None);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Active, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    private static async Task<(EfMonitorStore Store, Guid SeasonEntityId, Guid MonitorId)> SeedImportedSeasonAsync(
        PrismediaDbContext db, int wantedEpisodes, bool hintConsumed) {
        var now = DateTimeOffset.UtcNow;
        var seasonEntityId = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = seasonEntityId, KindCode = EntityKind.VideoSeason.ToCode(), Title = "Season 1",
            CreatedAt = now, UpdatedAt = now
        });
        for (var episode = 1; episode <= wantedEpisodes; episode++) {
            db.Entities.Add(new EntityRow {
                Id = Guid.NewGuid(), KindCode = EntityKind.Video.ToCode(), Title = $"Episode {episode}",
                ParentEntityId = seasonEntityId, IsWanted = true, SortOrder = episode,
                CreatedAt = now, UpdatedAt = now
            });
        }

        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, Status = AcquisitionStatus.Imported, Title = "Show S01",
            EntityId = seasonEntityId, ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now
        });
        db.AcquisitionImportHints.Add(new AcquisitionImportHintRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisitionId, EntityId = seasonEntityId,
            SourcePath = "/downloads/show-s01", Consumed = hintConsumed, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var store = new EfMonitorStore(db);
        var view = await store.StartAsync(acquisitionId, EntityKind.VideoSeason, "Show S01", null, CancellationToken.None);
        return (store, seasonEntityId, view.Id);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
