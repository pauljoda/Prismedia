using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Queue;

namespace Prismedia.Infrastructure.Tests;

public sealed class JobQueueServiceTests {
    [Fact]
    public async Task EnqueueCreatesQueuedJobAndListReturnsNewestFirst() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);

        var first = await service.EnqueueAsync(JobType.ScanLibrary, CancellationToken.None);
        var second = await service.EnqueueAsync(JobType.ProbeVideo, CancellationToken.None);
        var jobs = await service.ListAsync(hideNsfw: false, CancellationToken.None);

        Assert.Equal(JobRunStatus.Queued, first.Status);
        Assert.Equal(JobType.ProbeVideo, second.Type);
        Assert.Equal(2, jobs.Count);
        Assert.Equal(second.Id, jobs[0].Id);
        Assert.Equal(first.Id, jobs[1].Id);
        Assert.NotNull(first.GraphId);
        Assert.NotNull(second.GraphId);
        Assert.All(await db.JobGraphs.ToListAsync(), graph =>
            Assert.Equal(JobGraphOrigin.Background, graph.Origin));
    }

    [Fact]
    public async Task SeparateInteractiveActionsForTheSameEntityCreateIndependentGraphs() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var entityId = Guid.NewGuid().ToString();
        var request = new EnqueueJobRequest(
            JobType.IdentifySearch,
            TargetEntityKind: EntityKind.Video.ToCode(),
            TargetEntityId: entityId,
            Origin: JobGraphOrigin.Interactive);

        var first = await service.EnqueueAsync(request, CancellationToken.None);
        var second = await service.EnqueueAsync(request, CancellationToken.None);

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.GraphId, second.GraphId);
        Assert.Equal(2, await db.JobGraphs.CountAsync());
        Assert.All(await db.JobGraphs.ToListAsync(), graph =>
            Assert.Equal(JobGraphOrigin.Interactive, graph.Origin));
    }

    [Fact]
    public async Task InteractiveGraphRecordsTheInitiatingUserAndChildrenRetainThatGraph() {
        await using var db = CreateContext();
        var userId = Guid.NewGuid();
        var service = new JobQueueService(db, TestUserContext.MemberAs(userId));
        var parent = await service.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.RefreshEntity,
                TargetEntityKind: EntityKind.Video.ToCode(),
                TargetEntityId: Guid.NewGuid().ToString(),
                Origin: JobGraphOrigin.Interactive),
            CancellationToken.None);

        var child = await service.EnqueueChildAsync(
            parent,
            new EnqueueJobRequest(
                JobType.ProbeVideo,
                TargetEntityKind: EntityKind.Video.ToCode(),
                TargetEntityId: Guid.NewGuid().ToString()),
            CancellationToken.None);

        var graph = await db.JobGraphs.SingleAsync();
        Assert.Equal(userId, graph.InitiatingUserId);
        Assert.Equal(graph.Id, parent.GraphId);
        Assert.Equal(graph.Id, child.GraphId);
        Assert.Equal(JobGraphOrigin.Interactive, child.GraphOrigin);
    }

    [Fact]
    public async Task ChildEnqueueInheritsTheParentsGraphAndLane() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var parent = await service.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.RefreshEntity,
                TargetEntityId: Guid.NewGuid().ToString(),
                Origin: JobGraphOrigin.Interactive),
            CancellationToken.None);

        var child = await service.EnqueueChildAsync(
            parent,
            new EnqueueJobRequest(
                JobType.ProbeVideo,
                TargetEntityId: Guid.NewGuid().ToString()),
            CancellationToken.None);

        Assert.Equal(parent.GraphId, child.GraphId);
        Assert.Equal(parent.Id, child.ParentRunId);
        Assert.Equal(JobGraphOrigin.Interactive, child.GraphOrigin);
    }

    [Fact]
    public async Task SingletonJobsDropDuplicateEnqueues() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);

        var first = await service.EnqueueAsync(JobType.ScanLibrary, CancellationToken.None);
        // A second scan of the same kind returns the in-flight job instead of stacking another.
        var duplicate = await service.EnqueueAsync(JobType.ScanLibrary, CancellationToken.None);
        // A scan of a different kind is independent and is enqueued normally.
        var gallery = await service.EnqueueAsync(JobType.ScanGallery, CancellationToken.None);

        Assert.Equal(first.Id, duplicate.Id);
        Assert.NotEqual(first.Id, gallery.Id);
        var backup = await service.EnqueueAsync(JobType.DatabaseBackup, CancellationToken.None);
        var duplicateBackup = await service.EnqueueAsync(JobType.DatabaseBackup, CancellationToken.None);
        Assert.Equal(backup.Id, duplicateBackup.Id);
        Assert.Equal(3, await db.JobRuns.CountAsync());

        // Once the first scan reaches a terminal state, a fresh scan of that kind enqueues again.
        var firstRow = await db.JobRuns.FirstAsync(job => job.Id == first.Id);
        firstRow.Status = JobRunStatus.Completed;
        await db.SaveChangesAsync();

        var rescan = await service.EnqueueAsync(JobType.ScanLibrary, CancellationToken.None);
        Assert.NotEqual(first.Id, rescan.Id);
        Assert.Equal(4, await db.JobRuns.CountAsync());
    }

    [Fact]
    public async Task TargetedMonitoredSearchesDoNotCollapseIntoOrBlockTheGlobalSweep() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var firstEntityId = Guid.NewGuid().ToString();
        var secondEntityId = Guid.NewGuid().ToString();

        var firstTarget = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.MonitoredSearch,
            TargetEntityKind: JobTargetKinds.Entity,
            TargetEntityId: firstEntityId), CancellationToken.None);
        var global = await service.EnqueueAsync(JobType.MonitoredSearch, CancellationToken.None);
        var secondTarget = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.MonitoredSearch,
            TargetEntityKind: JobTargetKinds.Entity,
            TargetEntityId: secondEntityId), CancellationToken.None);
        var duplicateFirstTarget = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.MonitoredSearch,
            TargetEntityKind: JobTargetKinds.Entity,
            TargetEntityId: firstEntityId), CancellationToken.None);

        Assert.NotEqual(global.Id, firstTarget.Id);
        Assert.NotEqual(firstTarget.Id, secondTarget.Id);
        Assert.Equal(firstTarget.Id, duplicateFirstTarget.Id);
        Assert.Equal(3, await db.JobRuns.CountAsync());
    }

    [Fact]
    public async Task TargetedJobsReturnExistingPendingRunInsteadOfStackingDuplicates() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var entityId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee").ToString();

        var first = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.AutoIdentify,
            TargetEntityKind: EntityKind.AudioLibrary.ToCode(),
            TargetEntityId: entityId,
            TargetLabel: "Album"), CancellationToken.None);
        var duplicate = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.AutoIdentify,
            TargetEntityKind: EntityKind.AudioLibrary.ToCode(),
            TargetEntityId: entityId,
            TargetLabel: "Album again"), CancellationToken.None);
        var otherType = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.GeneratePreview,
            TargetEntityKind: EntityKind.AudioLibrary.ToCode(),
            TargetEntityId: entityId,
            TargetLabel: "Album preview"), CancellationToken.None);

        Assert.Equal(first.Id, duplicate.Id);
        Assert.NotEqual(first.Id, otherType.Id);
        Assert.Equal(2, await db.JobRuns.CountAsync());
    }

    [Fact]
    public async Task DirectPlayableEpisodeAutoIdentifyCanRunWhilePrerequisiteWorkIsPending() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var prerequisite = await service.EnqueueAsync(JobType.ProbeVideo, CancellationToken.None);
        var prerequisiteRow = await db.JobRuns.SingleAsync(run => run.Id == prerequisite.Id);
        prerequisiteRow.Status = JobRunStatus.Running;
        await db.SaveChangesAsync();
        var episode = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.AutoIdentify,
            TargetEntityKind: EntityKind.VideoEpisode.ToCode(),
            TargetEntityId: Guid.NewGuid().ToString()), CancellationToken.None);

        var claimed = await service.ClaimNextGraphNodeAsync(
            "worker",
            JobGraphOrigin.Background,
            CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(episode.Id, claimed.Id);
    }

    [Fact]
    public async Task ListKeepsActiveAndFailedRunsVisibleWhenBacklogExceedsRecentLimit() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var now = DateTimeOffset.UtcNow;
        var running = NewJobRun(JobType.GeneratePreview, JobRunStatus.Running, now.AddHours(-3));
        var failed = NewJobRun(JobType.FingerprintVideo, JobRunStatus.Failed, now.AddHours(-2));
        db.JobRuns.AddRange(running, failed);

        for (var i = 0; i < 210; i++) {
            db.JobRuns.Add(NewJobRun(
                JobType.ProbeVideo,
                JobRunStatus.Queued,
                now.AddMinutes(i),
                targetEntityId: i.ToString()));
        }

        await db.SaveChangesAsync();

        var jobs = await service.ListAsync(hideNsfw: false, CancellationToken.None);

        Assert.Contains(jobs, job => job.Id == running.Id);
        Assert.Contains(jobs, job => job.Id == failed.Id);
        Assert.True(jobs.Count <= 200);
    }

    [Fact]
    public async Task ListAndCountsExcludeNsfwEntityAndLibraryRootTargetsWhenHidden() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var now = DateTimeOffset.UtcNow;
        var safeEntityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var nsfwEntityId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var safeRootId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var nsfwRootId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        db.Entities.AddRange(
            new EntityRow {
                Id = safeEntityId,
                KindCode = EntityKind.Video.ToCode(),
                Title = "Safe",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = nsfwEntityId,
                KindCode = EntityKind.Video.ToCode(),
                Title = "Hidden",
                IsNsfw = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.LibraryRoots.AddRange(
            new LibraryRootRow {
                Id = safeRootId,
                Path = "/media/safe",
                Label = "Safe",
                CreatedAt = now,
                UpdatedAt = now
            },
            new LibraryRootRow {
                Id = nsfwRootId,
                Path = "/media/nsfw",
                Label = "Hidden",
                IsNsfw = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.JobRuns.AddRange(
            NewJobRun(JobType.GeneratePreview, JobRunStatus.Queued, now, safeEntityId.ToString(), EntityKind.Video.ToCode()),
            NewJobRun(JobType.GeneratePreview, JobRunStatus.Queued, now.AddMinutes(1), nsfwEntityId.ToString(), EntityKind.Video.ToCode()),
            NewJobRun(JobType.ScanLibrary, JobRunStatus.Queued, now.AddMinutes(2), safeRootId.ToString(), "library-root"),
            NewJobRun(JobType.ScanLibrary, JobRunStatus.Queued, now.AddMinutes(3), nsfwRootId.ToString(), "library-root"));
        await db.SaveChangesAsync();

        var jobs = await service.ListAsync(hideNsfw: true, CancellationToken.None);
        var counts = await service.GetQueueCountsAsync(hideNsfw: true, CancellationToken.None);

        Assert.DoesNotContain(jobs, job => job.TargetEntityId == nsfwEntityId.ToString());
        Assert.DoesNotContain(jobs, job => job.TargetEntityId == nsfwRootId.ToString());
        Assert.Contains(jobs, job => job.TargetEntityId == safeEntityId.ToString());
        Assert.Contains(jobs, job => job.TargetEntityId == safeRootId.ToString());
        Assert.Equal(1, Assert.Single(counts, count => count.TypeCode == JobType.GeneratePreview.ToCode()).Count);
        Assert.Equal(1, Assert.Single(counts, count => count.TypeCode == JobType.ScanLibrary.ToCode()).Count);
    }

    [Fact]
    public async Task CancelAllCancelsNsfwHiddenTargetsWhenDashboardIsHidden() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var now = DateTimeOffset.UtcNow;
        var safeEntityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var nsfwEntityId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        db.Entities.AddRange(
            new EntityRow {
                Id = safeEntityId,
                KindCode = EntityKind.Video.ToCode(),
                Title = "Safe",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = nsfwEntityId,
                KindCode = EntityKind.Video.ToCode(),
                Title = "Hidden",
                IsNsfw = true,
                CreatedAt = now,
                UpdatedAt = now
            });

        var safeQueued = NewJobRun(
            JobType.GeneratePreview,
            JobRunStatus.Queued,
            now,
            safeEntityId.ToString(),
            EntityKind.Video.ToCode());
        var hiddenQueued = NewJobRun(
            JobType.GeneratePreview,
            JobRunStatus.Queued,
            now.AddMinutes(1),
            nsfwEntityId.ToString(),
            EntityKind.Video.ToCode());
        var hiddenRunning = NewJobRun(
            JobType.FingerprintVideo,
            JobRunStatus.Running,
            now.AddMinutes(2),
            nsfwEntityId.ToString(),
            EntityKind.Video.ToCode());
        hiddenRunning.LockedAt = now.AddMinutes(3);
        hiddenRunning.LockedBy = "worker-1";
        var hiddenCompleted = NewJobRun(
            JobType.ImportMetadata,
            JobRunStatus.Completed,
            now.AddMinutes(4),
            nsfwEntityId.ToString(),
            EntityKind.Video.ToCode());

        db.JobRuns.AddRange(safeQueued, hiddenQueued, hiddenRunning, hiddenCompleted);
        await db.SaveChangesAsync();

        var sfwJobs = await service.ListAsync(hideNsfw: true, CancellationToken.None);
        var sfwCounts = await service.GetQueueCountsAsync(hideNsfw: true, CancellationToken.None);
        var cancelled = await service.CancelAsync(null, CancellationToken.None);
        var rows = await db.JobRuns.AsNoTracking().ToDictionaryAsync(row => row.Id);

        Assert.Contains(sfwJobs, job => job.Id == safeQueued.Id);
        Assert.DoesNotContain(sfwJobs, job => job.Id == hiddenQueued.Id);
        Assert.DoesNotContain(sfwJobs, job => job.Id == hiddenRunning.Id);
        Assert.Equal(1, Assert.Single(sfwCounts, count =>
            count.TypeCode == JobType.GeneratePreview.ToCode() &&
            count.StatusCode == JobRunStatus.Queued.ToCode()).Count);

        Assert.Equal(3, cancelled);
        Assert.Equal(JobRunStatus.Cancelled, rows[safeQueued.Id].Status);
        Assert.Equal(JobRunStatus.Cancelled, rows[hiddenQueued.Id].Status);
        Assert.Equal(JobRunStatus.Cancelled, rows[hiddenRunning.Id].Status);
        Assert.Null(rows[hiddenRunning.Id].LockedAt);
        Assert.Null(rows[hiddenRunning.Id].LockedBy);
        Assert.Equal(JobRunStatus.Completed, rows[hiddenCompleted.Id].Status);
    }

    [Fact]
    public async Task ClaimCompleteAndFailAdvanceJobLifecycle() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);

        var created = await service.EnqueueAsync(JobType.Noop, CancellationToken.None);
        var claimed = await service.ClaimNextAsync("worker-1", CancellationToken.None);
        await service.CompleteAsync(created.Id, "done", CancellationToken.None);
        var completed = await db.JobRuns.FindAsync(created.Id);

        Assert.NotNull(claimed);
        Assert.Equal(created.Id, claimed.Id);
        Assert.Equal(JobRunStatus.Running, claimed.Status);
        Assert.NotNull(completed);
        Assert.Equal(JobRunStatus.Completed, completed.Status);
        Assert.Equal(100, completed.Progress);
        Assert.Equal("done", completed.Message);
    }

    [Fact]
    public async Task InteractiveAndBackgroundClaimsUseSeparatePools() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var background = await service.EnqueueAsync(JobType.Noop, CancellationToken.None);
        var interactive = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.IdentifySearch,
            TargetEntityKind: EntityKind.Video.ToCode(),
            TargetEntityId: Guid.NewGuid().ToString(),
            Origin: JobGraphOrigin.Interactive), CancellationToken.None);

        var interactiveClaim = await service.ClaimNextGraphNodeAsync(
            "interactive-worker", JobGraphOrigin.Interactive, CancellationToken.None);
        var backgroundClaim = await service.ClaimNextGraphNodeAsync(
            "background-worker", JobGraphOrigin.Background, CancellationToken.None);

        Assert.Equal(interactive.Id, interactiveClaim?.Id);
        Assert.Equal(background.Id, backgroundClaim?.Id);
    }

    [Fact]
    public async Task CpuEligibilityIsAppliedBeforeAJobIsClaimed() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var heavy = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.GeneratePreview,
            ResourceClass: JobResourceClass.HeavyCpu), CancellationToken.None);
        var light = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.Noop,
            ResourceClass: JobResourceClass.Light), CancellationToken.None);

        var claimed = await service.ClaimNextGraphNodeAsync(
            "worker-1",
            JobGraphOrigin.Background,
            CancellationToken.None,
            [JobResourceClass.Light]);

        Assert.Equal(light.Id, claimed?.Id);
        Assert.Equal(JobRunStatus.Queued, (await db.JobRuns.FindAsync(heavy.Id))?.Status);
    }

    [Fact]
    public async Task DurablePluginResourceSerializesInvocationsAndHonorsStartInterval() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var resourceKey = JobResourceKeys.Plugin(WellKnownPluginIds.MusicBrainz);
        await service.DeclareResourceAsync(
            resourceKey,
            maxConcurrency: 1,
            minimumStartInterval: TimeSpan.FromSeconds(30),
            CancellationToken.None);
        var first = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.IdentifySearch,
            Origin: JobGraphOrigin.Interactive,
            ResourceKey: resourceKey), CancellationToken.None);
        var second = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.IdentifySearch,
            Origin: JobGraphOrigin.Interactive,
            ResourceKey: resourceKey), CancellationToken.None);

        var firstClaim = await service.ClaimNextGraphNodeAsync(
            "worker-1", JobGraphOrigin.Interactive, CancellationToken.None);
        var overlappingClaim = await service.ClaimNextGraphNodeAsync(
            "worker-2", JobGraphOrigin.Interactive, CancellationToken.None);
        Assert.Equal(first.Id, firstClaim?.Id);
        Assert.Null(overlappingClaim);

        await service.CompleteAsync(first.Id, "done", CancellationToken.None);
        var intervalBlockedClaim = await service.ClaimNextGraphNodeAsync(
            "worker-2", JobGraphOrigin.Interactive, CancellationToken.None);
        Assert.Null(intervalBlockedClaim);

        var resource = await db.JobResourceStates.SingleAsync(row => row.Key == resourceKey);
        resource.NextAvailableAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        var secondClaim = await service.ClaimNextGraphNodeAsync(
            "worker-2", JobGraphOrigin.Interactive, CancellationToken.None);
        Assert.Equal(second.Id, secondClaim?.Id);
    }

    [Fact]
    public async Task FailedClaimRetriesUntilMaxAttempts() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);

        var created = await service.EnqueueAsync(JobType.ImportMetadata, CancellationToken.None);
        await service.ClaimNextAsync("worker-1", CancellationToken.None);
        await service.FailAsync(created.Id, "missing handler", TimeSpan.Zero, CancellationToken.None);
        await service.ClaimNextAsync("worker-1", CancellationToken.None);
        await service.FailAsync(created.Id, "missing handler", TimeSpan.Zero, CancellationToken.None);
        await service.ClaimNextAsync("worker-1", CancellationToken.None);
        await service.FailAsync(created.Id, "missing handler", TimeSpan.Zero, CancellationToken.None);
        var failed = await db.JobRuns.FindAsync(created.Id);

        Assert.NotNull(failed);
        Assert.Equal(JobRunStatus.Failed, failed.Status);
        Assert.Equal(3, failed.Attempts);
        Assert.NotNull(failed.FinishedAt);
    }

    [Fact]
    public async Task DeferKeepsGraphAssociationWhenProviderSlotIsBusy() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);

        var created = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.BulkIdentify,
            Origin: JobGraphOrigin.Interactive), CancellationToken.None);
        await service.ClaimNextGraphNodeAsync(
            "worker-1", JobGraphOrigin.Interactive, CancellationToken.None);

        await service.DeferAsync(
            created.Id,
            "Bulk identify waiting for provider slot; retrying soon.",
            TimeSpan.Zero,
            CancellationToken.None);

        var deferred = await db.JobRuns.FindAsync(created.Id);
        Assert.NotNull(deferred);
        Assert.Equal(JobRunStatus.Queued, deferred.Status);
        Assert.Equal(created.GraphId, deferred.GraphId);
        Assert.Equal(0, deferred.Attempts);
        Assert.Equal("Bulk identify waiting for provider slot; retrying soon.", deferred.Message);
    }

    [Fact]
    public async Task RecoverStaleRunningJobsRequeuesOnlyExpiredRunsFromOtherWorkers() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var now = DateTimeOffset.UtcNow;

        var staleOtherWorker = NewJobRun(JobType.GenerateBookPageThumbnail, JobRunStatus.Running, now.AddMinutes(-30));
        staleOtherWorker.LockedAt = now.AddMinutes(-25);
        staleOtherWorker.LockedBy = "worker-old";
        staleOtherWorker.Progress = 60;
        staleOtherWorker.Message = "Generating thumbnail";

        var freshOtherWorker = NewJobRun(JobType.ProbeVideo, JobRunStatus.Running, now.AddMinutes(-2));
        freshOtherWorker.LockedAt = now.AddMinutes(-1);
        freshOtherWorker.LockedBy = "worker-old";

        var currentWorker = NewJobRun(JobType.GeneratePreview, JobRunStatus.Running, now.AddMinutes(-30));
        currentWorker.LockedAt = now.AddMinutes(-25);
        currentWorker.LockedBy = "worker-live";

        db.JobRuns.AddRange(staleOtherWorker, freshOtherWorker, currentWorker);
        await db.SaveChangesAsync();

        var recovered = await service.RecoverStaleRunningAsync("worker-live", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Equal(1, recovered);
        Assert.Equal(JobRunStatus.Queued, staleOtherWorker.Status);
        Assert.Equal(0, staleOtherWorker.Progress);
        Assert.Equal("Recovered from stale worker lease", staleOtherWorker.Message);
        Assert.Null(staleOtherWorker.LockedAt);
        Assert.Null(staleOtherWorker.LockedBy);
        Assert.Null(staleOtherWorker.StartedAt);
        Assert.Equal(JobRunStatus.Running, freshOtherWorker.Status);
        Assert.Equal(JobRunStatus.Running, currentWorker.Status);
    }

    [Theory]
    [InlineData(JobGraphStatus.Completed, false)]
    [InlineData(JobGraphStatus.Waiting, true)]
    public async Task RecoveryResumesAStrandedDurableImportOnAFreshGraph(
        JobGraphStatus originalGraphStatus,
        bool keepGraphOpen) {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var acquisitionId = Guid.NewGuid();
        var original = await service.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.AcquisitionImport,
                PayloadJson: AcquisitionJobPayload.Serialize(acquisitionId),
                TargetEntityId: acquisitionId.ToString(),
                Origin: JobGraphOrigin.Interactive),
            CancellationToken.None);
        var originalRun = await db.JobRuns.SingleAsync(run => run.Id == original.Id);
        originalRun.Status = JobRunStatus.Completed;
        originalRun.FinishedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var originalGraph = await db.JobGraphs.SingleAsync(graph => graph.Id == original.GraphId);
        originalGraph.Status = originalGraphStatus;
        originalGraph.FinishedAt = keepGraphOpen ? null : originalRun.FinishedAt;
        if (keepGraphOpen) {
            db.JobGraphSignals.Add(new JobGraphSignalRow {
                Id = Guid.NewGuid(),
                GraphId = originalGraph.Id,
                Key = "unrelated-acquisition-review",
                Kind = JobGraphSignalKind.DomainEvent,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            });
        }
        var acquisition = new AcquisitionRow {
            Id = acquisitionId,
            Kind = EntityKind.VideoEpisode,
            Title = "Stranded episode",
            Status = AcquisitionStatus.Importing,
            ImportCheckpointJson = "{}",
            JobGraphId = original.GraphId,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        };
        db.Acquisitions.Add(acquisition);
        await db.SaveChangesAsync();

        await service.RecoverStaleRunningAsync(
            "worker-live",
            TimeSpan.FromMinutes(2),
            CancellationToken.None);

        db.ChangeTracker.Clear();
        var recovered = await db.Acquisitions.SingleAsync(row => row.Id == acquisitionId);
        var retry = await db.JobRuns.SingleAsync(run => run.Id != original.Id);
        Assert.Equal(AcquisitionStatus.Failed, recovered.Status);
        Assert.Contains("resuming", recovered.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(original.GraphId, recovered.JobGraphId);
        Assert.Equal(recovered.JobGraphId, retry.GraphId);
        Assert.Equal(JobRunStatus.Queued, retry.Status);
        Assert.Equal(
            JobGraphOrigin.Interactive,
            (await db.JobGraphs.SingleAsync(graph => graph.Id == retry.GraphId)).Origin);
        Assert.True(AcquisitionJobPayload.Parse(retry.PayloadJson!).ManualRetry);
    }

    [Fact]
    public async Task RecoveryExplainsAStrandedImportWithoutADurableCheckpoint() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var acquisitionId = Guid.NewGuid();
        var original = await service.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.AcquisitionImport,
                PayloadJson: AcquisitionJobPayload.Serialize(acquisitionId),
                TargetEntityId: acquisitionId.ToString(),
                Origin: JobGraphOrigin.Interactive),
            CancellationToken.None);
        var originalRun = await db.JobRuns.SingleAsync(run => run.Id == original.Id);
        originalRun.Status = JobRunStatus.Completed;
        originalRun.FinishedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var originalGraph = await db.JobGraphs.SingleAsync(graph => graph.Id == original.GraphId);
        originalGraph.Status = JobGraphStatus.Completed;
        originalGraph.FinishedAt = originalRun.FinishedAt;
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Kind = EntityKind.VideoEpisode,
            Title = "Unrecoverable episode",
            Status = AcquisitionStatus.Importing,
            JobGraphId = original.GraphId,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        });
        await db.SaveChangesAsync();

        await service.RecoverStaleRunningAsync(
            "worker-live",
            TimeSpan.FromMinutes(2),
            CancellationToken.None);

        db.ChangeTracker.Clear();
        var recovered = await db.Acquisitions.SingleAsync(row => row.Id == acquisitionId);
        Assert.Equal(AcquisitionStatus.Failed, recovered.Status);
        Assert.Contains("before a resumable checkpoint", recovered.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original.GraphId, recovered.JobGraphId);
        Assert.Single(await db.JobRuns.ToArrayAsync());
    }

    [Fact]
    public async Task RecoveryDoesNotRestartAnExplicitlyCancelledImportGraph() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var acquisitionId = Guid.NewGuid();
        var original = await service.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.AcquisitionImport,
                PayloadJson: AcquisitionJobPayload.Serialize(acquisitionId),
                TargetEntityId: acquisitionId.ToString(),
                Origin: JobGraphOrigin.Interactive),
            CancellationToken.None);
        var originalRun = await db.JobRuns.SingleAsync(run => run.Id == original.Id);
        originalRun.Status = JobRunStatus.Cancelled;
        originalRun.FinishedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var originalGraph = await db.JobGraphs.SingleAsync(graph => graph.Id == original.GraphId);
        originalGraph.Status = JobGraphStatus.Cancelled;
        originalGraph.CancellationRequested = true;
        originalGraph.FinishedAt = originalRun.FinishedAt;
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Kind = EntityKind.VideoEpisode,
            Title = "Cancelled episode",
            Status = AcquisitionStatus.Importing,
            ImportCheckpointJson = "{}",
            JobGraphId = original.GraphId,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        });
        await db.SaveChangesAsync();

        await service.RecoverStaleRunningAsync(
            "worker-live",
            TimeSpan.FromMinutes(2),
            CancellationToken.None);

        db.ChangeTracker.Clear();
        var recovered = await db.Acquisitions.SingleAsync(row => row.Id == acquisitionId);
        Assert.Equal(AcquisitionStatus.Failed, recovered.Status);
        Assert.Contains("explicit retry", recovered.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original.GraphId, recovered.JobGraphId);
        Assert.Single(await db.JobRuns.ToArrayAsync());
    }

    [Fact]
    public async Task PruneHistoryRetainsTerminalNodesWhileTheirGraphIsStillActive() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var old = DateTimeOffset.UtcNow.AddDays(-30);
        var waitingRoot = NewJobRun(JobType.AcquisitionSearch, JobRunStatus.Completed, old);
        waitingRoot.FinishedAt = old.AddMinutes(1);
        var waitingGraph = new JobGraphRow {
            Id = Guid.NewGuid(),
            Origin = JobGraphOrigin.Background,
            Status = JobGraphStatus.Waiting,
            DisplayName = "Waiting for release review",
            RootRunId = waitingRoot.Id,
            ActiveKey = $"{JobType.AcquisitionSearch.ToCode()}:{Guid.NewGuid()}",
            CreatedAt = old,
            UpdatedAt = old
        };
        waitingRoot.GraphId = waitingGraph.Id;
        var terminalHistory = NewJobRun(JobType.Noop, JobRunStatus.Completed, old);
        terminalHistory.FinishedAt = old.AddMinutes(1);
        db.JobGraphs.Add(waitingGraph);
        db.JobRuns.AddRange(waitingRoot, terminalHistory);
        await db.SaveChangesAsync();

        var pruned = await service.PruneHistoryAsync(TimeSpan.FromDays(7), CancellationToken.None);

        Assert.Equal(1, pruned);
        Assert.NotNull(await db.JobRuns.FindAsync(waitingRoot.Id));
        Assert.Null(await db.JobRuns.FindAsync(terminalHistory.Id));
    }

    [Fact]
    public async Task EnqueueRetiresAnOrphanedActiveKeyWhenTheGraphRootWasPruned() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var acquisitionId = Guid.NewGuid();
        var orphanedGraph = new JobGraphRow {
            Id = Guid.NewGuid(),
            Origin = JobGraphOrigin.Background,
            Status = JobGraphStatus.Waiting,
            DisplayName = "Orphaned acquisition search",
            RootRunId = Guid.NewGuid(),
            ActiveKey = $"{JobType.AcquisitionSearch.ToCode()}:{acquisitionId}",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };
        var openSignal = new JobGraphSignalRow {
            Id = Guid.NewGuid(),
            GraphId = orphanedGraph.Id,
            Key = AcquisitionGraphSignals.Review(acquisitionId),
            Kind = JobGraphSignalKind.DomainEvent,
            CreatedAt = orphanedGraph.CreatedAt
        };
        db.JobGraphs.Add(orphanedGraph);
        db.JobGraphSignals.Add(openSignal);
        await db.SaveChangesAsync();

        var replacement = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.AcquisitionSearch,
            PayloadJson: AcquisitionJobPayload.Serialize(acquisitionId),
            TargetEntityId: acquisitionId.ToString(),
            Origin: JobGraphOrigin.Background), CancellationToken.None);

        Assert.NotEqual(orphanedGraph.Id, replacement.GraphId);
        Assert.Equal(JobRunStatus.Queued, replacement.Status);
        Assert.Equal(JobGraphStatus.Cancelled, orphanedGraph.Status);
        Assert.True(orphanedGraph.CancellationRequested);
        Assert.NotNull(orphanedGraph.FinishedAt);
        Assert.NotNull(openSignal.CancelledAt);
    }

    [Fact]
    public async Task CancelAndClearFailuresMoveRunsOutOfActiveBuckets() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);

        var queued = await service.EnqueueAsync(JobType.ScanLibrary, CancellationToken.None);
        var pending = await service.EnqueueAsync(JobType.ProbeVideo, CancellationToken.None);
        var running = await service.ClaimNextAsync("worker-1", CancellationToken.None);

        var cancelled = await service.CancelAsync(null, CancellationToken.None);
        Assert.NotNull(running);
        await service.CompleteAsync(running.Id, "should not overwrite cancellation", CancellationToken.None);
        var cancelledQueued = await db.JobRuns.FindAsync(queued.Id);
        var cancelledPending = await db.JobRuns.FindAsync(pending.Id);

        Assert.Equal(2, cancelled);
        Assert.NotNull(cancelledQueued);
        Assert.NotNull(cancelledPending);
        Assert.Equal(JobRunStatus.Cancelled, cancelledQueued.Status);
        Assert.Equal(JobRunStatus.Cancelled, cancelledPending.Status);

        var failed = await service.EnqueueAsync(JobType.ImportMetadata, CancellationToken.None);
        await service.ClaimNextAsync("worker-2", CancellationToken.None);
        await service.FailAsync(failed.Id, "permanent", TimeSpan.Zero, CancellationToken.None);
        await service.ClaimNextAsync("worker-2", CancellationToken.None);
        await service.FailAsync(failed.Id, "permanent", TimeSpan.Zero, CancellationToken.None);
        await service.ClaimNextAsync("worker-2", CancellationToken.None);
        await service.FailAsync(failed.Id, "permanent", TimeSpan.Zero, CancellationToken.None);

        var cleared = await service.ClearFailuresAsync(JobType.ImportMetadata, CancellationToken.None);
        var clearedFailed = await db.JobRuns.FindAsync(failed.Id);

        Assert.Equal(1, cleared);
        Assert.NotNull(clearedFailed);
        Assert.Equal(JobRunStatus.Cancelled, clearedFailed.Status);
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"job-queue-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }

    private static JobRunRow NewJobRun(
        JobType type,
        JobRunStatus status,
        DateTimeOffset createdAt,
        string? targetEntityId = null,
        string? targetEntityKind = null) =>
        new() {
            Id = Guid.NewGuid(),
            Type = type,
            Status = status,
            PayloadJson = "{}",
            Attempts = status == JobRunStatus.Running ? 1 : 0,
            MaxAttempts = 3,
            Progress = status == JobRunStatus.Running ? 50 : 0,
            TargetEntityKind = targetEntityKind,
            TargetEntityId = targetEntityId,
            AvailableAt = createdAt,
            CreatedAt = createdAt,
            StartedAt = status == JobRunStatus.Running ? createdAt.AddMinutes(1) : null,
            FinishedAt = status == JobRunStatus.Failed ? createdAt.AddMinutes(1) : null
        };
}
