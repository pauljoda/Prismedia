using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Covers the upgrade-replace orchestration: a successful swap records the new owned quality, retains the
/// consumed child until required Entity readiness finalizes it, and enqueues exact reconciliation; an aborted swap
/// (e.g. no longer an upgrade, or the replacer refused) leaves the owned book untouched and counts barren.
/// </summary>
public sealed class AcquisitionUpgradeReplaceJobHandlerTests {
    [Theory]
    [InlineData(EntityKind.Book, false, false)]
    [InlineData(EntityKind.Book, true, false)]
    [InlineData(EntityKind.Movie, false, false)]
    [InlineData(EntityKind.Movie, true, false)]
    [InlineData(EntityKind.VideoEpisode, false, false)]
    [InlineData(EntityKind.VideoEpisode, true, false)]
    [InlineData(EntityKind.Movie, false, true)]
    [InlineData(EntityKind.Movie, true, true)]
    public async Task InstalledUpgradeRetriesReadinessWithoutReplacingAgain(EntityKind kind, bool cleanupAlreadyPreserved, bool postgres) {
        await using var database = postgres ? await PostgresTestDatabase.CreateAsync() : null;
        await using var db = database?.CreateContext() ?? CreateContext();
        var (parentId, childId, _) = kind == EntityKind.Book ? await SeedAsync(db, "Some Book (retail) (epub)")
            : await SeedMediaAsync(db, kind, VideoQuality.Webdl720p.ToCode(), "Movie 2020 1080p WEB-DL");
        var folder = Directory.CreateTempSubdirectory("prismedia-upgrade-ready-").FullName;
        try {
            var installed = Path.Combine(folder, kind == EntityKind.Book ? "book.epub" : "video.mkv");
            await File.WriteAllTextAsync(installed, "installed-upgrade-bytes");
            var parent = (await db.Acquisitions.FindAsync(parentId))!;
            parent.FinalSourcePath = installed;
            var sourceId = Guid.NewGuid();
            db.EntityFiles.Add(new EntityFileRow { Id = sourceId, EntityId = parent.EntityId!.Value,
                Role = EntityFileRole.Source, Path = installed, Source = FileSourceKind.Scan.ToCode() });
            await db.SaveChangesAsync();
            var firstQueue = new RecordingJobQueue {
                BeforeEnqueue = postgres ? async () => {
                    await using var observer = database!.CreateContext();
                    Assert.Null((await observer.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == childId)).FinalSourcePath);
                    Assert.Equal(VideoQuality.Webdl720p.ToCode(), (await observer.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == parentId)).OwnedMediaQuality);
                } : null
            };
            await RunAsync(db, firstQueue, new FakeReplacer(OwnedFileReplaceResult.Ok(installed,
                    kind == EntityKind.Book ? BookFormatTier.Reflowable : BookFormatTier.Unknown)), childId,
                new FakeMediaUpgradePayloadInspector(new(720, 1080, false, false, 1200, 1200)));
            Assert.Equal(installed, (await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == childId)).FinalSourcePath);
            if (cleanupAlreadyPreserved) Assert.True(await new EfDetachedDownloadCleanupStore(db).PreserveUpgradeAsync(childId, default));
            var store = AcquisitionTestFactory.Store(db);
            await store.SetStatusAsync(childId, AcquisitionStatus.Failed, "Required processing interrupted", default);
            Assert.True((await store.GetAsync(childId, default))!.Summary.HasResumableImport);
            var installedImport = (await store.GetImportContextAsync(childId, default))!;
            Assert.False(await ImportCheckpointLifecycle.CanAbandonAsync(installedImport, default));
            Assert.False(await ImportCheckpointLifecycle.TryAbandonAsync(store, installedImport, default));
            Assert.False(await store.TryClaimFailedRecoveryAsync(childId, [AcquisitionStatus.Failed],
                await store.GetSelectedReleaseAsync(childId, default), "Stale download failure", default));
            Assert.True(await store.TryTransitionStatusAsync(childId, [AcquisitionStatus.Failed], AcquisitionStatus.Downloaded, "Retry", default));
            var resumed = new RecordingJobQueue();
            var replacer = new FakeReplacer(OwnedFileReplaceResult.Failed("The downloaded primary was already consumed"));

            await RunAsync(db, resumed, replacer, childId, new FakeMediaUpgradePayloadInspector(null));

            Assert.False(replacer.Called);
            Assert.Single(resumed.Enqueued, request => request.Type == JobType.ReconcileEntity);
            Assert.Equal(AcquisitionStatus.Importing, (await db.Acquisitions.FindAsync(childId))!.Status);
            Assert.Single(await db.AcquisitionHistory.Where(row => row.Event == AcquisitionHistoryEvent.Upgraded).ToArrayAsync());
            Assert.Equal(installed, (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.Id == sourceId)).Path);
            Assert.Equal("installed-upgrade-bytes", await File.ReadAllTextAsync(installed));
            await FinalizeAsync(db, resumed);
            Assert.False(await db.Acquisitions.AnyAsync(row => row.Id == childId));
            Assert.Single(await db.DetachedDownloadCleanups.ToArrayAsync());
        } finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("missing file")]
    [InlineData("missing source")]
    [InlineData("changed source path")]
    public async Task InstalledUpgradeWithChangedOwnershipHoldsWithoutAnotherSwap(string change) {
        await using var db = CreateContext();
        var (parentId, childId, _) = await SeedMediaAsync(db, EntityKind.Movie, VideoQuality.Webdl1080p.ToCode(), "Movie 2020 1080p WEB-DL");
        var folder = Directory.CreateTempSubdirectory("prismedia-upgrade-changed-").FullName;
        try {
            var installed = Path.Combine(folder, "video.mkv");
            if (change != "missing file") await File.WriteAllTextAsync(installed, "installed-upgrade-bytes");
            var parent = (await db.Acquisitions.FindAsync(parentId))!;
            parent.FinalSourcePath = installed;
            (await db.Acquisitions.FindAsync(childId))!.FinalSourcePath = installed;
            if (change != "missing source") db.EntityFiles.Add(new EntityFileRow { Id = Guid.NewGuid(), EntityId = parent.EntityId!.Value,
                Role = EntityFileRole.Source, Path = change == "changed source path" ? Path.Combine(folder, "different.mkv") : installed });
            await db.SaveChangesAsync();
            var queue = new RecordingJobQueue();
            var replacer = new FakeReplacer(OwnedFileReplaceResult.Failed("Must not swap"));

            await RunAsync(db, queue, replacer, childId, new FakeMediaUpgradePayloadInspector(new(1080, 1080, false, false, 1200, 1200)));

            Assert.False(replacer.Called);
            Assert.Empty(queue.Enqueued);
            Assert.Equal(AcquisitionStatus.ManualImportRequired, (await db.Acquisitions.FindAsync(childId))!.Status);
            Assert.True(await db.DownloadTransfers.AnyAsync(row => row.AcquisitionId == childId));
        } finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASharedEpisodeFileCannotBeReplacedByAnAtomicUpgrade(bool manualPick) {
        await using var db = CreateContext();
        var (parentId, childId, _) = await SeedMediaAsync(db, EntityKind.VideoEpisode,
            VideoQuality.Webdl720p.ToCode(), "Show S01E01 1080p WEB-DL", manualPick);
        await ShareOwnedFileAsync(db, parentId);
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok("/library/episode.mkv", BookFormatTier.Unknown));

        await RunAsync(db, new RecordingJobQueue(), replacer, childId,
            new FakeMediaUpgradePayloadInspector(new(720, 1080, true, true, 1200, 1200)));

        Assert.False(replacer.Called);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, (await db.Acquisitions.FindAsync(childId))!.Status);
        Assert.Equal(2, await db.EntityFiles.CountAsync());
    }

    [Fact]
    public async Task ASharedEpisodeFileDoesNotStartAnAtomicUpgradeSearch() {
        await using var db = CreateContext();
        var (parentId, childId, monitorId) = await SeedMediaAsync(db, EntityKind.VideoEpisode,
            VideoQuality.Webdl720p.ToCode(), "Show S01E01 1080p WEB-DL");
        await ShareOwnedFileAsync(db, parentId);
        (await db.Acquisitions.FindAsync(childId))!.Status = AcquisitionStatus.Cancelled;
        (await db.Monitors.FindAsync(monitorId))!.UpgradeChildAcquisitionId = null;
        await db.SaveChangesAsync();

        var acquisitions = AcquisitionTestFactory.Store(db);
        var owned = await acquisitions.GetUpgradeOwnedQualityAsync(childId, default);
        var rules = AcquisitionRuleContext.Apply(
            await new EfBookAcquisitionProfileStore(db).GetRulesAsync(null, EntityKind.VideoEpisode, default),
            (await acquisitions.GetSearchInputAsync(childId, default))!, owned, ProperDownloadPolicy.PreferAndUpgrade, [DownloadProtocol.Usenet]);
        var release = new IndexerRelease("Show S01E01 1080p WEB-DL", 1_000_000, null, null,
            DownloadProtocol.Usenet, null, null, null, null, null, null);
        Assert.Equal(ReleaseRejectionReason.NotAnUpgrade, new MediaUpgradeSpecification(EntityKind.VideoEpisode).Evaluate(release, rules));
        Assert.Null(await new EfMonitorStore(db).CreateUpgradeChildAsync(monitorId, default));
        Assert.Equal(2, await db.Acquisitions.CountAsync());
    }

    private static async Task ShareOwnedFileAsync(PrismediaDbContext db, Guid parentId) {
        var parent = (await db.Acquisitions.FindAsync(parentId))!;
        var now = DateTimeOffset.UtcNow;
        foreach (var owner in new[] { parent.EntityId!.Value, Guid.NewGuid() }) {
            db.EntityFiles.Add(new EntityFileRow { Id = Guid.NewGuid(), EntityId = owner,
                Role = EntityFileRole.Source, Path = parent.FinalSourcePath + "/episode.mkv",
                Source = FileSourceKind.Scan.ToCode(), CreatedAt = now, UpdatedAt = now });
        }
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(VideoQuality.Unknown, 1080, 1080, false, false)]
    [InlineData(VideoQuality.Unknown, 720, 1080, false, true)]
    [InlineData(VideoQuality.Webdl720p, 1080, 1080, false, false)]
    [InlineData(VideoQuality.Bluray2160p, 720, 1080, false, true)]
    [InlineData(VideoQuality.Unknown, 1080, 1080, true, true)]
    public async Task ReplacementUsesMeasuredDominanceWhenTheOwnedQualityLabelIsUnknownOrStale(
        VideoQuality ownedLabel, int ownedResolution, int candidateResolution, bool addsSubtitles, bool replace) {
        await using var db = CreateContext();
        var (_, childId, _) = await SeedMediaAsync(db, EntityKind.Movie, ownedLabel.ToCode(),
            addsSubtitles ? "Movie 2020 1080p WEB-DL MULTISUB" : "Movie 2020 1080p WEB-DL");
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok("/library/Movie (2020)/Movie (2020).mkv", BookFormatTier.Unknown));

        await RunAsync(db, new RecordingJobQueue(), replacer, childId,
            new FakeMediaUpgradePayloadInspector(new(ownedResolution, candidateResolution,
                false, addsSubtitles, 7200, 7200)));

        Assert.Equal(replace, replacer.Called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulSwapRetainsItsTransferUntilRequiredReadinessFinalizes(bool book) {
        await using var db = CreateContext();
        var (_, childId, _) = book ? await SeedAsync(db, "Some Book (retail) (epub)")
            : await SeedMediaAsync(db, EntityKind.Movie, VideoQuality.Webdl720p.ToCode(), "Movie 2020 1080p WEB-DL");
        var transfer = await db.DownloadTransfers.SingleAsync(row => row.AcquisitionId == childId);
        var client = new RecordingDownloadClient();
        var detail = new DownloadClientDetail(transfer.DownloadClientConfigId!.Value, DownloadClientKind.QBittorrent,
            "Downloads", "http://download-client", null, "prismedia", true, false, null);
        var queue = new RecordingJobQueue();
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok(book ? "/library/Book.epub" : "/library/Movie.mkv",
            book ? BookFormatTier.Reflowable : BookFormatTier.Unknown));

        await RunAsync(db, queue, replacer, childId, new FakeMediaUpgradePayloadInspector(new(720, 1080, false, false, 7200, 7200)),
            new SingleDownloadClientConfigStore(detail), new SingleDownloadClientFactory(client));

        Assert.True(replacer.Called);
        Assert.Null(client.RemovedClientItemId);
        Assert.True(await db.DownloadTransfers.AnyAsync(row => row.Id == transfer.Id));
        Assert.Empty(await db.DetachedDownloadCleanups.ToArrayAsync());
        Assert.Equal(AcquisitionStatus.Importing, (await db.Acquisitions.FindAsync(childId))!.Status);
        await FinalizeAsync(db, queue);
        Assert.False(await db.DownloadTransfers.AnyAsync(row => row.Id == transfer.Id));
        Assert.Equal(transfer.Id, Assert.Single(await db.DetachedDownloadCleanups.ToArrayAsync()).Id);
        Assert.Null(client.RemovedClientItemId);
    }

    [Fact]
    public async Task SuccessfulUpgradePreservesCleanupOwnershipWhenItsDownloaderIsUnavailable() {
        await using var db = CreateContext();
        var (parentId, childId, _) = await SeedMediaAsync(db, EntityKind.Movie,
            ownedCode: "bluray-1080p", childSelectedTitle: "Movie 2020 2160p BluRay");
        var clientId = Guid.NewGuid();
        db.DownloadClientConfigs.Add(new DownloadClientConfigRow { Id = clientId, Kind = DownloadClientKind.Sabnzbd,
            DisplayName = "Downloads", BaseUrl = "http://client", Category = "validation" });
        var transfer = await db.DownloadTransfers.SingleAsync(row => row.AcquisitionId == childId);
        transfer.DownloadClientConfigId = clientId;
        transfer.ContentPath = "/downloads/upgrade";
        await db.SaveChangesAsync();
        var queue = new RecordingJobQueue();

        await RunAsync(db, queue, new FakeReplacer(OwnedFileReplaceResult.Ok("/library/Film.mkv", BookFormatTier.Unknown)),
            childId, new FakeMediaUpgradePayloadInspector(new(OwnedResolutionTier: 1080, CandidateResolutionTier: 2160,
                OwnedHasSubtitles: false, CandidateHasSubtitles: false, OwnedDurationSeconds: 7200, CandidateDurationSeconds: 7200)));
        await FinalizeAsync(db, queue);

        Assert.False(await db.Acquisitions.AnyAsync(row => row.Id == childId));
        var cleanup = Assert.Single(await db.DetachedDownloadCleanups.ToArrayAsync());
        Assert.Equal(clientId, cleanup.DownloadClientConfigId);
        Assert.Equal("hash", cleanup.ClientItemId);
        Assert.Equal("/downloads/upgrade", cleanup.ContentPath);
        Assert.Equal((await db.Acquisitions.FindAsync(parentId))!.FinalSourcePath, cleanup.ImportedSourcePath);
    }

    [Fact]
    public async Task SuccessfulSwapUpdatesOwnedQualityAndReadinessFinalizerConsumesTheChild() {
        await using var db = CreateContext();
        var (parentId, childId, monitorId) = await SeedAsync(db, childSelectedTitle: "Some Book (retail) (epub)");
        var queue = new RecordingJobQueue();

        await RunAsync(db, queue, new FakeReplacer(OwnedFileReplaceResult.Ok("/library/Some Book/Book.epub", BookFormatTier.Reflowable)), childId);

        var parent = await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == parentId);
        Assert.Equal(BookSourceTier.Retail, parent.OwnedSourceTier); // upgraded source recorded
        Assert.Equal(BookFormatTier.Reflowable, parent.OwnedFormatTier);
        Assert.Equal(
            AcquisitionStatus.Importing,
            (await db.Acquisitions.AsNoTracking().SingleAsync(a => a.Id == childId)).Status);
        var monitor = await db.Monitors.AsNoTracking().FirstAsync(m => m.Id == monitorId);
        Assert.Equal(childId, monitor.UpgradeChildAcquisitionId);
        Assert.DoesNotContain(queue.Enqueued, job => job.Type == JobType.ScanBook);
        Assert.Contains(queue.Enqueued, job => job.Type == JobType.ReconcileEntity);

        await FinalizeAsync(db, queue);

        Assert.False(await db.Acquisitions.AsNoTracking().AnyAsync(a => a.Id == childId));
        monitor = await db.Monitors.AsNoTracking().FirstAsync(m => m.Id == monitorId);
        Assert.Null(monitor.UpgradeChildAcquisitionId);
        Assert.Equal(1, monitor.UpgradeAttempts);
    }

    [Fact]
    public async Task ReplacerRefusalAbortsAndCountsBarrenLeavingOwnedUntouched() {
        await using var db = CreateContext();
        var (parentId, childId, monitorId) = await SeedAsync(db, childSelectedTitle: "Some Book (retail) (epub)");
        var queue = new RecordingJobQueue();

        await RunAsync(db, queue, new FakeReplacer(OwnedFileReplaceResult.Failed("format change needs manual replacement")), childId);

        var parent = await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == parentId);
        Assert.Equal(BookSourceTier.Web, parent.OwnedSourceTier); // unchanged — owned book untouched
        Assert.Equal(AcquisitionStatus.Failed, (await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == childId)).Status);
        var monitor = await db.Monitors.AsNoTracking().FirstAsync(m => m.Id == monitorId);
        Assert.Null(monitor.UpgradeChildAcquisitionId);
        Assert.Equal(1, monitor.BarrenSearches);
        Assert.DoesNotContain(queue.Enqueued, job => job.Type == JobType.ScanBook);
    }

    [Fact]
    public async Task NoLongerAnUpgradeAbortsBeforeTouchingFiles() {
        await using var db = CreateContext();
        // The child's release is only equal to (not better than) the owned quality → must not swap.
        var (_, childId, _) = await SeedAsync(db, childSelectedTitle: "Some Book (web) (epub)");
        var queue = new RecordingJobQueue();
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok("x", BookFormatTier.Reflowable));

        await RunAsync(db, queue, replacer, childId);

        Assert.False(replacer.Called); // bailed before invoking the destructive swap
    }

    [Fact]
    public async Task ReviewedBookReplacementMayBeEqualWithoutErasingKnownSourceQuality() {
        await using var db = CreateContext();
        var (parentId, childId, _) = await SeedAsync(
            db,
            childSelectedTitle: "manually-uploaded.epub",
            manualPick: true);
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok("/library/Some Book/Book.epub", BookFormatTier.Reflowable));

        await RunAsync(db, new RecordingJobQueue(), replacer, childId);

        Assert.True(replacer.Called);
        var parent = await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == parentId);
        Assert.Equal(BookSourceTier.Web, parent.OwnedSourceTier);
        Assert.Equal(BookFormatTier.Reflowable, parent.OwnedFormatTier);
    }

    [Fact]
    public async Task MovieSuccessfulSwapRecordsOwnedLadderCodeAndFinalizerConsumesTheChild() {
        await using var db = CreateContext();
        var (parentId, childId, monitorId) = await SeedMediaAsync(db, EntityKind.Movie, ownedCode: "webdl-720p", childSelectedTitle: "Movie 2020 1080p BluRay");
        var queue = new RecordingJobQueue();
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok("/library/Movie (2020)/Movie (2020).mkv", BookFormatTier.Unknown));

        await RunAsync(db, queue, replacer, childId);

        Assert.Equal(EntityKind.Movie, replacer.CalledWithKind); // routed through the video replace path
        var parent = await db.Acquisitions.AsNoTracking().FirstAsync(a => a.Id == parentId);
        Assert.Equal("bluray-1080p", parent.OwnedMediaQuality); // upgraded ladder code recorded
        Assert.Equal(
            AcquisitionStatus.Importing,
            (await db.Acquisitions.AsNoTracking().SingleAsync(a => a.Id == childId)).Status);
        var monitor = await db.Monitors.AsNoTracking().FirstAsync(m => m.Id == monitorId);
        Assert.Equal(childId, monitor.UpgradeChildAcquisitionId);
        Assert.DoesNotContain(queue.Enqueued, job => job.Type == JobType.ScanLibrary);
        Assert.Contains(queue.Enqueued, job => job.Type == JobType.ReconcileEntity);

        await FinalizeAsync(db, queue);

        Assert.False(await db.Acquisitions.AsNoTracking().AnyAsync(a => a.Id == childId));
        monitor = await db.Monitors.AsNoTracking().FirstAsync(m => m.Id == monitorId);
        Assert.Null(monitor.UpgradeChildAcquisitionId);
        Assert.Equal(1, monitor.UpgradeAttempts);
    }

    [Fact]
    public async Task MovieNoLongerAnUpgradeAbortsBeforeTouchingFiles() {
        await using var db = CreateContext();
        // The child's release is only equal to (not better than) the owned ladder code → must not swap.
        var (_, childId, _) = await SeedMediaAsync(db, EntityKind.Movie, ownedCode: "bluray-1080p", childSelectedTitle: "Movie 2020 1080p BluRay");
        var queue = new RecordingJobQueue();
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok("x", BookFormatTier.Unknown));

        await RunAsync(db, queue, replacer, childId);

        Assert.False(replacer.Called); // bailed before invoking the destructive swap
    }

    [Fact]
    public async Task SameQualityMovieWithNewEmbeddedSubtitlesReplacesOwnedCopy() {
        await using var db = CreateContext();
        var (_, childId, _) = await SeedMediaAsync(
            db,
            EntityKind.Movie,
            ownedCode: "bluray-1080p",
            childSelectedTitle: "Movie 2020 1080p BluRay");
        var replacer = new FakeReplacer(
            OwnedFileReplaceResult.Ok("/library/Movie (2020)/Movie (2020).mkv", BookFormatTier.Unknown));

        await RunAsync(
            db,
            new RecordingJobQueue(),
            replacer,
            childId,
            new FakeMediaUpgradePayloadInspector(new(1080, 1080, false, true, 7200, 7200)));

        Assert.True(replacer.Called);
    }

    [Fact]
    public async Task SameQualitySubtitlePayloadDoesNotReplaceAnOwnedMovieThatAlreadyHasASidecar() {
        await using var db = CreateContext();
        var (parentId, childId, _) = await SeedMediaAsync(
            db,
            EntityKind.Movie,
            ownedCode: "bluray-1080p",
            childSelectedTitle: "Movie 2020 1080p BluRay MULTISUB");
        var entityId = (await db.Acquisitions.FindAsync(parentId))!.EntityId!.Value;
        db.EntitySubtitles.Add(new EntitySubtitleRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Language = "eng",
            Format = "vtt",
            Source = EntitySubtitleSource.Sidecar,
            SourceKey = "owned-sidecar",
            StoragePath = "/data/subtitles/owned.vtt",
            SourceFormat = "subrip",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok("x", BookFormatTier.Unknown));

        await RunAsync(
            db,
            new RecordingJobQueue(),
            replacer,
            childId,
            new FakeMediaUpgradePayloadInspector(new(1080, 1080, false, true, 7200, 7200)));

        Assert.False(replacer.Called);
    }

    [Fact]
    public async Task SameQualityMovieWithoutNewSubtitlesIsRejectedForBlocklistRecovery() {
        await using var db = CreateContext();
        var (_, childId, monitorId) = await SeedMediaAsync(
            db,
            EntityKind.Movie,
            ownedCode: "bluray-1080p",
            childSelectedTitle: "Movie 2020 1080p BluRay MULTISUB");
        var queue = new RecordingJobQueue();
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok("x", BookFormatTier.Unknown));

        await RunAsync(
            db,
            queue,
            replacer,
            childId,
            new FakeMediaUpgradePayloadInspector(new(1080, 1080, false, false, 7200, 7200)));

        Assert.False(replacer.Called);
        Assert.Equal(AcquisitionStatus.Failed, (await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == childId)).Status);
        Assert.Equal(childId, (await db.Monitors.AsNoTracking().SingleAsync(row => row.Id == monitorId)).UpgradeChildAcquisitionId);
        var recovery = Assert.Single(queue.Enqueued, job => job.Type == JobType.AcquisitionFailedHandle);
        Assert.Equal(BlocklistReason.NotAnUpgrade, AcquisitionFailedPayload.Parse(recovery.PayloadJson!).Reason);
    }

    [Fact]
    public async Task RejectedSpeculativeUpgradeDeletesItsDownloadedTransferData() {
        await using var db = CreateContext();
        var (_, childId, _) = await SeedMediaAsync(
            db,
            EntityKind.Movie,
            ownedCode: "bluray-1080p",
            childSelectedTitle: "Movie 2020 1080p BluRay MULTISUB");
        var clientId = Guid.NewGuid();
        (await db.DownloadTransfers.SingleAsync(row => row.AcquisitionId == childId)).DownloadClientConfigId = clientId;
        await db.SaveChangesAsync();
        var client = new RecordingDownloadClient();
        var detail = new DownloadClientDetail(
            clientId,
            DownloadClientKind.QBittorrent,
            "Downloads",
            "http://download-client",
            Username: null,
            Category: "prismedia",
            Enabled: true,
            HasPassword: false,
            Password: null);

        await RunAsync(
            db,
            new RecordingJobQueue(),
            new FakeReplacer(OwnedFileReplaceResult.Ok("x", BookFormatTier.Unknown)),
            childId,
            new FakeMediaUpgradePayloadInspector(new(1080, 1080, false, false, 7200, 7200)),
            new SingleDownloadClientConfigStore(detail),
            new SingleDownloadClientFactory(client));

        Assert.Equal("hash", client.RemovedClientItemId);
        Assert.True(client.DeletedData);
    }

    [Fact]
    public async Task MissingRecordedClientNeverDeletesTheSameItemOnADifferentDefaultClient() {
        await using var db = CreateContext();
        var (_, childId, _) = await SeedMediaAsync(db, EntityKind.Movie,
            ownedCode: "bluray-1080p", childSelectedTitle: "Movie 2020 1080p BluRay MULTISUB");
        (await db.DownloadTransfers.SingleAsync(row => row.AcquisitionId == childId)).DownloadClientConfigId = Guid.NewGuid();
        await db.SaveChangesAsync();
        var client = new RecordingDownloadClient();
        var unrelatedDefault = new DownloadClientDetail(Guid.NewGuid(), DownloadClientKind.QBittorrent,
            "Other downloads", "http://other-client", null, "prismedia", true, false, null);

        await RunAsync(db, new RecordingJobQueue(),
            new FakeReplacer(OwnedFileReplaceResult.Ok("x", BookFormatTier.Unknown)), childId,
            new FakeMediaUpgradePayloadInspector(new(1080, 1080, false, false, 7200, 7200)),
            new SingleDownloadClientConfigStore(unrelatedDefault), new SingleDownloadClientFactory(client));

        Assert.Null(client.RemovedClientItemId);
    }

    [Fact]
    public async Task MeasuredResolutionDowngradeIsRejectedEvenWhenTitleClaimsEqualQualityAndSubtitles() {
        await using var db = CreateContext();
        var (_, childId, _) = await SeedMediaAsync(
            db,
            EntityKind.Movie,
            ownedCode: "remux-2160p",
            childSelectedTitle: "Movie 2020 2160p Remux MULTISUB");
        var queue = new RecordingJobQueue();
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok("x", BookFormatTier.Unknown));

        await RunAsync(
            db,
            queue,
            replacer,
            childId,
            new FakeMediaUpgradePayloadInspector(new(2160, 1080, false, true, 7200, 7200)));

        Assert.False(replacer.Called);
        Assert.Contains(queue.Enqueued, job => job.Type == JobType.AcquisitionFailedHandle);
    }

    [Fact]
    public async Task ReviewedMovieReplacementWithUnknownTitleKeepsKnownOwnedQuality() {
        await using var db = CreateContext();
        var (parentId, childId, _) = await SeedMediaAsync(
            db,
            EntityKind.Movie,
            ownedCode: "bluray-1080p",
            childSelectedTitle: "manually-uploaded.mkv",
            manualPick: true);
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok("/library/Movie (2020)/Movie (2020).mkv", BookFormatTier.Unknown));

        await RunAsync(db, new RecordingJobQueue(), replacer, childId);

        Assert.True(replacer.Called);
        var parent = await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == parentId);
        Assert.Equal("bluray-1080p", parent.OwnedMediaQuality);
    }

    [Theory]
    [InlineData(null, 7200d)]
    [InlineData(7200d, null)]
    [InlineData(7200d, double.NaN)]
    [InlineData(double.NaN, 7200d)]
    [InlineData(7200d, double.PositiveInfinity)]
    [InlineData(double.PositiveInfinity, 7200d)]
    [InlineData(7200d, -1d)]
    [InlineData(7200d, 0d)]
    [InlineData(0d, 7200d)]
    public async Task UnknownOrInvalidRuntimeHoldsAutomaticReplacement(double? ownedDuration, double? candidateDuration) {
        await AssertUncertainInspectionIsHeldAsync(new(720, 1080, false, false, ownedDuration, candidateDuration));
    }

    [Fact]
    public async Task FailedInspectionPreservesTheDownloadForReviewInsteadOfPermanentlyRejectingIt() {
        await AssertUncertainInspectionIsHeldAsync(null);
    }

    private static async Task AssertUncertainInspectionIsHeldAsync(MediaUpgradePayloadInspection? inspection) {
        await using var db = CreateContext();
        var (parentId, childId, monitorId) = await SeedMediaAsync(
            db, EntityKind.Movie, VideoQuality.Bluray720p.ToCode(), "Movie 2020 1080p BluRay");
        var queue = new RecordingJobQueue();
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok("x", BookFormatTier.Unknown));

        var clientId = Guid.NewGuid();
        (await db.DownloadTransfers.SingleAsync(row => row.AcquisitionId == childId)).DownloadClientConfigId = clientId;
        await db.SaveChangesAsync();
        var client = new RecordingDownloadClient();
        var detail = new DownloadClientDetail(clientId, DownloadClientKind.QBittorrent, "Downloads",
            "http://download-client", null, "prismedia", true, false, null);

        await RunAsync(db, queue, replacer, childId, new FakeMediaUpgradePayloadInspector(inspection),
            new SingleDownloadClientConfigStore(detail), new SingleDownloadClientFactory(client));

        Assert.False(replacer.Called);
        Assert.Null(client.RemovedClientItemId);
        var child = await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == childId);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, child.Status);
        Assert.Contains("preserved", child.StatusMessage);
        Assert.Equal(VideoQuality.Bluray720p.ToCode(),
            (await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == parentId)).OwnedMediaQuality);
        Assert.Equal(childId, (await db.Monitors.SingleAsync(row => row.Id == monitorId)).UpgradeChildAcquisitionId);
        Assert.DoesNotContain(queue.Enqueued, job => job.Type == JobType.AcquisitionFailedHandle);
        Assert.Empty(await db.AcquisitionBlocklist.ToArrayAsync());
    }

    [Theory]
    [InlineData(60)]
    [InlineData(3600)]
    public async Task SubstantiallyShorterUpgradeIsHeldWithBothPayloadsPreserved(double candidateDuration) {
        await using var db = CreateContext();
        var (parentId, childId, _) = await SeedMediaAsync(
            db, EntityKind.Movie, VideoQuality.Bluray720p.ToCode(), "Movie 2020 1080p BluRay");
        var queue = new RecordingJobQueue();
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok("x", BookFormatTier.Unknown));

        await RunAsync(db, queue, replacer, childId,
            new FakeMediaUpgradePayloadInspector(new(720, 1080, false, false, 7200, candidateDuration)));

        Assert.False(replacer.Called);
        var child = await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == childId);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, child.Status);
        Assert.Contains("shorter", child.StatusMessage);
        Assert.Equal(VideoQuality.Bluray720p.ToCode(),
            (await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == parentId)).OwnedMediaQuality);
        Assert.DoesNotContain(queue.Enqueued, job => job.Type == JobType.AcquisitionFailedHandle);
        Assert.Empty(await db.AcquisitionBlocklist.ToArrayAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task KnownAudioMustMatchTheCurrentProfileBeforeAutomaticReplacement(bool manualPick, bool hasEnglish) {
        await using var db = CreateContext();
        var (parentId, childId, _) = await SeedMediaAsync(db, EntityKind.Movie,
            VideoQuality.Bluray720p.ToCode(), "Movie 2020 1080p BluRay", manualPick: manualPick);
        var profile = new BookAcquisitionProfileRow {
            Id = Guid.NewGuid(), Kind = EntityKind.Movie, DisplayName = "English",
            PreferredLanguages = ["en"], TargetLibraryRootId = Guid.NewGuid()
        };
        db.BookAcquisitionProfiles.Add(profile);
        (await db.Acquisitions.SingleAsync(row => row.Id == parentId)).ProfileId = profile.Id;
        await db.SaveChangesAsync();
        var queue = new RecordingJobQueue();
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok("x", BookFormatTier.Unknown));

        await RunAsync(db, queue, replacer, childId, new FakeMediaUpgradePayloadInspector(
            new(720, 1080, false, false, 7200, 7200, hasEnglish ? ["tur", "eng"] : ["tur"])));

        Assert.Equal(manualPick || hasEnglish, replacer.Called);
        if (!manualPick && !hasEnglish) {
            Assert.Equal(AcquisitionStatus.ManualImportRequired,
                (await db.Acquisitions.SingleAsync(row => row.Id == childId)).Status);
            Assert.Equal(VideoQuality.Bluray720p.ToCode(),
                (await db.Acquisitions.SingleAsync(row => row.Id == parentId)).OwnedMediaQuality);
            Assert.DoesNotContain(queue.Enqueued, job => job.Type == JobType.AcquisitionFailedHandle);
            Assert.Empty(await db.AcquisitionBlocklist.ToArrayAsync());
        }
    }

    [Theory]
    [InlineData(false, 7000)]
    [InlineData(true, 3600)]
    public async Task ModestRuntimeDifferencesAndExplicitlyReviewedCutsCanReplace(bool manualPick, double candidateDuration) {
        await using var db = CreateContext();
        var (_, childId, _) = await SeedMediaAsync(
            db, EntityKind.Movie, VideoQuality.Bluray720p.ToCode(), "Movie 2020 1080p BluRay", manualPick: manualPick);
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok("x", BookFormatTier.Unknown));

        await RunAsync(db, new RecordingJobQueue(), replacer, childId,
            new FakeMediaUpgradePayloadInspector(new(720, 1080, false, false, 7200, candidateDuration)));

        Assert.True(replacer.Called);
    }

    [Fact]
    public async Task UnexpectedNonAtomicKindAbortsBeforeTouchingFiles() {
        await using var db = CreateContext();
        var (_, childId, monitorId) = await SeedMediaAsync(
            db,
            EntityKind.VideoSeason,
            ownedCode: "webdl-720p",
            childSelectedTitle: "Show S01 1080p BluRay");
        var replacer = new FakeReplacer(
            OwnedFileReplaceResult.Ok("/library/Show/Season 01", BookFormatTier.Unknown));

        await RunAsync(db, new RecordingJobQueue(), replacer, childId);

        Assert.False(replacer.Called);
        var child = await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == childId);
        Assert.Equal(AcquisitionStatus.Failed, child.Status);
        Assert.Contains("does not support atomic file replacement", child.StatusMessage);
        var monitor = await db.Monitors.AsNoTracking().SingleAsync(row => row.Id == monitorId);
        Assert.Null(monitor.UpgradeChildAcquisitionId);
        Assert.Equal(1, monitor.BarrenSearches);
    }

    [Fact]
    public async Task EntityDeletionClaimMakesAStaleReplaceJobNoOpBeforeFilesystemMutation() {
        await using var db = CreateContext();
        var (parentId, childId, monitorId) = await SeedAsync(
            db,
            childSelectedTitle: "Some Book (retail) (epub)");
        var entityId = (await db.Acquisitions.FindAsync(parentId))!.EntityId!.Value;
        var entity = (await db.Entities.FindAsync(entityId))!;
        entity.LifecycleClaimKind = EntityLifecycleClaimKind.DeletingFiles;
        entity.LifecycleClaimId = Guid.NewGuid();
        entity.LifecycleClaimedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok("x", BookFormatTier.Reflowable));

        await RunAsync(db, new RecordingJobQueue(), replacer, childId);

        Assert.False(replacer.Called);
        Assert.Equal(
            AcquisitionStatus.Downloaded,
            (await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == childId)).Status);
        Assert.Equal(
            childId,
            (await db.Monitors.AsNoTracking().SingleAsync(row => row.Id == monitorId)).UpgradeChildAcquisitionId);
    }

    [Fact]
    public async Task CancelledUpgradeChildMakesAStaleReplaceJobNoOp() {
        await using var db = CreateContext();
        var (_, childId, monitorId) = await SeedAsync(
            db,
            childSelectedTitle: "Some Book (retail) (epub)");
        var child = (await db.Acquisitions.FindAsync(childId))!;
        child.Status = AcquisitionStatus.Cancelled;
        await db.SaveChangesAsync();
        var replacer = new FakeReplacer(OwnedFileReplaceResult.Ok("x", BookFormatTier.Reflowable));

        await RunAsync(db, new RecordingJobQueue(), replacer, childId);

        Assert.False(replacer.Called);
        Assert.Equal(
            AcquisitionStatus.Cancelled,
            (await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == childId)).Status);
        Assert.Equal(
            childId,
            (await db.Monitors.AsNoTracking().SingleAsync(row => row.Id == monitorId)).UpgradeChildAcquisitionId);
    }

    private static async Task<(Guid ParentId, Guid ChildId, Guid MonitorId)> SeedMediaAsync(
        PrismediaDbContext db,
        EntityKind kind,
        string ownedCode,
        string childSelectedTitle,
        bool manualPick = false) {
        var now = DateTimeOffset.UtcNow;
        var entityId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = kind.ToCode(),
            Title = "Some Movie",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Acquisitions.Add(new AcquisitionRow {
            Id = parentId, EntityId = entityId, Kind = kind, Status = AcquisitionStatus.Imported, Title = "Some Movie", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            FinalSourcePath = "/library/Movie (2020)", OwnedMediaQuality = ownedCode, UpgradeQualityCaptured = true, CreatedAt = now, UpdatedAt = now
        });
        db.Acquisitions.Add(new AcquisitionRow {
            Id = childId, Kind = kind, Status = AcquisitionStatus.Downloaded, Title = "Some Movie", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            UpgradeOfAcquisitionId = parentId, SelectedReleaseJson = JsonSerializer.Serialize(new SelectedRelease(childSelectedTitle, "Indexer", "hash", manualPick)),
            CreatedAt = now, UpdatedAt = now
        });
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(), AcquisitionId = childId, DownloadClientConfigId = SeedClient(db), ClientItemId = "hash", ContentPath = "/downloads/Movie", Progress = 1, CreatedAt = now, UpdatedAt = now
        });
        var monitorId = Guid.NewGuid();
        db.Monitors.Add(new MonitorRow {
            Id = monitorId, Kind = kind, EntityId = entityId, AcquisitionId = parentId, Status = MonitorStatus.Active, Title = "Some Movie",
            UpgradeChildAcquisitionId = childId, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return (parentId, childId, monitorId);
    }

    private static async Task RunAsync(
        PrismediaDbContext db,
        RecordingJobQueue queue,
        FakeReplacer replacer,
        Guid childId,
        IMediaUpgradePayloadInspector? inspector = null,
        IDownloadClientConfigStore? downloadClientConfigs = null,
        IDownloadClientFactory? downloadClientFactory = null) {
        var handler = new AcquisitionUpgradeReplaceJobHandler(
            AcquisitionTestFactory.Store(db), new EfMonitorStore(db), new EfBookAcquisitionProfileStore(db), replacer,
            downloadClientConfigs ?? new NullDownloadClientConfigStore(),
            downloadClientFactory ?? new ThrowingDownloadClientFactory(),
            new EfAcquisitionHistoryStore(db),
            NullLogger<AcquisitionUpgradeReplaceJobHandler>.Instance,
            mediaUpgradeInspector: inspector);
        var job = new JobRunSnapshot(
            Guid.NewGuid(), JobType.AcquisitionUpgradeReplace, JobRunStatus.Running, 0, null,
            AcquisitionJobPayload.Serialize(childId), null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        await handler.HandleAsync(new JobContext(job, queue), CancellationToken.None);
    }

    private static async Task FinalizeAsync(PrismediaDbContext db, RecordingJobQueue queue) {
        var reconcile = Assert.Single(queue.Enqueued, job => job.Type == JobType.ReconcileEntity);
        var payload = AcquisitionFinalizeJobPayload.Parse(reconcile.PayloadJson!);
        var handler = new AcquisitionFinalizeJobHandler(
            AcquisitionTestFactory.Store(db),
            new EfMonitorStore(db),
            new EfDetachedDownloadCleanupStore(db));
        var now = DateTimeOffset.UtcNow;
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.AcquisitionFinalize,
            JobRunStatus.Running,
            0,
            null,
            payload.ToJson(),
            null,
            payload.AcquisitionId.ToString(),
            null,
            now,
            now,
            null);
        await handler.HandleAsync(new JobContext(job, queue), CancellationToken.None);
    }

    private static async Task<(Guid ParentId, Guid ChildId, Guid MonitorId)> SeedAsync(
        PrismediaDbContext db,
        string childSelectedTitle,
        bool manualPick = false) {
        var now = DateTimeOffset.UtcNow;
        var entityId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = EntityKind.Book.ToCode(),
            Title = "Some Book",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Acquisitions.Add(new AcquisitionRow {
            Id = parentId, EntityId = entityId, Status = AcquisitionStatus.Imported, Title = "Some Book", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            FinalSourcePath = "/library/Some Book", OwnedSourceTier = BookSourceTier.Web, OwnedFormatTier = BookFormatTier.Reflowable,
            UpgradeQualityCaptured = true, CreatedAt = now, UpdatedAt = now
        });
        db.Acquisitions.Add(new AcquisitionRow {
            Id = childId, Status = AcquisitionStatus.Downloaded, Title = "Some Book", ExternalIdsJson = "{}", SourceUrlsJson = "[]",
            UpgradeOfAcquisitionId = parentId, SelectedReleaseJson = JsonSerializer.Serialize(new SelectedRelease(childSelectedTitle, "Indexer", "hash", manualPick)),
            CreatedAt = now, UpdatedAt = now
        });
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(), AcquisitionId = childId, DownloadClientConfigId = SeedClient(db), ClientItemId = "hash", ContentPath = "/downloads/Some Book", Progress = 1, CreatedAt = now, UpdatedAt = now
        });
        var monitorId = Guid.NewGuid();
        db.Monitors.Add(new MonitorRow {
            Id = monitorId, Kind = EntityKind.Book, EntityId = entityId, AcquisitionId = parentId, Status = MonitorStatus.Active, Title = "Some Book",
            UpgradeChildAcquisitionId = childId, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return (parentId, childId, monitorId);
    }

    private static Guid SeedClient(PrismediaDbContext db) {
        var id = Guid.NewGuid();
        db.DownloadClientConfigs.Add(new DownloadClientConfigRow { Id = id, Kind = DownloadClientKind.QBittorrent,
            DisplayName = "Downloads", BaseUrl = "http://client", Category = "validation" });
        return id;
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeReplacer(OwnedFileReplaceResult result) : IOwnedFileReplacer {
        public bool Called { get; private set; }
        public EntityKind CalledWithKind { get; private set; }
        public Task<OwnedFileReplaceResult> ReplaceAsync(string ownedFolder, string newContentPath, BookFormatTier ownedFormatTier, CancellationToken cancellationToken, EntityKind kind, bool allowFormatChange = false) {
            Called = true;
            CalledWithKind = kind;
            return Task.FromResult(result);
        }
    }

    private sealed class NullDownloadClientConfigStore : IDownloadClientConfigStore {
        public Task<DownloadClientDetail?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<DownloadClientDetail?>(null);
        public Task<DownloadClientDetail?> GetDefaultAsync(CancellationToken cancellationToken) => Task.FromResult<DownloadClientDetail?>(null);
        public Task<DownloadClientDetail?> GetDefaultAsync(Prismedia.Domain.Entities.DownloadProtocol protocol, CancellationToken cancellationToken) => GetDefaultAsync(cancellationToken);
        public Task<IReadOnlyList<DownloadClientDetail>> ListEnabledAsync(Prismedia.Domain.Entities.DownloadProtocol protocol, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Prismedia.Domain.Entities.DownloadProtocol>> GetEnabledProtocolsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Prismedia.Domain.Entities.DownloadProtocol>>([Prismedia.Domain.Entities.DownloadProtocol.Torrent]);
        public Task<IReadOnlyList<DownloadClientSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadClientDetail>> ListDetailsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientSummary> SaveAsync(DownloadClientSaveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingDownloadClientFactory : IDownloadClientFactory {
        public IDownloadClient Get(DownloadClientKind kind) => throw new NotSupportedException();
    }

    private sealed class SingleDownloadClientFactory(IDownloadClient client) : IDownloadClientFactory {
        public IDownloadClient Get(DownloadClientKind kind) => client;
    }

    private sealed class SingleDownloadClientConfigStore(DownloadClientDetail detail) : IDownloadClientConfigStore {
        public Task<DownloadClientDetail?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<DownloadClientDetail?>(id == detail.Id ? detail : null);
        public Task<DownloadClientDetail?> GetDefaultAsync(CancellationToken cancellationToken) => Task.FromResult<DownloadClientDetail?>(detail);
        public Task<DownloadClientDetail?> GetDefaultAsync(DownloadProtocol protocol, CancellationToken cancellationToken) => GetDefaultAsync(cancellationToken);
        public Task<IReadOnlyList<DownloadClientDetail>> ListEnabledAsync(DownloadProtocol protocol, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadProtocol>> GetEnabledProtocolsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadClientSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadClientDetail>> ListDetailsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientSummary> SaveAsync(DownloadClientSaveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingDownloadClient : IDownloadClient {
        public DownloadClientKind Kind => DownloadClientKind.QBittorrent;
        public string? RemovedClientItemId { get; private set; }
        public bool DeletedData { get; private set; }

        public Task RemoveAsync(DownloadClientConnection connection, string clientItemId, bool deleteData, CancellationToken cancellationToken) {
            RemovedClientItemId = clientItemId;
            DeletedData = deleteData;
            return Task.CompletedTask;
        }

        public Task<string> AddAsync(DownloadClientConnection connection, DownloadAddRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> AddTorrentFileAsync(DownloadClientConnection connection, string fileName, byte[] torrent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadItemStatus?> GetItemAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadItemStatus>> ListItemsAsync(DownloadClientConnection connection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadItemFile>> GetFilesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadItemProperties?> GetPropertiesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> GetPieceStatesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientConnectionTest> TestAsync(DownloadClientConnection connection, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeMediaUpgradePayloadInspector(MediaUpgradePayloadInspection? result) : IMediaUpgradePayloadInspector {
        public Task<MediaUpgradePayloadInspection?> InspectAsync(
            string ownedContentPath,
            string candidateContentPath,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class RecordingJobQueue : IJobQueueService {
        public List<EnqueueJobRequest> Enqueued { get; } = [];
        public Func<Task>? BeforeEnqueue { get; init; }
        public async Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            if (BeforeEnqueue is not null) await BeforeEnqueue();
            Enqueued.Add(request);
            var now = DateTimeOffset.UtcNow;
            return new JobRunSnapshot(Guid.NewGuid(), request.Type, JobRunStatus.Queued, 0, null, request.PayloadJson ?? "{}", request.TargetEntityKind, request.TargetEntityId, request.TargetLabel, now, null, null);
        }
        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
