using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Ensures acquisition teardown cancels only queued/running jobs for the exact acquisition.</summary>
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
