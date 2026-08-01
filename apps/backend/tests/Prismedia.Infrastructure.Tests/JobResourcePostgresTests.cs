using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Queue;

namespace Prismedia.Infrastructure.Tests;

/// <summary>PostgreSQL regressions for durable resource locking and xmin concurrency.</summary>
public sealed class JobResourcePostgresTests {
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ClaimingAResourceGatedNodeLocksAndUpdatesTheRealPostgresRow() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var queue = new JobQueueService(db);
        var resourceKey = JobResourceKeys.Plugin("postgres-resource-test");
        await queue.DeclareResourceAsync(resourceKey, 1, TimeSpan.FromMilliseconds(25), CancellationToken.None);
        var queued = await queue.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.Noop,
                Origin: JobGraphOrigin.Interactive,
                ResourceKey: resourceKey),
            CancellationToken.None);

        var claimed = await queue.ClaimNextGraphNodeAsync(
            "postgres-resource-worker",
            JobGraphOrigin.Interactive,
            CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(queued.Id, claimed.Id);
        Assert.Equal(resourceKey, claimed.ResourceKey);
        Assert.Single(await db.JobResourceLeases.AsNoTracking().ToArrayAsync());
        Assert.True((await db.JobResourceStates.AsNoTracking().SingleAsync()).NextAvailableAt > DateTimeOffset.MinValue);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task RecoveryRepairsAQueuedEntityResourceBeforeTheNodeIsClaimed() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var resourceKey = JobResourceKeys.Entity(Guid.NewGuid().ToString("D"));
        Guid runId;
        await using (var setup = database.CreateContext()) {
            var queued = await new JobQueueService(setup).EnqueueAsync(
                new EnqueueJobRequest(
                    JobType.Noop,
                    Origin: JobGraphOrigin.Interactive,
                    ResourceKey: resourceKey),
                CancellationToken.None);
            runId = queued.Id;

            Assert.True(await setup.JobResourceStates.AnyAsync(state => state.Key == resourceKey));
            await setup.JobResourceStates
                .Where(state => state.Key == resourceKey)
                .ExecuteDeleteAsync();
        }

        await using (var recovery = database.CreateContext()) {
            await new JobQueueService(recovery).RecoverStaleRunningAsync(
                "postgres-recovery-worker",
                TimeSpan.FromMinutes(5),
                CancellationToken.None);
            Assert.True(await recovery.JobResourceStates
                .AsNoTracking()
                .AnyAsync(state => state.Key == resourceKey));
        }

        await using var claiming = database.CreateContext();
        var claimed = await new JobQueueService(claiming).ClaimNextGraphNodeAsync(
            "postgres-recovery-worker",
            JobGraphOrigin.Interactive,
            CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(runId, claimed.Id);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ConcurrentSchedulersCannotExceedOneDurableResourceLease() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var resourceKey = JobResourceKeys.Plugin("postgres-concurrency-test");
        await using (var setup = database.CreateContext()) {
            var queue = new JobQueueService(setup);
            await queue.DeclareResourceAsync(resourceKey, 1, TimeSpan.Zero, CancellationToken.None);
            await queue.EnqueueAsync(
                new EnqueueJobRequest(JobType.Noop, Origin: JobGraphOrigin.Interactive, ResourceKey: resourceKey),
                CancellationToken.None);
            await queue.EnqueueAsync(
                new EnqueueJobRequest(JobType.Noop, Origin: JobGraphOrigin.Interactive, ResourceKey: resourceKey),
                CancellationToken.None);
        }

        await using var firstDb = database.CreateContext();
        await using var secondDb = database.CreateContext();
        var claims = await Task.WhenAll(
            new JobQueueService(firstDb).ClaimNextGraphNodeAsync(
                "postgres-worker-a", JobGraphOrigin.Interactive, CancellationToken.None),
            new JobQueueService(secondDb).ClaimNextGraphNodeAsync(
                "postgres-worker-b", JobGraphOrigin.Interactive, CancellationToken.None));

        Assert.Single(claims, claim => claim is not null);
        await using var verification = database.CreateContext();
        Assert.Single(await verification.JobResourceLeases.AsNoTracking().ToArrayAsync());
        Assert.Equal(1, await verification.JobRuns.CountAsync(run => run.Status == JobRunStatus.Running));
        Assert.Equal(1, await verification.JobRuns.CountAsync(run => run.Status == JobRunStatus.Queued));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ConcurrentNodeProgressDoesNotFailOnTheSharedGraphConcurrencyToken() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Guid graphId;
        Guid[] runIds;
        await using (var setup = database.CreateContext()) {
            var graphs = new JobGraphService(setup);
            var graph = await graphs.StartAsync(
                new StartJobGraphRequest(
                    JobGraphOrigin.Background,
                    "Large scan",
                    new GraphJobNodeRequest("node:0", new EnqueueJobRequest(JobType.Noop))),
                CancellationToken.None);
            for (var index = 1; index < 32; index++) {
                await graphs.AppendNodeAsync(
                    graph.Id,
                    new GraphJobNodeRequest($"node:{index}", new EnqueueJobRequest(JobType.Noop)),
                    CancellationToken.None);
            }

            var now = DateTimeOffset.UtcNow;
            await setup.JobRuns
                .Where(run => run.GraphId == graph.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(run => run.Status, JobRunStatus.Running)
                    .SetProperty(run => run.LockedBy, "test-worker")
                    .SetProperty(run => run.LockedAt, now)
                    .SetProperty(run => run.StartedAt, now));
            graphId = graph.Id;
            runIds = await setup.JobRuns.AsNoTracking()
                .Where(run => run.GraphId == graph.Id)
                .Select(run => run.Id)
                .ToArrayAsync();
        }

        await Task.WhenAll(runIds.Select(async runId => {
            await using var context = database.CreateContext();
            await new JobQueueService(context).UpdateProgressAsync(
                runId,
                10,
                "started",
                CancellationToken.None);
        }));

        await Task.WhenAll(runIds.Select(async runId => {
            await using var context = database.CreateContext();
            await new JobQueueService(context).CompleteAsync(
                runId,
                "done",
                CancellationToken.None);
        }));

        await using var verification = database.CreateContext();
        Assert.All(
            await verification.JobRuns.AsNoTracking().Where(run => run.GraphId == graphId).ToArrayAsync(),
            run => Assert.Equal(JobRunStatus.Completed, run.Status));
        Assert.Equal(
            JobGraphStatus.Completed,
            (await verification.JobGraphs.AsNoTracking().SingleAsync(graph => graph.Id == graphId)).Status);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ConcurrentGraphExpansionProducesEveryStableChildExactlyOnce() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Guid graphId;
        await using (var setup = database.CreateContext()) {
            graphId = (await new JobGraphService(setup).StartAsync(
                new StartJobGraphRequest(
                    JobGraphOrigin.Background,
                    "Expanding scan",
                    new GraphJobNodeRequest("root", new EnqueueJobRequest(JobType.ScanLibrary))),
                CancellationToken.None)).Id;
        }

        await Task.WhenAll(Enumerable.Range(0, 32).Select(async index => {
            await using var context = database.CreateContext();
            await new JobGraphService(context).AppendNodeAsync(
                graphId,
                new GraphJobNodeRequest(
                    $"probe:{index}",
                    new EnqueueJobRequest(JobType.ProbeVideo)),
                CancellationToken.None);
        }));

        await using var verification = database.CreateContext();
        var nodes = await verification.JobRuns.AsNoTracking()
            .Where(run => run.GraphId == graphId)
            .OrderBy(run => run.Sequence)
            .ToArrayAsync();
        Assert.Equal(33, nodes.Length);
        Assert.Equal(33, nodes.Select(node => node.NodeKey).Distinct().Count());
        Assert.Equal(33, nodes.Select(node => node.Sequence).Distinct().Count());
    }
}
