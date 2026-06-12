using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
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
    }

    [Fact]
    public async Task ScanJobsArePerKindSingletonsThatDropDuplicateEnqueues() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);

        var first = await service.EnqueueAsync(JobType.ScanLibrary, CancellationToken.None);
        // A second scan of the same kind returns the in-flight job instead of stacking another.
        var duplicate = await service.EnqueueAsync(JobType.ScanLibrary, CancellationToken.None);
        // A scan of a different kind is independent and is enqueued normally.
        var gallery = await service.EnqueueAsync(JobType.ScanGallery, CancellationToken.None);

        Assert.Equal(first.Id, duplicate.Id);
        Assert.NotEqual(first.Id, gallery.Id);
        Assert.Equal(2, await db.JobRuns.CountAsync());

        // Once the first scan reaches a terminal state, a fresh scan of that kind enqueues again.
        var firstRow = await db.JobRuns.FirstAsync(job => job.Id == first.Id);
        firstRow.Status = JobRunStatus.Completed;
        await db.SaveChangesAsync();

        var rescan = await service.EnqueueAsync(JobType.ScanLibrary, CancellationToken.None);
        Assert.NotEqual(first.Id, rescan.Id);
        Assert.Equal(3, await db.JobRuns.CountAsync());
    }

    [Fact]
    public async Task TargetedJobsReturnExistingPendingRunInsteadOfStackingDuplicates() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var entityId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee").ToString();

        var first = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.AutoIdentify,
            TargetEntityKind: EntityKindRegistry.AudioLibrary.Code,
            TargetEntityId: entityId,
            TargetLabel: "Album"), CancellationToken.None);
        var duplicate = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.AutoIdentify,
            TargetEntityKind: EntityKindRegistry.AudioLibrary.Code,
            TargetEntityId: entityId,
            TargetLabel: "Album again"), CancellationToken.None);
        var otherType = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.GeneratePreview,
            TargetEntityKind: EntityKindRegistry.AudioLibrary.Code,
            TargetEntityId: entityId,
            TargetLabel: "Album preview"), CancellationToken.None);

        Assert.Equal(first.Id, duplicate.Id);
        Assert.NotEqual(first.Id, otherType.Id);
        Assert.Equal(2, await db.JobRuns.CountAsync());
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
                KindCode = EntityKindRegistry.Video.Code,
                Title = "Safe",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = nsfwEntityId,
                KindCode = EntityKindRegistry.Video.Code,
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
            NewJobRun(JobType.GeneratePreview, JobRunStatus.Queued, now, safeEntityId.ToString(), EntityKindRegistry.Video.Code),
            NewJobRun(JobType.GeneratePreview, JobRunStatus.Queued, now.AddMinutes(1), nsfwEntityId.ToString(), EntityKindRegistry.Video.Code),
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
    public async Task ClaimNextPrefersInteractiveIdentifyOverAutoIdentifyBacklog() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);
        var now = DateTimeOffset.UtcNow;
        var autoIdentify = NewJobRun(
            JobType.AutoIdentify,
            JobRunStatus.Queued,
            now.AddMinutes(-10),
            priority: JobPriorities.AutoIdentify);
        var manualBulkIdentify = NewJobRun(
            JobType.BulkIdentify,
            JobRunStatus.Queued,
            now,
            priority: JobPriorities.InteractiveIdentify);
        db.JobRuns.AddRange(autoIdentify, manualBulkIdentify);
        await db.SaveChangesAsync();

        var claimed = await service.ClaimNextAsync("worker-1", CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(manualBulkIdentify.Id, claimed.Id);
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
    public async Task DeferKeepsPriorityWhenProviderSlotIsBusy() {
        await using var db = CreateContext();
        var service = new JobQueueService(db);

        var created = await service.EnqueueAsync(new EnqueueJobRequest(
            JobType.BulkIdentify,
            Priority: JobPriorities.InteractiveIdentify), CancellationToken.None);
        await service.ClaimNextAsync("worker-1", CancellationToken.None);

        await service.DeferAsync(
            created.Id,
            "Bulk identify waiting for provider slot; retrying soon.",
            TimeSpan.Zero,
            CancellationToken.None);

        var deferred = await db.JobRuns.FindAsync(created.Id);
        Assert.NotNull(deferred);
        Assert.Equal(JobRunStatus.Queued, deferred.Status);
        Assert.Equal(JobPriorities.InteractiveIdentify, deferred.Priority);
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
        string? targetEntityKind = null,
        int priority = 0) =>
        new() {
            Id = Guid.NewGuid(),
            Type = type,
            Status = status,
            PayloadJson = "{}",
            Priority = priority,
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
