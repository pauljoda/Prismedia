using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Settings;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Settings;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

public sealed partial class HeldTvImportRecoveryTests {
    [Theory]
    [InlineData("Show S01E01 First Story 720p WEB-DL", "new-release", true, false)]
    [InlineData("Show S01E02 Second Story 1080p WEB-DL", "new-release", false, false)]
    [InlineData("Show S01E01 First Story 720p WEB-DL", "held-test", false, false)]
    [InlineData("Show S01E01 First Story 720p WEB-DL", "held-test", true, true)]
    public async Task RecoveryCandidatesMustImproveTheHeldMappingWithoutDiscardingIt(string title, string hash, bool accepted, bool missingPayload) {
        await using var db = CreateContext();
        var (_, held, episodes) = await SeedAsync(db);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        held.EntityId = episodes[0].Id;
        held.Kind = EntityKind.VideoEpisode;
        held.EpisodeNumber = 1;
        held.Title = "First Story";
        File.Delete(Path.Combine(root, "payload", "Show.S01E01E02.First.Story.Second.Story.mkv"));
        var retained = Path.Combine(root, "payload", "Show.S01E09.Unrelated.Story.mkv");
        await File.WriteAllTextAsync(retained, "retained review bytes");
        if (missingPayload) Directory.Delete(Path.GetDirectoryName(retained)!, true);
        var child = new AcquisitionRow { Id = Guid.NewGuid(), Kind = held.Kind, EntityId = held.EntityId,
            Title = held.Title, Series = held.Series, SeasonNumber = 1, EpisodeNumber = 1,
            RecoveryOfAcquisitionId = held.Id, Status = AcquisitionStatus.Searching };
        db.Acquisitions.Add(child);
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        var policy = new HeldAcquisitionCandidatePolicy(store, new EfImportTargetIndex(db), new DownloadPayloadReader(),
            new EfBookAcquisitionProfileStore(db), new SettingsService(new EfSettingsPersistence(db)));
        var release = new IndexerRelease(title, 1000, 10, 2, DownloadProtocol.Torrent,
            "https://indexer.test/release", null, hash, null, null, null);

        var result = await policy.FilterAsync((await store.GetSearchInputAsync(child.Id, default))!,
            [new(release, null, "Indexer", true, 100, [])], default);

        Assert.Equal(accepted, Assert.Single(result).Accepted);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, held.Status);
        Assert.Equal(!missingPayload, File.Exists(retained));
    }

    [Fact]
    public async Task AFileThatFailedVerificationIsNotAUsableCoverageBaseline() {
        await using var db = CreateContext();
        var (_, held, episodes) = await SeedAsync(db);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        held.Kind = EntityKind.VideoEpisode;
        held.EntityId = episodes[0].Id;
        held.EpisodeNumber = 1;
        held.Title = "First Story";
        var file = "Show.S01E01E02.First.Story.Second.Story.mkv";
        held.ImportResultJson = AcquisitionImportFileLedgerJson.Serialize(new(AcquisitionImportPhase.Imported,
            [new("damaged", file, 10, file, null, AcquisitionImportFileRole.Media, AcquisitionImportContentKind.Video,
                AcquisitionImportFileStatus.Skipped, AcquisitionImportDecision.HoldVerification, "Full decoding failed.")]));
        var child = new AcquisitionRow { Id = Guid.NewGuid(), Kind = held.Kind, EntityId = held.EntityId,
            Title = held.Title, Series = held.Series, SeasonNumber = 1, EpisodeNumber = 1,
            RecoveryOfAcquisitionId = held.Id, Status = AcquisitionStatus.Searching };
        db.Acquisitions.Add(child);
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        var policy = new HeldAcquisitionCandidatePolicy(store, new EfImportTargetIndex(db), new DownloadPayloadReader(),
            new EfBookAcquisitionProfileStore(db), new SettingsService(new EfSettingsPersistence(db)));
        var release = new IndexerRelease("Show S01E01 First Story 720p WEB-DL", 1000, 10, 2, DownloadProtocol.Torrent,
            "https://indexer.test/release", null, "replacement", null, null, null);
        var result = await policy.FilterAsync((await store.GetSearchInputAsync(child.Id, default))!,
            [new(release, null, "Indexer", true, 100, [])], default);
        Assert.True(Assert.Single(result).Accepted);
        Assert.True(File.Exists(Path.Combine(root, "payload", file)));
    }

    [Fact]
    public async Task RecoveryDoesNotReturnToAnOlderHeldRelease() {
        await using var db = CreateContext();
        var (_, held, episodes) = await SeedAsync(db);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        held.Kind = EntityKind.VideoEpisode;
        held.EntityId = episodes[0].Id;
        held.EpisodeNumber = 1;
        held.Title = "First Story";
        File.Delete(Path.Combine(root, "payload", "Show.S01E01E02.First.Story.Second.Story.mkv"));
        await File.WriteAllTextAsync(Path.Combine(root, "payload", "Show.S01E09.Unrelated.Story.mkv"), "held bytes");
        var older = new AcquisitionRow { Id = Guid.NewGuid(), Kind = held.Kind, EntityId = held.EntityId,
            Status = AcquisitionStatus.ManualImportRequired, Title = held.Title, SelectedReleaseJson =
                System.Text.Json.JsonSerializer.Serialize(new SelectedRelease("Show S01E01 First Story 1080p WEB-DL", "Indexer", "older-held")) };
        held.RecoveryOfAcquisitionId = older.Id;
        var child = new AcquisitionRow { Id = Guid.NewGuid(), Kind = held.Kind, EntityId = held.EntityId,
            Title = held.Title, Series = held.Series, SeasonNumber = 1, EpisodeNumber = 1,
            RecoveryOfAcquisitionId = held.Id, Status = AcquisitionStatus.Searching };
        db.Acquisitions.AddRange(older, child);
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        var policy = new HeldAcquisitionCandidatePolicy(store, new EfImportTargetIndex(db), new DownloadPayloadReader(),
            new EfBookAcquisitionProfileStore(db), new SettingsService(new EfSettingsPersistence(db)));
        var release = new IndexerRelease("Show S01E01 First Story 1080p WEB-DL", 1000, 10, 2, DownloadProtocol.Torrent,
            "https://indexer.test/release", null, "older-held", null, null, null);
        var result = await policy.FilterAsync((await store.GetSearchInputAsync(child.Id, default))!,
            [new(release, null, "Indexer", true, 100, [])], default);
        Assert.False(Assert.Single(result).Accepted);
    }

    [Fact]
    public async Task HeldPayloadWaitsWhileItsAlternativeIsDownloading() {
        await using var db = CreateContext();
        var (recovery, held, episodes) = await SeedAsync(db);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        var monitor = await db.Monitors.SingleAsync();
        var child = new AcquisitionRow { Id = Guid.NewGuid(), Kind = held.Kind, EntityId = held.EntityId,
            Status = AcquisitionStatus.Downloading, Title = held.Title, RecoveryOfAcquisitionId = held.Id };
        db.Acquisitions.Add(child);
        monitor.AcquisitionId = child.Id;
        await db.SaveChangesAsync();
        await recovery.RecoverAsync(default);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, held.Status);
        child.Status = AcquisitionStatus.AwaitingSelection;
        await db.SaveChangesAsync();
        await recovery.RecoverAsync(default);
        Assert.Equal(AcquisitionStatus.Downloaded, held.Status);
    }

    [Fact]
    public async Task RecoveryReplacementKeepsUnresolvedHeldCopiesVisibleForReview() {
        await using var db = CreateContext();
        var (_, held, episodes) = await SeedAsync(db);
        held.Kind = EntityKind.VideoEpisode;
        held.EntityId = episodes[0].Id;
        held.EpisodeNumber = 1;
        episodes[0].IsWanted = false;
        var child = new AcquisitionRow { Id = Guid.NewGuid(), Kind = held.Kind, EntityId = held.EntityId,
            Status = AcquisitionStatus.Importing, Title = held.Title, RecoveryOfAcquisitionId = held.Id };
        db.Acquisitions.Add(child);
        db.EntityFiles.Add(new EntityFileRow { Id = Guid.NewGuid(), EntityId = episodes[0].Id,
            Role = EntityFileRole.Source, Path = Path.Combine(root, "owned.mkv") });
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        await store.MarkImportedWithQualityAsync(child.Id, default, null, default);
        await db.Entry(held).ReloadAsync();
        Assert.Equal(AcquisitionStatus.ManualImportRequired, held.Status);
        Assert.Contains(await store.ListAsync(default), row => row.Id == held.Id);
    }

    [Fact]
    public async Task RecoveryRetryAndRemovalPreserveOlderRetainedAncestors() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (_, held, episodes) = await SeedAsync(db);
        held.EntityId = episodes[0].Id;
        held.Kind = EntityKind.VideoEpisode;
        var ancestor = new AcquisitionRow { Id = Guid.NewGuid(), Kind = held.Kind, EntityId = held.EntityId,
            Status = AcquisitionStatus.ManualImportRequired, Title = "Older held release" };
        db.Acquisitions.Add(ancestor);
        held.RecoveryOfAcquisitionId = ancestor.Id;
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        var cloneId = (await store.CloneForRetryAsync(held.Id, default))!.Value;
        var clone = await db.Acquisitions.SingleAsync(row => row.Id == cloneId);
        Assert.Equal(ancestor.Id, clone.RecoveryOfAcquisitionId);
        clone.RecoveryOfAcquisitionId = held.Id;
        await db.SaveChangesAsync();
        Assert.True(await store.DeleteAsync(held.Id, default));
        await db.Entry(clone).ReloadAsync();
        Assert.Equal(ancestor.Id, clone.RecoveryOfAcquisitionId);
        Assert.True(await db.Acquisitions.AnyAsync(row => row.Id == ancestor.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersededHeldCleanupRequiresImportedPresentReplacementAndPreservesReview(bool missingHeldPayload) {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (_, held, episodes) = await SeedAsync(db);
        var monitor = await db.Monitors.SingleAsync();
        held.Kind = monitor.Kind = EntityKind.VideoEpisode;
        held.EntityId = monitor.EntityId = episodes[0].Id;
        held.EpisodeNumber = 1;
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        var alternatives = new EfHeldAcquisitionAlternativeService(db, store, new EfMonitorStore(db));
        var childId = (await alternatives.CreateAsync(new(monitor.Id, held.Id, held.Title, held.Kind,
            EntityId: held.EntityId), default))!.Value;
        var cleanup = new EfSupersededHeldAcquisitionStore(db, store, new EfMonitorStore(db), new EfImportTargetIndex(db), new DownloadPayloadReader());
        var candidate = new SupersededHeldAcquisition(held.Id, childId);
        Assert.Empty(await cleanup.ListAsync(default));
        Assert.False(await cleanup.TryClaimAsync(candidate, default));
        var child = await db.Acquisitions.SingleAsync(row => row.Id == childId);
        child.Status = AcquisitionStatus.Imported;
        child.SelectedReleaseJson = System.Text.Json.JsonSerializer.Serialize(new SelectedRelease("Show S01E01 First Story 1080p WEB-DL", "Indexer", "replacement"));
        child.FinalSourcePath = Path.Combine(root, "library", "Show.S01E01.mkv");
        db.EntityFiles.Add(new EntityFileRow { Id = Guid.NewGuid(), EntityId = episodes[0].Id,
            Role = EntityFileRole.Source, Path = child.FinalSourcePath });
        await db.SaveChangesAsync();
        Assert.False(await cleanup.TryClaimAsync(candidate, default));
        Directory.CreateDirectory(Path.GetDirectoryName(child.FinalSourcePath)!);
        await File.WriteAllTextAsync(child.FinalSourcePath, "verified replacement bytes");
        held.ImportManualReview = true;
        await db.SaveChangesAsync();
        Assert.False(await cleanup.TryClaimAsync(candidate, default));
        held.ImportManualReview = false;
        await db.SaveChangesAsync();
        monitor.Status = MonitorStatus.Paused;
        await db.SaveChangesAsync();
        Assert.False(await cleanup.TryClaimAsync(candidate, default));
        monitor.Status = MonitorStatus.Active;
        await db.SaveChangesAsync();
        var extra = Path.Combine(root, "payload", "Show.S02E01.Unknown.Story.mkv");
        await File.WriteAllTextAsync(extra, "foreign season review bytes");
        Assert.False(await cleanup.TryClaimAsync(candidate, default));
        File.Delete(extra);
        child.ImportManualReview = true;
        await db.SaveChangesAsync();
        Assert.False(await cleanup.TryClaimAsync(candidate, default));
        child.ImportManualReview = false;
        await db.SaveChangesAsync();
        // The old combined file still offers another missing episode. Importing only this
        // replacement must not erase that coverage; a completely wrong held file can be discarded.
        Assert.False(await cleanup.TryClaimAsync(candidate, default));
        var heldName = "Show.S01E01E02.First.Story.Second.Story.mkv";
        File.Move(Path.Combine(root, "payload", heldName), Path.Combine(root, "payload", "Show.S01E09.Unrelated.Story.mkv"));
        if (missingHeldPayload) Directory.Delete(Path.Combine(root, "payload"), true);
        Assert.Equal(candidate, Assert.Single(await cleanup.ListAsync(default)));
        Assert.True(await cleanup.TryClaimAsync(candidate, default));
        await db.Entry(held).ReloadAsync();
        Assert.Equal(AcquisitionStatus.Stopping, held.Status);
        Assert.Equal(AcquisitionTeardownIntent.Remove, held.TeardownIntent);
        Assert.True(File.Exists(child.FinalSourcePath));
        Assert.Equal(!missingHeldPayload, File.Exists(Path.Combine(root, "payload", "Show.S01E09.Unrelated.Story.mkv")));
        Assert.True((await store.GetTeardownClaimAsync(held.Id, default))!.SupersededHeldDownload);
        Assert.True(await cleanup.TryClaimAsync(candidate, default));
    }

    [Fact]
    public async Task RecoverySearchesBackOffDurablyAndPreserveExplicitReview() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (_, held, episodes) = await SeedAsync(db);
        var monitor = await db.Monitors.SingleAsync();
        held.Kind = monitor.Kind = EntityKind.VideoEpisode;
        held.EntityId = monitor.EntityId = episodes[0].Id;
        held.EpisodeNumber = 1;
        held.UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2);
        monitor.LastSearchedAt = held.UpdatedAt;
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        var service = new EfHeldAcquisitionAlternativeService(db, store, new EfMonitorStore(db));
        var due = new DueMonitor(monitor.Id, held.Id, held.Title, held.Kind, EntityId: held.EntityId) { HeldAlternativeRequired = true };
        held.ImportManualReview = true;
        await db.SaveChangesAsync();
        Assert.Null(await service.CreateAsync(due, default));
        held.ImportManualReview = false;
        await db.SaveChangesAsync();
        var childId = (await service.CreateAsync(due, default))!.Value;
        await store.SetStatusAsync(childId, AcquisitionStatus.Searching, null, default);
        Assert.True(await store.TryCompleteSearchAsync(childId, [], null, default));
        var input = (await store.GetSearchInputAsync(childId, default))!;
        await service.RecordSearchAsync(input, new([], []), default);
        await service.RecordSearchAsync(input, new([], []), default);
        await db.Entry(monitor).ReloadAsync();
        Assert.Equal(1, monitor.BarrenSearches);
        monitor.LastSearchedAt = DateTimeOffset.UtcNow.AddHours(-7);
        await db.SaveChangesAsync();
        Assert.Empty(await new EfMonitorStore(db).ListDueMonitorsAsync(60, default));
        monitor.LastSearchedAt = DateTimeOffset.UtcNow.AddHours(-13);
        await db.SaveChangesAsync();
        Assert.Equal(childId, Assert.Single(await new EfMonitorStore(db).ListDueMonitorsAsync(60, default)).AcquisitionId);
        monitor.BarrenSearches = 99;
        monitor.LastSearchedAt = DateTimeOffset.UtcNow.AddDays(-8);
        await db.SaveChangesAsync();
        Assert.Single(await new EfMonitorStore(db).ListDueMonitorsAsync(60, default));
        Assert.True(File.Exists(Path.Combine(root, "payload", "Show.S01E01E02.First.Story.Second.Story.mkv")));
    }

    [Fact]
    public async Task RecoveryCreatesOneIndependentAttemptAndKeepsTheOriginalPayload() {
        await using var db = CreateContext();
        var (_, acquisition, episodes) = await SeedAsync(db);
        var monitor = await db.Monitors.SingleAsync();
        acquisition.EntityId = episodes[0].Id;
        acquisition.Kind = EntityKind.VideoEpisode;
        acquisition.EpisodeNumber = 1;
        monitor.EntityId = episodes[0].Id;
        monitor.Kind = EntityKind.VideoEpisode;
        await db.SaveChangesAsync();
        var due = new DueMonitor(monitor.Id, acquisition.Id, acquisition.Title, acquisition.Kind,
            EntityId: acquisition.EntityId) { HeldAlternativeRequired = true };
        var service = new EfHeldAcquisitionAlternativeService(db, AcquisitionTestFactory.Store(db), new EfMonitorStore(db));

        var alternativeId = await service.CreateAsync(due, default);

        Assert.NotNull(alternativeId);
        Assert.NotEqual(acquisition.Id, alternativeId);
        Assert.Equal(alternativeId, monitor.AcquisitionId);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        Assert.Equal(acquisition.Id, (await db.DownloadTransfers.SingleAsync()).AcquisitionId);
        Assert.Null(await service.CreateAsync(due, default));
        Assert.Equal(2, await db.Acquisitions.CountAsync());
    }

    [Fact]
    public async Task UnattendedHeldEpisodesBecomeDueWithoutLosingTheirRetainedTransfer() {
        await using var db = CreateContext();
        var (_, acquisition, episodes) = await SeedAsync(db);
        var monitor = await db.Monitors.SingleAsync();
        acquisition.EntityId = episodes[0].Id;
        acquisition.Kind = EntityKind.VideoEpisode;
        acquisition.EpisodeNumber = 1;
        acquisition.UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2);
        monitor.EntityId = episodes[0].Id;
        monitor.Kind = EntityKind.VideoEpisode;
        monitor.LastSearchedAt = DateTimeOffset.UtcNow.AddDays(-2);
        await db.SaveChangesAsync();

        var due = await new EfMonitorStore(db).ListDueMonitorsAsync(60, default);

        Assert.Equal(acquisition.Id, Assert.Single(due).AcquisitionId);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        Assert.Single(await db.DownloadTransfers.ToArrayAsync());
        Assert.True(File.Exists(Path.Combine(root, "payload", "Show.S01E01E02.First.Story.Second.Story.mkv")));
    }
}
