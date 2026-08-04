using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Queue;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Ensures acquisition teardown cancels exact work and any active graph that owns it.</summary>
public sealed class AcquisitionJobCleanupTests {
    [Fact]
    public async Task CancelsQueuedAndRunningJobsForOneAcquisitionOnly() {
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var queued = AddJob(db, acquisitionId, JobRunStatus.Queued, JobType.AcquisitionSearch);
        var running = AddJob(db, acquisitionId, JobRunStatus.Running, JobType.AcquisitionImport);
        var completed = AddJob(db, acquisitionId, JobRunStatus.Completed, JobType.AcquisitionEnrich);
        var unrelated = AddJob(db, otherId, JobRunStatus.Running, JobType.AcquisitionImport);
        await db.SaveChangesAsync();

        var count = await new AcquisitionJobCleanup(db)
            .CancelAsync(acquisitionId, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(JobRunStatus.Cancelled, (await db.JobRuns.FindAsync(queued))!.Status);
        Assert.Equal(JobRunStatus.Cancelled, (await db.JobRuns.FindAsync(running))!.Status);
        Assert.Equal(JobRunStatus.Completed, (await db.JobRuns.FindAsync(completed))!.Status);
        Assert.Equal(JobRunStatus.Running, (await db.JobRuns.FindAsync(unrelated))!.Status);
    }

    [Fact]
    public async Task CancelsTheOpenReleaseReviewGraphOwnedByTheRemovedAcquisition() {
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var signalId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.JobGraphs.Add(new JobGraphRow {
            Id = graphId,
            Origin = JobGraphOrigin.Background,
            Status = JobGraphStatus.Waiting,
            DisplayName = "Let It Go (Demi Lovato version)",
            RootRunId = runId,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.JobRuns.Add(new JobRunRow {
            Id = runId,
            GraphId = graphId,
            Type = JobType.AcquisitionSearch,
            Status = JobRunStatus.Completed,
            TargetEntityId = acquisitionId.ToString(),
            AvailableAt = now,
            CreatedAt = now,
            FinishedAt = now
        });
        db.JobGraphSignals.Add(new JobGraphSignalRow {
            Id = signalId,
            GraphId = graphId,
            Key = AcquisitionGraphSignals.Review(acquisitionId),
            Kind = JobGraphSignalKind.DomainEvent,
            Message = "Waiting for release review",
            CreatedAt = now
        });
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            JobGraphId = graphId,
            Kind = EntityKind.AudioTrack,
            Status = AcquisitionStatus.AwaitingSelection,
            Title = "Let It Go (Demi Lovato version)",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        await new AcquisitionJobCleanup(db, new JobGraphService(db))
            .CancelAsync(acquisitionId, CancellationToken.None);

        var graph = await db.JobGraphs.SingleAsync(row => row.Id == graphId);
        Assert.Equal(JobGraphStatus.Cancelled, graph.Status);
        Assert.NotNull(graph.FinishedAt);
        Assert.True(graph.CancellationRequested);
        Assert.NotNull((await db.JobGraphSignals.SingleAsync(row => row.Id == signalId)).CancelledAt);
    }

    private static Guid AddJob(
        PrismediaDbContext db,
        Guid acquisitionId,
        JobRunStatus status,
        JobType type) {
        var id = Guid.NewGuid();
        db.JobRuns.Add(new JobRunRow {
            Id = id,
            Type = type,
            Status = status,
            TargetEntityId = acquisitionId.ToString(),
            AvailableAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        });
        return id;
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
