using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Queue;

namespace Prismedia.Infrastructure.Tests;

public sealed class JobGraphServiceTests {
    [Fact]
    public async Task InteractiveGraphOwnsItsRootNodeAndStableLane() {
        await using var db = CreateContext();
        var service = new JobGraphService(db);
        var entityId = Guid.NewGuid().ToString();

        var graph = await service.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Interactive,
                "Identify Album",
                new GraphJobNodeRequest(
                    "identify-root",
                    new EnqueueJobRequest(
                        JobType.IdentifySearch,
                        TargetEntityKind: EntityKindRegistry.AudioLibrary.Code,
                        TargetEntityId: entityId,
                        TargetLabel: "Album")),
                RootEntityKind: EntityKindRegistry.AudioLibrary.Code,
                RootEntityId: entityId),
            CancellationToken.None);

        var root = await db.JobRuns.SingleAsync();

        Assert.Equal(JobGraphOrigin.Interactive, graph.Origin);
        Assert.Equal(graph.Id, graph.LaneId);
        Assert.Equal(graph.Id, root.GraphId);
        Assert.Equal("identify-root", root.NodeKey);
        Assert.Equal(JobNodeImportance.Required, root.Importance);
    }

    [Fact]
    public async Task ChildNodeInheritsGraphAndWaitsForItsDependency() {
        await using var db = CreateContext();
        var graphs = new JobGraphService(db);
        var queue = new JobQueueService(db);
        var graph = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Interactive,
                "Refresh Video",
                new GraphJobNodeRequest(
                    "reconcile",
                    new EnqueueJobRequest(JobType.RefreshEntity))),
            CancellationToken.None);
        var root = await db.JobRuns.SingleAsync();

        var child = await graphs.AppendNodeAsync(
            graph.Id,
            new GraphJobNodeRequest(
                "probe",
                new EnqueueJobRequest(JobType.ProbeVideo),
                ParentRunId: root.Id,
                DependsOn: [root.Id]),
            CancellationToken.None);

        var firstClaim = await queue.ClaimNextGraphNodeAsync(
            "worker-1",
            JobGraphOrigin.Interactive,
            CancellationToken.None);
        var blockedClaim = await queue.ClaimNextGraphNodeAsync(
            "worker-2",
            JobGraphOrigin.Interactive,
            CancellationToken.None);

        Assert.Equal(root.Id, firstClaim?.Id);
        Assert.Null(blockedClaim);
        Assert.Equal(graph.Id, child.GraphId);
        Assert.Equal(root.Id, child.ParentRunId);

        await queue.CompleteAsync(root.Id, "done", CancellationToken.None);
        var childClaim = await queue.ClaimNextGraphNodeAsync(
            "worker-2",
            JobGraphOrigin.Interactive,
            CancellationToken.None);

        Assert.Equal(child.Id, childClaim?.Id);
    }

    [Fact]
    public async Task SeparateInteractiveGraphsCanRunConcurrently() {
        await using var db = CreateContext();
        var graphs = new JobGraphService(db);
        var queue = new JobQueueService(db);

        var first = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Interactive,
                "Identify One",
                new GraphJobNodeRequest("identify", new EnqueueJobRequest(JobType.IdentifySearch))),
            CancellationToken.None);
        var second = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Interactive,
                "Identify Two",
                new GraphJobNodeRequest("identify", new EnqueueJobRequest(JobType.IdentifySearch))),
            CancellationToken.None);

        var firstClaim = await queue.ClaimNextGraphNodeAsync(
            "worker-1",
            JobGraphOrigin.Interactive,
            CancellationToken.None);
        var secondClaim = await queue.ClaimNextGraphNodeAsync(
            "worker-2",
            JobGraphOrigin.Interactive,
            CancellationToken.None);

        Assert.NotNull(firstClaim);
        Assert.NotNull(secondClaim);
        Assert.NotEqual(firstClaim.GraphId, secondClaim.GraphId);
        Assert.Contains(firstClaim.GraphId!.Value, new[] { first.Id, second.Id });
        Assert.Contains(secondClaim.GraphId!.Value, new[] { first.Id, second.Id });
    }

    [Fact]
    public async Task DeclaredExternalResourceQueuesNodesAcrossIndependentGraphs() {
        await using var db = CreateContext();
        var graphs = new JobGraphService(db);
        var queue = new JobQueueService(db);
        await queue.DeclareResourceAsync("plugin:musicbrainz", 1, TimeSpan.Zero, CancellationToken.None);

        foreach (var title in new[] { "Album One", "Album Two" }) {
            await graphs.StartAsync(
                new StartJobGraphRequest(
                    JobGraphOrigin.Interactive,
                    title,
                    new GraphJobNodeRequest(
                        "identify",
                        new EnqueueJobRequest(JobType.IdentifySearch),
                        ResourceKey: "plugin:musicbrainz")),
                CancellationToken.None);
        }

        var first = await queue.ClaimNextGraphNodeAsync(
            "worker-1",
            JobGraphOrigin.Interactive,
            CancellationToken.None);
        var blocked = await queue.ClaimNextGraphNodeAsync(
            "worker-2",
            JobGraphOrigin.Interactive,
            CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(blocked);

        await queue.CompleteAsync(first.Id, "done", CancellationToken.None);
        var second = await queue.ClaimNextGraphNodeAsync(
            "worker-2",
            JobGraphOrigin.Interactive,
            CancellationToken.None);

        Assert.NotNull(second);
        Assert.NotEqual(first.Id, second.Id);
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"job-graphs-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }
}
