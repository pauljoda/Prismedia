using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Queue;

namespace Prismedia.Infrastructure.Tests;

public sealed partial class HeldTvImportRecoveryTests {
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task LegacyAutomaticHoldsRecoverOnlyWithMatchingCompletedJobEvidence(bool postgres, bool manualRetry) {
        await using var database = postgres ? await PostgresTestDatabase.CreateAsync() : null;
        await using var db = database?.CreateContext() ?? CreateContext();
        var (_, acquisition, _) = await SeedAsync(db);
        var queue = new JobQueueService(db);
        var job = await queue.EnqueueAsync(new EnqueueJobRequest(JobType.AcquisitionImport,
            PayloadJson: AcquisitionJobPayload.Serialize(acquisition.Id, manualRetry: manualRetry),
            TargetEntityId: acquisition.Id.ToString()), default);
        var row = await db.JobRuns.SingleAsync(run => run.Id == job.Id);
        row.Status = JobRunStatus.Completed;
        row.FinishedAt = DateTimeOffset.UtcNow;
        acquisition.JobGraphId = job.GraphId;
        acquisition.ImportManualReview = true;
        await db.SaveChangesAsync();

        var held = await new EfHeldTvImportRecoveryStore(db, queue).ListAsync(default);

        Assert.Equal(manualRetry, acquisition.ImportManualReview);
        Assert.Equal(manualRetry ? 0 : 1, held.Count);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task LegacyHoldsPreserveMissingHistoryNewerDecisionsAndUnrelatedGraphs(bool missingHistory, bool newerDecision, bool differentGraph) {
        await using var db = CreateContext();
        var (_, acquisition, _) = await SeedAsync(db);
        var queue = new JobQueueService(db);
        var job = await queue.EnqueueAsync(new EnqueueJobRequest(JobType.AcquisitionImport,
            PayloadJson: AcquisitionJobPayload.Serialize(acquisition.Id), TargetEntityId: acquisition.Id.ToString()), default);
        var row = await db.JobRuns.SingleAsync(run => run.Id == job.Id);
        row.Status = JobRunStatus.Completed;
        row.FinishedAt = DateTimeOffset.UtcNow;
        acquisition.JobGraphId = differentGraph ? Guid.NewGuid() : job.GraphId;
        acquisition.ImportManualReview = true;
        if (newerDecision) acquisition.UpdatedAt = row.FinishedAt.Value.AddSeconds(1);
        if (missingHistory) db.JobRuns.Remove(row);
        await db.SaveChangesAsync();

        Assert.Empty(await new EfHeldTvImportRecoveryStore(db, queue).ListAsync(default));
        Assert.True(acquisition.ImportManualReview);
    }
}
