using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Queue;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class HeldTvImportRecoveryTests : IDisposable {
    private readonly string root = Directory.CreateTempSubdirectory("prismedia-held-tv-").FullName;
    private readonly Guid rootId = Guid.NewGuid();
    public void Dispose() => Directory.Delete(root, true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingNumberingQueuesProviderRefreshWithoutDiscardingTheRetainedDownload(bool missingSeason) {
        await using var db = CreateContext();
        var (service, acquisition, _) = await SeedAsync(db);
        acquisition.IdentityNamespace = "test-provider";
        acquisition.IdentityValue = "season-identity";
        if (missingSeason) {
            acquisition.SeasonNumber = null;
            (await db.Entities.SingleAsync(row => row.Id == acquisition.EntityId)).SortOrder = null;
        }
        await db.SaveChangesAsync();

        await service.RecoverAsync(default);
        await CreateService(db).RecoverAsync(default);

        var refresh = Assert.Single(await db.JobRuns.ToArrayAsync());
        Assert.Equal(JobType.AcquisitionEnrich, refresh.Type);
        Assert.Equal(acquisition.Id, AcquisitionJobPayload.Parse(refresh.PayloadJson).AcquisitionId);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        Assert.Null(acquisition.ImportRecoveryFingerprint);
        Assert.True(File.Exists(Path.Combine(root, "payload", "Show.S01E01E02.First.Story.Second.Story.mkv")));
    }

    [Fact]
    public async Task NumberingRefreshUsesDurableCooldownAndResumesOnlyAfterMetadataImproves() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (service, acquisition, episodes) = await SeedAsync(db);
        acquisition.IdentityNamespace = "test-provider";
        acquisition.IdentityValue = "season-identity";
        await db.SaveChangesAsync();
        await service.RecoverAsync(default);
        var refresh = Assert.Single(await db.JobRuns.Where(job => job.Type == JobType.AcquisitionEnrich).ToArrayAsync());
        var queue = new JobQueueService(db);
        Assert.NotNull(await queue.ClaimNextAsync("metadata-recovery-test", default));
        await db.Entry(refresh).ReloadAsync();
        await queue.CompleteAsync(refresh.Id, "Provider has no numbering yet", default);
        await db.Entry(refresh).ReloadAsync();
        Assert.Equal(JobRunStatus.Completed, refresh.Status);
        await CreateService(db).RecoverAsync(default);
        Assert.Single(await db.JobRuns.ToArrayAsync());

        refresh.CreatedAt = DateTimeOffset.UtcNow.AddDays(-2);
        refresh.FinishedAt = refresh.CreatedAt;
        await db.SaveChangesAsync();
        await CreateService(db).RecoverAsync(default);
        Assert.Equal(2, await db.JobRuns.CountAsync());
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);

        foreach (var (episode, number) in episodes.Select((episode, index) => (episode, index + 1)))
            db.EntityPositions.Add(new EntityPositionRow { EntityId = episode.Id, Code = EntityPositionCodes.Episode, Value = number });
        await db.SaveChangesAsync();
        await CreateService(db).RecoverAsync(default);
        Assert.Equal(AcquisitionStatus.Downloaded, acquisition.Status);
        Assert.Equal(2, await db.JobRuns.CountAsync());
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task MissingNumberingDoesNotRefreshPausedExplicitReviewOrUnidentifiedRequests(bool paused, bool manual, bool unidentified) {
        await using var db = CreateContext();
        var (service, acquisition, _) = await SeedAsync(db);
        if (!unidentified) {
            acquisition.IdentityNamespace = "test-provider";
            acquisition.IdentityValue = "season-identity";
        }
        if (paused) (await db.Monitors.SingleAsync()).Status = MonitorStatus.Paused;
        acquisition.ImportManualReview = manual;
        await db.SaveChangesAsync();
        await service.RecoverAsync(default);
        Assert.Empty(await db.JobRuns.ToArrayAsync());
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
    }

    [Fact]
    public async Task SlowProviderBacklogsYieldBetweenAttemptsAndRotateAcrossServiceScopes() {
        await using var db = CreateContext();
        var expected = new List<Guid>();
        for (var index = 0; index < 3; index++) {
            var (_, acquisition, episodes) = await SeedAsync(db);
            episodes[0].SortOrder = 1;
            episodes[1].SortOrder = 2;
            acquisition.UpdatedAt = DateTimeOffset.UnixEpoch.AddSeconds(index);
            expected.Add(acquisition.EntityId!.Value);
        }
        await db.SaveChangesAsync();
        File.Delete(Path.Combine(root, "payload", "Show.S01E01E02.First.Story.Second.Story.mkv"));
        await File.WriteAllTextAsync(Path.Combine(root, "payload", "unmatched.mkv"), "unresolved video");
        var clock = new RecoveryTimeProvider();
        var cursor = new HeldTvImportRecoveryCursor();
        var evidence = new RecordingEvidence(_ => { clock.Advance(TimeSpan.FromSeconds(20)); return Task.CompletedTask; });

        for (var sweep = 0; sweep < 4; sweep++) {
            await CreateService(db, evidence, cursor, clock).RecoverAsync(default);
            Assert.Equal(sweep + 1, evidence.Calls);
        }

        Assert.Equal(expected.Append(expected[0]), evidence.LinkedIds);
        Assert.All(await db.Acquisitions.ToArrayAsync(), acquisition => Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SweepTimeoutCancelsInFlightProviderWorkButCallerCancellationStillPropagates(bool callerCancels) {
        await using var db = CreateContext();
        var (_, acquisition, episodes) = await SeedAsync(db);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        await db.SaveChangesAsync();
        File.Delete(Path.Combine(root, "payload", "Show.S01E01E02.First.Story.Second.Story.mkv"));
        await File.WriteAllTextAsync(Path.Combine(root, "payload", "unmatched.mkv"), "unresolved video");
        using var caller = new CancellationTokenSource();
        var clock = new RecoveryTimeProvider();
        var evidence = new RecordingEvidence(async token => {
            if (callerCancels) caller.Cancel();
            else clock.Advance(TimeSpan.FromSeconds(20));
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        var service = CreateService(db, evidence, new HeldTvImportRecoveryCursor(), clock);

        if (callerCancels) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RecoverAsync(caller.Token));
        else await service.RecoverAsync(caller.Token);

        Assert.Equal(1, evidence.Calls);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderReadsRunOutsideTheMonitorLockAndPausingDuringLookupPreventsRecovery(bool pause) {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (_, acquisition, episodes) = await SeedAsync(db);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        await db.SaveChangesAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "payload", "Show.S01E99.mkv"), "unknown episode");
        var hadTransaction = false;
        var evidence = new RecordingEvidence(async token => {
            hadTransaction = db.Database.CurrentTransaction is not null;
            if (pause) {
                (await db.Monitors.SingleAsync(token)).Status = MonitorStatus.Paused;
                await db.SaveChangesAsync(token);
            }
        });

        await CreateService(db, evidence).RecoverAsync(default);

        Assert.Equal(1, evidence.Calls);
        Assert.False(hadTransaction);
        Assert.Equal(pause ? AcquisitionStatus.ManualImportRequired : AcquisitionStatus.Downloaded, acquisition.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MonitoringARetainedForeignSeasonResumesOnlyItsConfidentMissingEpisodes(bool mislabeled) {
        await using var db = CreateContext();
        var (service, acquisition, episodes) = await SeedAsync(db);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        foreach (var episode in episodes) episode.IsWanted = false;
        var original = Path.Combine(root, "payload", "Show.S01E01E02.First.Story.Second.Story.mkv");
        File.Delete(original);
        var foreignName = mislabeled ? "Show.S01E49.Hidden.Garden.mkv" : "Show.S02E03.Hidden.Garden.mkv";
        await File.WriteAllTextAsync(Path.Combine(root, "payload", foreignName), "retained foreign episode");
        var season = new EntityRow {
            Id = Guid.NewGuid(), ParentEntityId = (await db.Entities.SingleAsync(row => row.Id == acquisition.EntityId)).ParentEntityId,
            KindCode = EntityKind.VideoSeason.ToCode(), Title = "Season 2", SortOrder = 2, IsWanted = true
        };
        var extra = new EntityRow {
            Id = Guid.NewGuid(), ParentEntityId = season.Id, KindCode = EntityKind.VideoEpisode.ToCode(),
            Title = "Hidden Garden", SortOrder = 3, IsWanted = true
        };
        db.Entities.AddRange(season, extra);
        acquisition.FinalSourcePath = Path.Combine(root, "previous.mkv");
        await File.WriteAllTextAsync(acquisition.FinalSourcePath, "previous imported video");
        acquisition.ImportResultJson = AcquisitionImportFileLedgerJson.Serialize(
            new AcquisitionImportFileLedger(AcquisitionImportPhase.Imported, []).RetainUnmappedTvVideos([new(foreignName, 23)]));
        await db.SaveChangesAsync();

        await service.RecoverAsync(default);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        await new EfMonitorStore(db).StartForEntityAsync(season.Id, EntityKind.VideoSeason, "Season 2", null, null, default);
        await service.RecoverAsync(default);

        Assert.Equal(AcquisitionStatus.Downloaded, acquisition.Status);
        Assert.NotNull(acquisition.ImportRecoveryFingerprint);
        var fingerprint = acquisition.ImportRecoveryFingerprint;
        acquisition.Status = AcquisitionStatus.ManualImportRequired;
        await db.SaveChangesAsync();
        await service.RecoverAsync(default);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        Assert.Equal(fingerprint, acquisition.ImportRecoveryFingerprint);
        Assert.Equal("previous imported video", await File.ReadAllTextAsync(acquisition.FinalSourcePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task APartialImportCanHealOnlyWhenItsLedgerRetainsReviewVideos(bool retained) {
        await using var db = CreateContext();
        var (service, acquisition, episodes) = await SeedAsync(db);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        acquisition.FinalSourcePath = Path.Combine(root, "already-imported.mkv");
        await File.WriteAllTextAsync(acquisition.FinalSourcePath, "previous library bytes");
        if (retained) {
            acquisition.ImportResultJson = AcquisitionImportFileLedgerJson.Serialize(
                new AcquisitionImportFileLedger(AcquisitionImportPhase.Imported, []).RetainUnmappedTvVideos([
                    new("Show.S01E01E02.First.Story.Second.Story.mkv", 10)
                ]));
        }
        await db.SaveChangesAsync();

        await service.RecoverAsync(default);

        Assert.Equal(retained ? AcquisitionStatus.Downloaded : AcquisitionStatus.ManualImportRequired, acquisition.Status);
        Assert.Equal("previous library bytes", await File.ReadAllTextAsync(acquisition.FinalSourcePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialImportRecoveryRechecksTheExactRetainedEvidence(bool postgres) {
        await using var database = postgres ? await PostgresTestDatabase.CreateAsync() : null;
        await using var db = database?.CreateContext() ?? CreateContext();
        var (_, acquisition, _) = await SeedAsync(db);
        acquisition.FinalSourcePath = Path.Combine(root, "previous.mkv");
        acquisition.ImportResultJson = AcquisitionImportFileLedgerJson.Serialize(
            new AcquisitionImportFileLedger(AcquisitionImportPhase.Imported, []).RetainUnmappedTvVideos([new("extra.mkv", 10)]));
        await db.SaveChangesAsync();
        var store = new EfHeldTvImportRecoveryStore(db, new JobQueueService(db));
        var held = Assert.Single(await store.ListAsync(default));
        Assert.True(await store.TryResumeAsync(held, "mapping-one", default));
        acquisition.Status = AcquisitionStatus.ManualImportRequired;
        acquisition.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var newer = Assert.Single(await store.ListAsync(default));
        acquisition.ImportResultJson = AcquisitionImportFileLedgerJson.Serialize(new(AcquisitionImportPhase.Imported, []));
        await db.SaveChangesAsync();

        Assert.False(await store.TryResumeAsync(newer, "mapping-two", default));
        Assert.Empty(await store.ListAsync(default));
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
    }

    [Fact]
    public async Task CorrectedEpisodeIdentitiesResumeTheRetainedPayloadOnlyOncePerMapping() {
        await using var db = CreateContext();
        var (service, acquisition, episodes) = await SeedAsync(db);
        await service.RecoverAsync(CancellationToken.None);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);

        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        await db.SaveChangesAsync();
        await service.RecoverAsync(CancellationToken.None);
        Assert.Equal(AcquisitionStatus.Downloaded, acquisition.Status);
        Assert.NotNull(acquisition.ImportRecoveryFingerprint);
        Assert.Single(await AcquisitionTestFactory.Store(db).ListDownloadedCompletionWorkAsync(CancellationToken.None));

        // Model an importer that still needs review for another reason. Restarting the recovery service
        // with the same mapping must leave that hold alone rather than repeatedly queuing import jobs.
        acquisition.Status = AcquisitionStatus.ManualImportRequired;
        acquisition.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await CreateService(db).RecoverAsync(CancellationToken.None);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);

        episodes[1].Title = "Second Story Revised";
        await db.SaveChangesAsync();
        await CreateService(db).RecoverAsync(CancellationToken.None);
        Assert.Equal(AcquisitionStatus.Downloaded, acquisition.Status);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PausedMonitoringAndManualReleaseChoicesStayHeld(bool paused, bool manualPick) {
        await using var db = CreateContext();
        var (service, acquisition, episodes) = await SeedAsync(db);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        if (paused) (await db.Monitors.SingleAsync()).Status = MonitorStatus.Paused;
        await db.SaveChangesAsync();
        if (manualPick) await AcquisitionTestFactory.Store(db).SetSelectedReleaseAsync(acquisition.Id,
            new SelectedRelease("Show S01 720p WEB", "Indexer", "held-test", ManualPick: true), CancellationToken.None);

        await service.RecoverAsync(CancellationToken.None);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        Assert.Null(acquisition.ImportRecoveryFingerprint);
    }

    [Fact]
    public async Task DangerousCompanionsPreventAutomaticRecovery() {
        await using var db = CreateContext();
        var (service, acquisition, episodes) = await SeedAsync(db);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        await db.SaveChangesAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "payload", "setup.exe"), "test");

        await service.RecoverAsync(CancellationToken.None);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        Assert.Null(acquisition.ImportRecoveryFingerprint);
    }

    [Fact]
    public async Task AnExplicitImportRetryRetainsManualAuthorityAfterTheJobHistoryIsGone() {
        await using var db = CreateContext();
        var (service, acquisition, episodes) = await SeedAsync(db);
        var store = AcquisitionTestFactory.Store(db);
        Assert.True(await store.TryClaimInitialImportAsync(acquisition.Id, Guid.NewGuid(), true, CancellationToken.None));
        await store.SetStatusAsync(acquisition.Id, AcquisitionStatus.ManualImportRequired, "Review remains necessary.", CancellationToken.None);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        await db.SaveChangesAsync();

        await service.RecoverAsync(CancellationToken.None);

        Assert.True(acquisition.ImportManualReview);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
    }

    [Theory]
    [InlineData(1, false, AcquisitionStatus.Downloaded)]
    [InlineData(2, false, AcquisitionStatus.ManualImportRequired)]
    [InlineData(2, true, AcquisitionStatus.Downloaded)]
    public async Task ACombinedFileIsReconsideredOnlyWhenItCanFillAMissingOwner(int ownedEpisodes, bool missingFile, AcquisitionStatus expected) {
        await using var db = CreateContext();
        var (service, acquisition, episodes) = await SeedAsync(db);
        var season = await db.Entities.SingleAsync(entity => entity.Id == acquisition.EntityId);
        var folder = Directory.CreateDirectory(Path.Combine(root, "Show")).FullName;
        var seasonFolder = Directory.CreateDirectory(Path.Combine(folder, "Season 1")).FullName;
        db.EntitySources.AddRange(
            new EntitySourceRow { EntityId = season.ParentEntityId!.Value, Code = EntitySourceCode.Folder.ToCode(), Value = folder },
            new EntitySourceRow { EntityId = season.Id, Code = EntitySourceCode.Folder.ToCode(), Value = seasonFolder });
        var ownedPath = Path.Combine(seasonFolder, "Show - S01E01.mkv");
        await File.WriteAllTextAsync(ownedPath, "test bytes");
        for (var index = 0; index < episodes.Length; index++) {
            episodes[index].SortOrder = index + 1;
            if (index < ownedEpisodes) db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(), EntityId = episodes[index].Id, Role = EntityFileRole.Source, Path = ownedPath
            });
        }
        await db.SaveChangesAsync();

        if (missingFile) File.Delete(ownedPath);
        await service.RecoverAsync(CancellationToken.None);

        Assert.Equal(expected, acquisition.Status);
        if (missingFile) Assert.False(File.Exists(ownedPath));
        else Assert.Equal("test bytes", await File.ReadAllTextAsync(ownedPath));
        Assert.Equal(ownedEpisodes, await db.EntityFiles.CountAsync());
    }

    [Fact]
    public async Task ACompletedOlderAttemptCannotAuthorizeImportOfANewerPartialTransfer() {
        await using var db = CreateContext();
        var (service, acquisition, episodes) = await SeedAsync(db);
        episodes[0].SortOrder = 1;
        episodes[1].SortOrder = 2;
        var completed = await db.DownloadTransfers.SingleAsync();
        var store = new EfHeldTvImportRecoveryStore(db, new JobQueueService(db));
        var held = Assert.Single(await store.ListAsync(CancellationToken.None));
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisition.Id, ClientItemId = "partial-retry",
            ContentPath = completed.ContentPath, Progress = 0.5,
            CreatedAt = completed.CreatedAt.AddSeconds(1), UpdatedAt = completed.UpdatedAt.AddSeconds(1)
        });
        await db.SaveChangesAsync();

        Assert.False(await store.TryResumeAsync(held, "changed-mapping", CancellationToken.None));
        Assert.Empty(await store.ListAsync(CancellationToken.None));
        await service.RecoverAsync(CancellationToken.None);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        Assert.Null(acquisition.ImportRecoveryFingerprint);
    }

    [Fact]
    public async Task ResumeCannotOverwriteANewerHoldOrCancellation() {
        await using var db = CreateContext();
        var (_, acquisition, _) = await SeedAsync(db);
        var store = new EfHeldTvImportRecoveryStore(db, new JobQueueService(db));
        var held = Assert.Single(await store.ListAsync(CancellationToken.None));
        acquisition.UpdatedAt = held.HeldAt.AddSeconds(1);
        await db.SaveChangesAsync();
        Assert.False(await store.TryResumeAsync(held, "mapping-a", CancellationToken.None));
        var newer = Assert.Single(await store.ListAsync(CancellationToken.None));
        acquisition.Status = AcquisitionStatus.Cancelled;
        await db.SaveChangesAsync();
        Assert.False(await store.TryResumeAsync(newer, "mapping-a", CancellationToken.None));
        Assert.Equal(AcquisitionStatus.Cancelled, acquisition.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedNewFilePlanRetriesOnceWhenItsProfileOrRetainedFileChanges(bool changeProfile) {
        await using var db = CreateContext();
        var (service, acquisition, _) = await SeedAsync(db);
        var checkpoint = await SaveNewFileCheckpointAsync(db, acquisition);
        var profile = new BookAcquisitionProfileRow {
            Id = Guid.NewGuid(), Kind = EntityKind.VideoSeries, DisplayName = "Current TV rules",
            AllowedQualities = [VideoQuality.Webdl1080p.ToCode()]
        };
        db.BookAcquisitionProfiles.Add(profile);
        acquisition.ProfileId = profile.Id;
        await db.SaveChangesAsync();

        await service.RecoverAsync(default);
        Assert.Equal(AcquisitionStatus.Downloaded, acquisition.Status);
        var fingerprint = acquisition.ImportRecoveryFingerprint;
        Assert.NotNull(fingerprint);
        // A failed retry keeps the elected plan; its new queue claim is not a validation input.
        acquisition.Status = AcquisitionStatus.ManualImportRequired;
        acquisition.ImportCheckpointJson = TvImportCheckpointJson.Serialize(checkpoint with { ClaimJobId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        await CreateService(db).RecoverAsync(default);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        Assert.Equal(fingerprint, acquisition.ImportRecoveryFingerprint);

        if (changeProfile) profile.AllowedQualities = [VideoQuality.Webdl720p.ToCode()];
        else await File.AppendAllTextAsync(checkpoint.Units[0].SourceAbsolutePath!, " repaired payload");
        await db.SaveChangesAsync();
        var exactPlan = acquisition.ImportCheckpointJson;
        await CreateService(db).RecoverAsync(default);

        Assert.Equal(AcquisitionStatus.Downloaded, acquisition.Status);
        Assert.NotEqual(fingerprint, acquisition.ImportRecoveryFingerprint);
        Assert.Equal(exactPlan, acquisition.ImportCheckpointJson);
        Assert.False(File.Exists(checkpoint.Units[0].TargetAbsolutePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedPlanRecoveryPreservesCompletedUnitsAndChecksTheExactObservation(bool postgres) {
        await using var database = postgres ? await PostgresTestDatabase.CreateAsync() : null;
        await using var db = database?.CreateContext() ?? CreateContext();
        var (_, acquisition, _) = await SeedAsync(db);
        var checkpoint = await SaveNewFileCheckpointAsync(db, acquisition);
        var placed = Path.Combine(checkpoint.SeriesFolderPath, "placed.mkv");
        await File.WriteAllTextAsync(placed, "already imported bytes");
        checkpoint = checkpoint with { Units = [.. checkpoint.Units,
            new("placed.mkv", placed, 1, 2, [], FinalPath: placed,
                SourceAbsolutePath: Path.Combine(root, "payload", "placed.mkv"))] };
        acquisition.ImportCheckpointJson = TvImportCheckpointJson.Serialize(checkpoint);
        acquisition.FinalSourcePath = placed;
        await db.SaveChangesAsync();
        var store = new EfHeldTvImportRecoveryStore(db, new JobQueueService(db));
        var held = Assert.Single(await store.ListAsync(default));
        Assert.True(await store.TryResumeAsync(held, "inputs-one", default));
        Assert.Equal(checkpoint.AttemptId, TvImportCheckpointJson.Deserialize(acquisition.ImportCheckpointJson)!.AttemptId);
        Assert.Equal(placed, acquisition.FinalSourcePath);
        Assert.Equal("already imported bytes", await File.ReadAllTextAsync(placed));
        acquisition.Status = AcquisitionStatus.ManualImportRequired;
        await db.SaveChangesAsync();
        var newer = Assert.Single(await store.ListAsync(default));
        acquisition.ImportCheckpointJson = TvImportCheckpointJson.Serialize(checkpoint with { AttemptId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        Assert.False(await store.TryResumeAsync(newer, "inputs-two", default));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CompleteTransferChangesInvalidateSavedPlanRecovery(bool postgres, bool newerTransfer) {
        await using var database = postgres ? await PostgresTestDatabase.CreateAsync() : null;
        await using var db = database?.CreateContext() ?? CreateContext();
        var (_, acquisition, _) = await SeedAsync(db);
        await SaveNewFileCheckpointAsync(db, acquisition);
        var store = new EfHeldTvImportRecoveryStore(db, new JobQueueService(db));
        var held = Assert.Single(await store.ListAsync(default));
        var transfer = await db.DownloadTransfers.SingleAsync();
        if (newerTransfer) db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisition.Id, ClientItemId = transfer.ClientItemId,
            ContentPath = transfer.ContentPath, Progress = 1, CreatedAt = transfer.CreatedAt.AddSeconds(1)
        });
        else transfer.ContentPath = Path.Combine(root, "different-payload");
        await db.SaveChangesAsync();

        Assert.False(await store.TryResumeAsync(held, "changed-inputs", default));
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AManualReleaseChangeAfterObservationCannotBeAutomaticallyResumed(bool postgres) {
        await using var database = postgres ? await PostgresTestDatabase.CreateAsync() : null;
        await using var db = database?.CreateContext() ?? CreateContext();
        var (_, acquisition, _) = await SeedAsync(db);
        await SaveNewFileCheckpointAsync(db, acquisition);
        var store = new EfHeldTvImportRecoveryStore(db, new JobQueueService(db));
        var held = Assert.Single(await store.ListAsync(default));
        // Do not change UpdatedAt: the release itself is part of the observed authority.
        acquisition.SelectedReleaseJson = System.Text.Json.JsonSerializer.Serialize(
            new SelectedRelease("Show S01 720p WEB", "Indexer", "held-test", ManualPick: true));
        await db.SaveChangesAsync();

        Assert.False(await store.TryResumeAsync(held, "changed-inputs", default));
        Assert.Empty(await store.ListAsync(default));
    }

    [Fact]
    public async Task UnknownCheckpointMembersStayHeldWithoutQueuingAnUnclaimablePlan() {
        await using var db = CreateContext();
        var (service, acquisition, _) = await SeedAsync(db);
        await SaveNewFileCheckpointAsync(db, acquisition);
        acquisition.ImportCheckpointJson = acquisition.ImportCheckpointJson![..^1] + ",\"FuturePolicy\":true}";
        await db.SaveChangesAsync();

        Assert.Empty(await new EfHeldTvImportRecoveryStore(db, new JobQueueService(db)).ListAsync(default));
        await service.RecoverAsync(default);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task SavedPlansPreservePausedManualAndActiveClaimAuthority(bool paused, bool manual, bool claimed) {
        await using var db = CreateContext();
        var (service, acquisition, _) = await SeedAsync(db);
        await SaveNewFileCheckpointAsync(db, acquisition);
        if (paused) (await db.Monitors.SingleAsync()).Status = MonitorStatus.Paused;
        acquisition.ImportManualReview = manual;
        if (claimed) acquisition.ImportClaimJobId = Guid.NewGuid();
        await db.SaveChangesAsync();

        await service.RecoverAsync(default);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        Assert.Null(acquisition.ImportRecoveryFingerprint);
    }

    [Fact]
    public async Task AnElectedExtrasDestinationProfileChangeResumesTheSamePlanWithoutReelecting() {
        await using var db = CreateContext();
        var (service, acquisition, _) = await SeedAsync(db);
        var checkpoint = await SaveNewFileCheckpointAsync(db, acquisition);
        var requestedSeason = await db.Entities.SingleAsync(entity => entity.Id == acquisition.EntityId);
        var destination = new EntityRow {
            Id = Guid.NewGuid(), ParentEntityId = requestedSeason.ParentEntityId,
            KindCode = EntityKind.VideoSeason.ToCode(), Title = "Season 2", SortOrder = 2
        };
        db.Entities.Add(destination);
        db.Entities.Add(new EntityRow { Id = Guid.NewGuid(), ParentEntityId = destination.Id,
            KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Extra episode", SortOrder = 1, IsWanted = true });
        var profile = new BookAcquisitionProfileRow { Id = Guid.NewGuid(), Kind = EntityKind.VideoSeries,
            DisplayName = "Extra season rules", AllowedQualities = [VideoQuality.Webdl1080p.ToCode()] };
        db.BookAcquisitionProfiles.Add(profile);
        db.Monitors.Add(new MonitorRow { Id = Guid.NewGuid(), EntityId = destination.Id, Kind = EntityKind.VideoSeason,
            Title = "Season 2", ProfileId = profile.Id, Status = MonitorStatus.Paused });
        checkpoint = checkpoint with { Units = [checkpoint.Units[0] with { SeasonNumber = 2 }] };
        acquisition.ImportCheckpointJson = TvImportCheckpointJson.Serialize(checkpoint);
        await db.SaveChangesAsync();
        await service.RecoverAsync(default);
        Assert.Equal(AcquisitionStatus.Downloaded, acquisition.Status);
        acquisition.Status = AcquisitionStatus.ManualImportRequired;
        await db.SaveChangesAsync();
        await service.RecoverAsync(default);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        profile.AllowedQualities = [VideoQuality.Webdl720p.ToCode()];
        await db.SaveChangesAsync();

        await service.RecoverAsync(default);

        Assert.Equal(AcquisitionStatus.Downloaded, acquisition.Status);
        Assert.Equal(TvImportCheckpointJson.Serialize(checkpoint), acquisition.ImportCheckpointJson);
        Assert.Equal(MonitorStatus.Paused, (await db.Monitors.SingleAsync(row => row.EntityId == destination.Id)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullyPlacedAndReplacementPlansKeepTheirDedicatedRecovery(bool replacement) {
        await using var db = CreateContext();
        var (service, acquisition, _) = await SeedAsync(db);
        var checkpoint = await SaveNewFileCheckpointAsync(db, acquisition);
        var unit = checkpoint.Units[0];
        await File.WriteAllTextAsync(unit.TargetAbsolutePath, "owned bytes");
        unit = replacement ? unit with {
            PreviousFilePath = unit.TargetAbsolutePath,
            ReplacementBackupPath = OwnedFileReplacementArtifacts.CheckpointBackupPath(unit.TargetAbsolutePath, checkpoint.AttemptId),
            ReplacementEvidencePath = OwnedFileReplacementArtifacts.CheckpointEvidencePath(unit.TargetAbsolutePath, checkpoint.AttemptId)
        } : unit with { FinalPath = unit.TargetAbsolutePath };
        acquisition.ImportCheckpointJson = TvImportCheckpointJson.Serialize(checkpoint with { Units = [unit] });
        await db.SaveChangesAsync();

        Assert.Empty(await new EfHeldTvImportRecoveryStore(db, new JobQueueService(db)).ListAsync(default));
        await service.RecoverAsync(default);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        Assert.Equal("owned bytes", await File.ReadAllTextAsync(unit.TargetAbsolutePath));
    }

    private async Task<TvImportCheckpoint> SaveNewFileCheckpointAsync(PrismediaDbContext db, AcquisitionRow acquisition) {
        var seriesFolder = Directory.CreateDirectory(Path.Combine(root, "Show")).FullName;
        var name = "Show.S01E01E02.First.Story.Second.Story.mkv";
        var checkpoint = new TvImportCheckpoint(rootId, seriesFolder, ImportMode.Move, false, "Imported", false,
            [new(name, Path.Combine(seriesFolder, "episode.mkv"), 1, 1, [],
                SourceAbsolutePath: Path.Combine(root, "payload", name))],
            TransferClientItemId: "retained-payload", AttemptId: Guid.NewGuid(), ClaimJobId: Guid.NewGuid());
        acquisition.ImportCheckpointJson = TvImportCheckpointJson.Serialize(checkpoint);
        await db.SaveChangesAsync();
        return checkpoint;
    }

    private async Task<(HeldTvImportRecoveryService Service, AcquisitionRow Acquisition, EntityRow[] Episodes)> SeedAsync(PrismediaDbContext db) {
        var now = DateTimeOffset.UtcNow;
        var series = new EntityRow { Id = Guid.NewGuid(), KindCode = EntityKind.VideoSeries.ToCode(), Title = "Show", CreatedAt = now, UpdatedAt = now };
        var season = new EntityRow { Id = Guid.NewGuid(), KindCode = EntityKind.VideoSeason.ToCode(), Title = "Season 1", ParentEntityId = series.Id, SortOrder = 1, CreatedAt = now, UpdatedAt = now };
        EntityRow[] episodes = [
            new() { Id = Guid.NewGuid(), KindCode = EntityKind.VideoEpisode.ToCode(), Title = "First Story", ParentEntityId = season.Id, IsWanted = true, CreatedAt = now, UpdatedAt = now },
            new() { Id = Guid.NewGuid(), KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Second Story", ParentEntityId = season.Id, IsWanted = true, CreatedAt = now, UpdatedAt = now }
        ];
        db.Entities.AddRange([series, season, .. episodes]);
        var acquisition = new AcquisitionRow {
            Id = Guid.NewGuid(), EntityId = season.Id, Kind = EntityKind.VideoSeason,
            Title = "Season 1", Series = "Show", SeasonNumber = 1,
            Status = AcquisitionStatus.ManualImportRequired, CreatedAt = now, UpdatedAt = now
        };
        db.Acquisitions.Add(acquisition);
        var payload = Directory.CreateDirectory(Path.Combine(root, "payload")).FullName;
        await File.WriteAllTextAsync(Path.Combine(payload, "Show.S01E01E02.First.Story.Second.Story.mkv"), "test bytes");
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisition.Id, ClientItemId = "retained-payload",
            ContentPath = payload, Progress = 1, CreatedAt = now, UpdatedAt = now
        });
        db.Monitors.Add(new MonitorRow {
            Id = Guid.NewGuid(), AcquisitionId = acquisition.Id, EntityId = season.Id,
            Kind = EntityKind.VideoSeason, Title = "Season 1", Status = MonitorStatus.Active, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        await AcquisitionTestFactory.Store(db).SetSelectedReleaseAsync(acquisition.Id,
            new SelectedRelease("Show S01 720p WEB", "Indexer", "held-test"), CancellationToken.None);
        return (CreateService(db), acquisition, episodes);
    }

    private HeldTvImportRecoveryService CreateService(PrismediaDbContext db, ITvEpisodeCatalogEvidenceSource? evidence = null,
        HeldTvImportRecoveryCursor? cursor = null, TimeProvider? clock = null) => new(
        new EfHeldTvImportRecoveryStore(db, new JobQueueService(db)), AcquisitionTestFactory.Store(db), new EfImportTargetIndex(db),
        new DownloadPayloadReader(), new EfBookAcquisitionProfileStore(db), new Roots(root, rootId), new EfMonitorStore(db),
        NullLogger<HeldTvImportRecoveryService>.Instance, evidence, cursor, clock);

    private sealed class RecoveryTimeProvider : TimeProvider {
        private long timestamp;
        private readonly List<RecoveryTimer> timers = [];
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) {
            var timer = new RecoveryTimer(callback, state, timestamp + dueTime.Ticks);
            timers.Add(timer);
            return timer;
        }
        public void Advance(TimeSpan duration) {
            timestamp += duration.Ticks;
            foreach (var timer in timers) timer.Fire(timestamp);
        }
    }

    private sealed class RecoveryTimer(TimerCallback callback, object? state, long dueAt) : ITimer {
        private bool finished;
        public void Fire(long timestamp) {
            if (finished || timestamp < dueAt) return;
            finished = true;
            callback(state);
        }
        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException("Recovery uses a fixed one-shot budget.");
        public void Dispose() => finished = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class RecordingEvidence(Func<CancellationToken, Task> read) : ITvEpisodeCatalogEvidenceSource {
        public int Calls { get; private set; }
        public List<Guid> LinkedIds { get; } = [];
        public async Task<IReadOnlyList<TvSeasonEpisodeCatalog>> ReadAsync(Guid linkedEntityId, int requestedSeason,
            IReadOnlyList<ImportCandidateFile> files, CancellationToken cancellationToken) {
            Calls++;
            LinkedIds.Add(linkedEntityId);
            await read(cancellationToken);
            return [];
        }
    }

    private static PrismediaDbContext CreateContext() => new(new DbContextOptionsBuilder<PrismediaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Roots(string path, Guid id) : ILibraryScanRootPersistence {
        private readonly LibraryRootData library = new(id, path, "Library", true, true, true, false, false, false, false);
        public Task<LibraryRootData?> GetLibraryRootAsync(Guid rootId, CancellationToken cancellationToken) => Task.FromResult<LibraryRootData?>(library);
        public Task<IReadOnlyList<LibraryRootData>> GetEnabledRootsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LibraryRootData>>([library]);
        public Task<LibrarySettingsData> GetSettingsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateRootLastScannedAsync(Guid rootId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetExcludedPathsForRootAsync(Guid rootId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveEntitiesInExcludedPathsAsync(Guid rootId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveEntitiesOutsideLibraryRootsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveOrphanTagsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
