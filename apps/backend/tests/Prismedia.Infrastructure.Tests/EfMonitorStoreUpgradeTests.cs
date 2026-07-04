using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Covers the upgrade-until-cutoff due-logic and the upgrade-child lifecycle in <see cref="EfMonitorStore"/>:
/// when an imported book is due for an upgrade re-search, when it fulfills instead (cutoff met, caps hit,
/// upgrade off), the one-in-flight interlock, and the success/failure counters.
/// </summary>
public sealed class EfMonitorStoreUpgradeTests {
    [Fact]
    public async Task ImportedBelowCutoffWithUpgradeOnIsDueAsUpgrade() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable));

        var due = await store.ListDueMonitorsAsync(360, CancellationToken.None);

        var monitor = Assert.Single(due);
        Assert.True(monitor.IsUpgrade);
    }

    [Fact]
    public async Task ImportedAtCutoffFulfills() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Retail, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable));

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task ImportedWithUpgradeOffFulfills() {
        await using var db = CreateContext();
        // Profile exists but upgrade is off → an imported book is simply fulfilled (the original behavior).
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable), upgradeOn: false);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task UpgradeAttemptsCapFulfills() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable), upgradeAttempts: 3);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task NotYetCapturedImportedStaysActiveAndNotDue() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable), captured: false);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Active, (await store.ListAsync(CancellationToken.None))[0].Status); // waits for capture, does not fulfill
    }

    [Fact]
    public async Task InFlightUpgradeChildBlocksAnotherUpgradeSearch() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable));
        var childId = await store.CreateUpgradeChildAsync((await store.ListAsync(CancellationToken.None))[0].Id, CancellationToken.None);
        // The child is mid-download (in flight).
        var child = await db.Acquisitions.FirstAsync(a => a.Id == childId);
        child.Status = AcquisitionStatus.Downloading;
        await db.SaveChangesAsync();

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None)); // interlock holds — no second upgrade
    }

    [Fact]
    public async Task SettledUpgradeChildReleasesTheInterlockAndCountsBarren() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable));
        var monitorId = (await store.ListAsync(CancellationToken.None))[0].Id;
        var childId = await store.CreateUpgradeChildAsync(monitorId, CancellationToken.None);
        // The child's search found nothing better and failed without ever reaching the replace handler.
        (await db.Acquisitions.FirstAsync(a => a.Id == childId)).Status = AcquisitionStatus.Failed;
        await db.SaveChangesAsync();

        // The sweep reconciles: clears the interlock and counts a barren search. Not due this pass (cooldown).
        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        var monitor = await db.Monitors.AsNoTracking().FirstAsync(m => m.Id == monitorId);
        Assert.Null(monitor.UpgradeChildAcquisitionId);
        Assert.Equal(1, monitor.BarrenSearches);
    }

    [Fact]
    public async Task CrashOrphanedDownloadedChildReleasesTheInterlock() {
        // The child reached Downloaded but the replace job was never enqueued/ran (a crash window). The sweep
        // must reclaim the interlock so the monitor is not frozen forever, counting it as a barren attempt.
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable));
        var monitorId = (await store.ListAsync(CancellationToken.None))[0].Id;
        var childId = await store.CreateUpgradeChildAsync(monitorId, CancellationToken.None);
        (await db.Acquisitions.FirstAsync(a => a.Id == childId)).Status = AcquisitionStatus.Downloaded;
        await db.SaveChangesAsync();

        await store.ListDueMonitorsAsync(360, CancellationToken.None);

        var monitor = await db.Monitors.AsNoTracking().FirstAsync(m => m.Id == monitorId);
        Assert.Null(monitor.UpgradeChildAcquisitionId); // interlock reclaimed — not stuck
        Assert.Equal(1, monitor.BarrenSearches);
    }

    [Fact]
    public async Task CreateUpgradeChildCopiesParentAndIsClaimedOnce() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable));
        var monitorId = (await store.ListAsync(CancellationToken.None))[0].Id;

        var childId = await store.CreateUpgradeChildAsync(monitorId, CancellationToken.None);
        Assert.NotNull(childId);
        var child = await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == childId);
        Assert.Equal("Some Book", child.Title);
        Assert.Equal(AcquisitionStatus.Pending, child.Status);
        Assert.NotNull(child.UpgradeOfAcquisitionId);

        // A second claim while the first is in flight returns null (one upgrade at a time).
        Assert.Null(await store.CreateUpgradeChildAsync(monitorId, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveUpgradeChildSuccessCountsAttemptAndResetsBarren() {
        await using var db = CreateContext();
        var store = await SeedUpgradeMonitorAsync(db, owned: new(BookSourceTier.Web, BookFormatTier.Reflowable), cutoff: new(BookSourceTier.Retail, BookFormatTier.Reflowable), barrenSearches: 2);
        var monitorId = (await store.ListAsync(CancellationToken.None))[0].Id;
        var childId = await store.CreateUpgradeChildAsync(monitorId, CancellationToken.None);

        await store.ResolveUpgradeChildAsync(childId!.Value, succeeded: true, CancellationToken.None);

        var monitor = await db.Monitors.AsNoTracking().FirstAsync(m => m.Id == monitorId);
        Assert.Null(monitor.UpgradeChildAcquisitionId);
        Assert.Equal(1, monitor.UpgradeAttempts);
        Assert.Equal(0, monitor.BarrenSearches); // a success resets the fruitless streak
    }

    [Fact]
    public async Task ImportedMovieBelowCutoffWithUpgradeOnIsDueAsUpgrade() {
        await using var db = CreateContext();
        var store = await SeedMediaUpgradeMonitorAsync(db, EntityKind.Movie, owned: "webdl-720p", cutoff: "bluray-1080p");

        var due = await store.ListDueMonitorsAsync(360, CancellationToken.None);

        var monitor = Assert.Single(due);
        Assert.True(monitor.IsUpgrade);
    }

    [Fact]
    public async Task ImportedMovieAtOrAboveCutoffFulfills() {
        await using var db = CreateContext();
        // Owned exactly at cutoff → fulfilled.
        var atCutoff = await SeedMediaUpgradeMonitorAsync(db, EntityKind.Movie, owned: "bluray-1080p", cutoff: "bluray-1080p");
        Assert.Empty(await atCutoff.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await atCutoff.ListAsync(CancellationToken.None))[0].Status);

        // Owned above cutoff → also fulfilled.
        await using var db2 = CreateContext();
        var aboveCutoff = await SeedMediaUpgradeMonitorAsync(db2, EntityKind.Movie, owned: "remux-2160p", cutoff: "bluray-1080p");
        Assert.Empty(await aboveCutoff.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await aboveCutoff.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task ImportedMovieWithUpgradeOffFulfills() {
        await using var db = CreateContext();
        var store = await SeedMediaUpgradeMonitorAsync(db, EntityKind.Movie, owned: "webdl-720p", cutoff: "bluray-1080p", upgradeOn: false);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task ImportedSeasonPackAlwaysFulfillsEvenBelowCutoff() {
        await using var db = CreateContext();
        // A season pack is multi-file — it captures owned quality but never upgrades; it fulfills on import.
        var store = await SeedMediaUpgradeMonitorAsync(db, EntityKind.VideoSeason, owned: "webdl-720p", cutoff: "bluray-1080p");

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task ImportedAlbumAlwaysFulfillsEvenBelowCutoff() {
        await using var db = CreateContext();
        var store = await SeedMediaUpgradeMonitorAsync(db, EntityKind.AudioLibrary, owned: "lossy", cutoff: "lossless");

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task ImportedMovieAtLadderCutoffButBelowFormatScoreCutoffStaysDue() {
        await using var db = CreateContext();
        // Ladder cutoff is met (owned == cutoff quality) but the format-score cutoff (500) is not — the
        // monitor keeps chasing a better-scoring release at the same quality instead of fulfilling.
        var store = await SeedMediaUpgradeMonitorAsync(
            db, EntityKind.Movie, owned: "bluray-1080p", cutoff: "bluray-1080p", cutoffFormatScore: 500, ownedFormatScore: 0);

        var due = await store.ListDueMonitorsAsync(360, CancellationToken.None);

        var monitor = Assert.Single(due);
        Assert.True(monitor.IsUpgrade);
    }

    [Fact]
    public async Task ImportedMovieAtBothCutoffsFulfills() {
        await using var db = CreateContext();
        // Both the ladder cutoff AND the format-score cutoff are met → fulfilled.
        var store = await SeedMediaUpgradeMonitorAsync(
            db, EntityKind.Movie, owned: "bluray-1080p", cutoff: "bluray-1080p", cutoffFormatScore: 500, ownedFormatScore: 500);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task ImportedMovieWithNoFormatCutoffFulfillsAtLadderCutoff() {
        await using var db = CreateContext();
        // No format-score cutoff configured (null) → the format score imposes no requirement; the ladder
        // cutoff alone decides, so an owned copy at the ladder cutoff fulfills even with a 0 format score.
        var store = await SeedMediaUpgradeMonitorAsync(
            db, EntityKind.Movie, owned: "bluray-1080p", cutoff: "bluray-1080p", cutoffFormatScore: null, ownedFormatScore: 0);

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Fulfilled, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    [Fact]
    public async Task ImportedMovieWithNoOwnedQualityCapturedStaysActive() {
        await using var db = CreateContext();
        // The captured flag is set but the ladder code was never recorded (no selected release) → the loop
        // can't judge the owned copy yet, so it waits (Active) rather than fulfilling too early.
        var store = await SeedMediaUpgradeMonitorAsync(db, EntityKind.Movie, owned: null, cutoff: "bluray-1080p");

        Assert.Empty(await store.ListDueMonitorsAsync(360, CancellationToken.None));
        Assert.Equal(MonitorStatus.Active, (await store.ListAsync(CancellationToken.None))[0].Status);
    }

    /// <summary>
    /// Seeds an imported media (movie/TV/music) monitor: a governing profile for the kind's profile family
    /// with the ladder cutoff-quality code, and an imported acquisition carrying the owned ladder code.
    /// </summary>
    private static async Task<EfMonitorStore> SeedMediaUpgradeMonitorAsync(
        PrismediaDbContext db,
        EntityKind kind,
        string? owned,
        string cutoff,
        bool upgradeOn = true,
        int? cutoffFormatScore = null,
        int ownedFormatScore = 0) {
        var now = DateTimeOffset.UtcNow;
        db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow {
            Id = Guid.NewGuid(), Kind = AcquisitionProfileKinds.For(kind), DisplayName = "Default", IsDefault = true,
            TargetLibraryRootId = Guid.NewGuid(), AutoPick = true, UpgradeUntilCutoff = upgradeOn, CutoffQuality = cutoff,
            CutoffFormatScore = cutoffFormatScore, CreatedAt = now, UpdatedAt = now
        });
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, Kind = kind, Status = AcquisitionStatus.Imported, Title = "Some Media", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            OwnedMediaQuality = owned, OwnedFormatScore = ownedFormatScore, UpgradeQualityCaptured = true, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);
        await store.StartAsync(acquisitionId, kind, "Some Media", null, CancellationToken.None);
        return store;
    }

    private static async Task<EfMonitorStore> SeedUpgradeMonitorAsync(
        PrismediaDbContext db,
        BookQualityRank owned,
        BookQualityRank cutoff,
        bool upgradeOn = true,
        bool captured = true,
        int upgradeAttempts = 0,
        int barrenSearches = 0) {
        var now = DateTimeOffset.UtcNow;
        db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow {
            Id = Guid.NewGuid(), DisplayName = "Default", IsDefault = true, TargetLibraryRootId = Guid.NewGuid(),
            AutoPick = true, UpgradeUntilCutoff = upgradeOn, CutoffSourceTier = cutoff.Source, CutoffFormatTier = cutoff.Format,
            CreatedAt = now, UpdatedAt = now
        });
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, Status = AcquisitionStatus.Imported, Title = "Some Book", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            OwnedSourceTier = owned.Source, OwnedFormatTier = owned.Format, UpgradeQualityCaptured = captured, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);
        await store.StartAsync(acquisitionId, EntityKind.Book, "Some Book", "Author", CancellationToken.None);
        var monitor = await db.Monitors.FirstAsync(m => m.AcquisitionId == acquisitionId);
        monitor.UpgradeAttempts = upgradeAttempts;
        monitor.BarrenSearches = barrenSearches;
        await db.SaveChangesAsync();
        return store;
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
