using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Covers the Wanted read-model list methods on <see cref="EfMonitorStore"/>: the Missing list (active
/// per-item monitors not yet acquired, excluding container follows and imported ones), the Cutoff Unmet
/// list (imported monitors still below their kind's cutoff, excluding at/above-cutoff ones), paging totals
/// and slices, the kind filter, and the next-search backoff projection.
/// </summary>
public sealed class EfMonitorStoreWantedTests {
    [Fact]
    public async Task MissingListsAnActiveNonImportedMonitor() {
        await using var db = CreateContext();
        await SeedItemMonitorAsync(db, EntityKind.Book, AcquisitionStatus.Failed);
        var store = new EfMonitorStore(db);

        var page = await store.ListMissingAsync(1, 50, null, CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal(1, page.Total);
        Assert.Equal(AcquisitionStatus.Failed, item.AcquisitionStatus);
        Assert.Equal(MonitorStatus.Active, item.MonitorStatus);
        Assert.Null(item.OwnedQuality); // nothing owned on the missing list
    }

    [Fact]
    public async Task MissingExcludesImportedMonitors() {
        await using var db = CreateContext();
        await SeedItemMonitorAsync(db, EntityKind.Book, AcquisitionStatus.Imported);
        var store = new EfMonitorStore(db);

        var page = await store.ListMissingAsync(1, 50, null, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task MissingExcludesContainerFollows() {
        // A container monitor (author/artist discovery follow) has an EntityId but no AcquisitionId — it is
        // not a missing item, so it never appears on the Missing list.
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        db.Monitors.Add(new MonitorRow {
            Id = Guid.NewGuid(), Kind = EntityKind.BookAuthor, EntityId = Guid.NewGuid(),
            Status = MonitorStatus.Active, Title = "Some Author", CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);

        var page = await store.ListMissingAsync(1, 50, null, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task MissingExcludesPausedMonitors() {
        await using var db = CreateContext();
        var monitorId = await SeedItemMonitorAsync(db, EntityKind.Book, AcquisitionStatus.Failed);
        var monitor = await db.Monitors.FirstAsync(m => m.Id == monitorId);
        monitor.Status = MonitorStatus.Paused;
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);

        var page = await store.ListMissingAsync(1, 50, null, CancellationToken.None);

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task MissingExcludesAnOrphanedMonitorWhoseAcquisitionWasDeleted() {
        // Hard-deleting an acquisition nulls the monitor's AcquisitionId (SetNull FK); the sweep then pauses
        // such an orphan. An orphan (AcquisitionId null) is container-follow-shaped, so Missing — which
        // requires a live AcquisitionId — excludes it, leaving container follows and orphans out alike.
        await using var db = CreateContext();
        var monitorId = await SeedItemMonitorAsync(db, EntityKind.Book, AcquisitionStatus.Failed);
        var monitor = await db.Monitors.FirstAsync(m => m.Id == monitorId);
        monitor.AcquisitionId = null; // the SetNull FK effect
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);

        var page = await store.ListMissingAsync(1, 50, null, CancellationToken.None);

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task MissingKindFilterOnlyReturnsTheRequestedKind() {
        await using var db = CreateContext();
        await SeedItemMonitorAsync(db, EntityKind.Book, AcquisitionStatus.Failed);
        await SeedItemMonitorAsync(db, EntityKind.Movie, AcquisitionStatus.Failed);
        var store = new EfMonitorStore(db);

        var books = await store.ListMissingAsync(1, 50, EntityKind.Book, CancellationToken.None);
        var movies = await store.ListMissingAsync(1, 50, EntityKind.Movie, CancellationToken.None);

        Assert.Equal(1, books.Total);
        Assert.Equal(EntityKind.Book, Assert.Single(books.Items).Kind);
        Assert.Equal(1, movies.Total);
        Assert.Equal(EntityKind.Movie, Assert.Single(movies.Items).Kind);
    }

    [Fact]
    public async Task MissingPagesTotalAndSlice() {
        await using var db = CreateContext();
        // Five missing monitors with staggered CreatedAt so the newest-first order is deterministic.
        var baseTime = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++) {
            await SeedItemMonitorAsync(db, EntityKind.Book, AcquisitionStatus.Failed, createdAt: baseTime.AddMinutes(i), title: $"Book {i}");
        }

        var store = new EfMonitorStore(db);
        var firstPage = await store.ListMissingAsync(1, 2, null, CancellationToken.None);
        var secondPage = await store.ListMissingAsync(2, 2, null, CancellationToken.None);
        var thirdPage = await store.ListMissingAsync(3, 2, null, CancellationToken.None);

        Assert.Equal(5, firstPage.Total); // total is the full match count, independent of the page slice
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(2, secondPage.Items.Count);
        Assert.Single(thirdPage.Items);
        // Newest-first: "Book 4" (latest CreatedAt) leads.
        Assert.Equal("Book 4", firstPage.Items[0].Title);
        // No overlap across pages.
        var seen = firstPage.Items.Concat(secondPage.Items).Concat(thirdPage.Items).Select(i => i.MonitorId).ToArray();
        Assert.Equal(5, seen.Distinct().Count());
    }

    [Fact]
    public async Task MissingPageSizeIsClampedToTheMaximum() {
        await using var db = CreateContext();
        await SeedItemMonitorAsync(db, EntityKind.Book, AcquisitionStatus.Failed);
        var store = new EfMonitorStore(db);

        // A huge requested page size is clamped (no throw, returns the available rows).
        var page = await store.ListMissingAsync(1, 100_000, null, CancellationToken.None);

        Assert.Single(page.Items);
    }

    [Fact]
    public async Task MissingNextSearchReflectsBackoffDoubling() {
        // barren=1 must double the base interval relative to barren=0, mirroring the sweep's exponential
        // backoff. We compare the two ETAs against the shared last-searched anchor.
        await using var db = CreateContext();
        var lastSearched = DateTimeOffset.UtcNow.AddDays(-10);
        var barren0 = await SeedItemMonitorAsync(db, EntityKind.Book, AcquisitionStatus.Failed, lastSearchedAt: lastSearched, barrenSearches: 0);
        var barren1 = await SeedItemMonitorAsync(db, EntityKind.Book, AcquisitionStatus.Failed, lastSearchedAt: lastSearched, barrenSearches: 1);
        var store = new EfMonitorStore(db);

        var page = await store.ListMissingAsync(1, 50, null, CancellationToken.None);
        var item0 = page.Items.Single(i => i.MonitorId == barren0);
        var item1 = page.Items.Single(i => i.MonitorId == barren1);

        Assert.NotNull(item0.NextSearchAt);
        Assert.NotNull(item1.NextSearchAt);
        var gap0 = item0.NextSearchAt!.Value - lastSearched;
        var gap1 = item1.NextSearchAt!.Value - lastSearched;
        Assert.Equal(gap0 * 2, gap1); // one barren search doubles the interval
    }

    [Fact]
    public async Task MissingNextSearchIsNullWhenNeverSearched() {
        await using var db = CreateContext();
        await SeedItemMonitorAsync(db, EntityKind.Book, AcquisitionStatus.Failed);
        var store = new EfMonitorStore(db);

        var item = Assert.Single((await store.ListMissingAsync(1, 50, null, CancellationToken.None)).Items);

        Assert.Null(item.LastSearchedAt);
        Assert.Null(item.NextSearchAt); // due immediately — no ETA
    }

    [Fact]
    public async Task CutoffUnmetListsAnImportedMovieBelowItsProfileCutoff() {
        await using var db = CreateContext();
        await SeedImportedMediaMonitorAsync(db, EntityKind.Movie, owned: "webdl-720p", cutoff: "bluray-1080p");
        var store = new EfMonitorStore(db);

        var page = await store.ListCutoffUnmetAsync(1, 50, null, CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal("webdl-720p", item.OwnedQuality);
        Assert.Equal("bluray-1080p", item.CutoffQuality);
        Assert.Equal(AcquisitionStatus.Imported, item.AcquisitionStatus);
    }

    [Fact]
    public async Task CutoffUnmetExcludesAMovieAtOrAboveCutoff() {
        await using var db = CreateContext();
        await SeedImportedMediaMonitorAsync(db, EntityKind.Movie, owned: "bluray-1080p", cutoff: "bluray-1080p");
        await SeedImportedMediaMonitorAsync(db, EntityKind.Movie, owned: "remux-2160p", cutoff: "bluray-1080p");
        var store = new EfMonitorStore(db);

        var page = await store.ListCutoffUnmetAsync(1, 50, null, CancellationToken.None);

        Assert.Empty(page.Items); // both at/above cutoff are refined out per page
        Assert.Equal(2, page.Total); // total is the imported+active upper bound (documented)
    }

    [Fact]
    public async Task CutoffUnmetListsABookBelowItsTierCutoffAndExcludesOneAtCutoff() {
        await using var db = CreateContext();
        // Below cutoff: owned web/reflowable, cutoff retail/reflowable.
        await SeedImportedBookMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable), title: "Below");
        // At cutoff: owned retail/reflowable == cutoff.
        await SeedImportedBookMonitorAsync(db, owned: new(BookSourceTier.Retail, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable), title: "AtCutoff");
        var store = new EfMonitorStore(db);

        var page = await store.ListCutoffUnmetAsync(1, 50, null, CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal("Below", item.Title);
        Assert.Equal("web/reflowable", item.OwnedQuality);
        Assert.Equal("retail/reflowable", item.CutoffQuality);
    }

    [Fact]
    public async Task CutoffUnmetExcludesUpgradeOffKinds() {
        await using var db = CreateContext();
        // A season pack never upgrades — imported below cutoff, but the sweep would fulfill it, so it must
        // not appear on Cutoff Unmet.
        await SeedImportedMediaMonitorAsync(db, EntityKind.VideoSeason, owned: "webdl-720p", cutoff: "bluray-1080p");
        var store = new EfMonitorStore(db);

        var page = await store.ListCutoffUnmetAsync(1, 50, null, CancellationToken.None);

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task CutoffUnmetKindFilterWorks() {
        await using var db = CreateContext();
        await SeedImportedMediaMonitorAsync(db, EntityKind.Movie, owned: "webdl-720p", cutoff: "bluray-1080p");
        await SeedImportedBookMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable));
        var store = new EfMonitorStore(db);

        var movies = await store.ListCutoffUnmetAsync(1, 50, EntityKind.Movie, CancellationToken.None);

        var item = Assert.Single(movies.Items);
        Assert.Equal(EntityKind.Movie, item.Kind);
    }

    [Fact]
    public async Task CutoffUnmetExcludesNonImportedMonitors() {
        await using var db = CreateContext();
        // A still-searching monitor never appears on Cutoff Unmet (it belongs on Missing).
        await SeedItemMonitorAsync(db, EntityKind.Movie, AcquisitionStatus.Failed);
        var store = new EfMonitorStore(db);

        var page = await store.ListCutoffUnmetAsync(1, 50, null, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    /// <summary>Seeds a per-item monitor (an acquisition + its monitor) and returns the monitor id.</summary>
    private static async Task<Guid> SeedItemMonitorAsync(
        PrismediaDbContext db,
        EntityKind kind,
        AcquisitionStatus status,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? lastSearchedAt = null,
        int barrenSearches = 0,
        string title = "Some Item") {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, Kind = kind, Status = status, Title = title, ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now
        });
        var monitorId = Guid.NewGuid();
        db.Monitors.Add(new MonitorRow {
            Id = monitorId, Kind = kind, AcquisitionId = acquisitionId, Status = MonitorStatus.Active,
            Title = title, LastSearchedAt = lastSearchedAt, BarrenSearches = barrenSearches, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return monitorId;
    }

    /// <summary>Seeds an imported media monitor (movie/season/album) with a governing profile carrying the ladder cutoff.</summary>
    private static async Task SeedImportedMediaMonitorAsync(
        PrismediaDbContext db,
        EntityKind kind,
        string owned,
        string cutoff,
        string title = "Some Media") {
        var now = DateTimeOffset.UtcNow;
        db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow {
            Id = Guid.NewGuid(), Kind = AcquisitionProfileKinds.For(kind), DisplayName = "Default", IsDefault = true,
            TargetLibraryRootId = Guid.NewGuid(), AutoPick = true, UpgradeUntilCutoff = true, CutoffQuality = cutoff,
            CreatedAt = now, UpdatedAt = now
        });
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, Kind = kind, Status = AcquisitionStatus.Imported, Title = title, ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            OwnedMediaQuality = owned, UpgradeQualityCaptured = true, CreatedAt = now, UpdatedAt = now
        });
        db.Monitors.Add(new MonitorRow {
            Id = Guid.NewGuid(), Kind = kind, AcquisitionId = acquisitionId, Status = MonitorStatus.Active,
            Title = title, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds an imported book monitor with a governing profile carrying the source/format tier cutoff.</summary>
    private static async Task SeedImportedBookMonitorAsync(
        PrismediaDbContext db,
        BookQualityRank owned,
        BookQualityRank cutoff,
        string title = "Some Book") {
        var now = DateTimeOffset.UtcNow;
        db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow {
            Id = Guid.NewGuid(), DisplayName = "Default", IsDefault = true, TargetLibraryRootId = Guid.NewGuid(),
            AutoPick = true, UpgradeUntilCutoff = true, CutoffSourceTier = cutoff.Source, CutoffFormatTier = cutoff.Format,
            CreatedAt = now, UpdatedAt = now
        });
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, Status = AcquisitionStatus.Imported, Title = title, ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            OwnedSourceTier = owned.Source, OwnedFormatTier = owned.Format, UpgradeQualityCaptured = true, CreatedAt = now, UpdatedAt = now
        });
        db.Monitors.Add(new MonitorRow {
            Id = Guid.NewGuid(), Kind = EntityKind.Book, AcquisitionId = acquisitionId, Status = MonitorStatus.Active,
            Title = title, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
