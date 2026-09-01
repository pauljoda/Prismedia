using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
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
                        TargetEntityKind: EntityKind.AudioLibrary.ToCode(),
                        TargetEntityId: entityId,
                        TargetLabel: "Album")),
                RootEntityKind: EntityKind.AudioLibrary.ToCode(),
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
    public async Task RequiredBackgroundWorkPreemptsOlderBestEffortGraphs() {
        await using var db = CreateContext();
        var graphs = new JobGraphService(db);
        var queue = new JobQueueService(db);

        var bestEffort = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Background,
                "Generate preview",
                new GraphJobNodeRequest(
                    "preview",
                    new EnqueueJobRequest(JobType.GeneratePreview),
                    Importance: JobNodeImportance.BestEffort)),
            CancellationToken.None);
        var required = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Background,
                "Probe video",
                new GraphJobNodeRequest(
                    "probe",
                    new EnqueueJobRequest(JobType.ProbeVideo),
                    Importance: JobNodeImportance.Required)),
            CancellationToken.None);

        var claimed = await queue.ClaimNextGraphNodeAsync(
            "worker",
            JobGraphOrigin.Background,
            CancellationToken.None);

        Assert.Equal(required.Id, claimed?.GraphId);
        Assert.NotEqual(bestEffort.Id, claimed?.GraphId);
        Assert.Equal(JobType.ProbeVideo, claimed?.Type);
    }

    [Fact]
    public async Task BestEffortBackgroundWorkPreemptsOlderDeferredGraphs() {
        await using var db = CreateContext();
        var graphs = new JobGraphService(db);
        var queue = new JobQueueService(db);

        var deferred = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Background,
                "Generate trickplay",
                new GraphJobNodeRequest(
                    "trickplay",
                    new EnqueueJobRequest(JobType.GenerateTrickplay),
                    Importance: JobNodeImportance.Deferred)),
            CancellationToken.None);
        var bestEffort = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Background,
                "Generate preview",
                new GraphJobNodeRequest(
                    "preview",
                    new EnqueueJobRequest(JobType.GeneratePreview),
                    Importance: JobNodeImportance.BestEffort)),
            CancellationToken.None);

        var claimed = await queue.ClaimNextGraphNodeAsync(
            "worker",
            JobGraphOrigin.Background,
            CancellationToken.None);

        Assert.Equal(bestEffort.Id, claimed?.GraphId);
        Assert.NotEqual(deferred.Id, claimed?.GraphId);
        Assert.Equal(JobType.GeneratePreview, claimed?.Type);
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

    [Fact]
    public async Task CompletingTheLastNodeCompletesItsGraph() {
        await using var db = CreateContext();
        var graphs = new JobGraphService(db);
        var queue = new JobQueueService(db);
        var graph = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Background,
                "Maintenance",
                new GraphJobNodeRequest("root", new EnqueueJobRequest(JobType.Noop))),
            CancellationToken.None);
        var run = await queue.ClaimNextGraphNodeAsync("worker", JobGraphOrigin.Background, CancellationToken.None);

        await queue.CompleteAsync(run!.Id, "done", CancellationToken.None);

        Assert.Equal(
            JobGraphStatus.Completed,
            (await db.JobGraphs.AsNoTracking().SingleAsync(row => row.Id == graph.Id)).Status);
    }

    [Fact]
    public async Task BestEffortTerminalFailureCompletesGraphWithWarnings() {
        await using var db = CreateContext();
        var graphs = new JobGraphService(db);
        var queue = new JobQueueService(db);
        var graph = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Interactive,
                "Artwork",
                new GraphJobNodeRequest(
                    "artwork",
                    new EnqueueJobRequest(JobType.GenerateGridThumbnail),
                    Importance: JobNodeImportance.BestEffort)),
            CancellationToken.None);
        var row = await db.JobRuns.SingleAsync();
        row.MaxAttempts = 1;
        await db.SaveChangesAsync();
        var run = await queue.ClaimNextGraphNodeAsync("worker", JobGraphOrigin.Interactive, CancellationToken.None);

        await queue.FailAsync(run!.Id, "missing artwork", TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(
            JobGraphStatus.CompletedWithWarnings,
            (await db.JobGraphs.AsNoTracking().SingleAsync(item => item.Id == graph.Id)).Status);
    }

    [Fact]
    public async Task RequiredFailureSkipsDependentDescendantsButLetsIndependentBranchFinish() {
        await using var db = CreateContext();
        var graphs = new JobGraphService(db);
        var queue = new JobQueueService(db);
        var graph = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Background,
                "Branches",
                new GraphJobNodeRequest("required", new EnqueueJobRequest(JobType.Noop))),
            CancellationToken.None);
        var root = await db.JobRuns.SingleAsync();
        root.MaxAttempts = 1;
        await db.SaveChangesAsync();
        await graphs.AppendNodeAsync(
            graph.Id,
            new GraphJobNodeRequest(
                "dependent",
                new EnqueueJobRequest(JobType.ProbeVideo),
                DependsOn: [root.Id]),
            CancellationToken.None);
        var independent = await graphs.AppendNodeAsync(
            graph.Id,
            new GraphJobNodeRequest("independent", new EnqueueJobRequest(JobType.Noop)),
            CancellationToken.None);
        var claimed = await queue.ClaimNextGraphNodeAsync("worker", JobGraphOrigin.Background, CancellationToken.None);

        await queue.FailAsync(claimed!.Id, "required failed", TimeSpan.Zero, CancellationToken.None);

        var dependent = await db.JobRuns.AsNoTracking().SingleAsync(run => run.NodeKey == "dependent");
        Assert.Equal(JobRunStatus.Cancelled, dependent.Status);
        var independentClaim = await queue.ClaimNextGraphNodeAsync("worker", JobGraphOrigin.Background, CancellationToken.None);
        Assert.Equal(independent.Id, independentClaim?.Id);

        await queue.CompleteAsync(independent.Id, "done", CancellationToken.None);

        Assert.Equal(
            JobGraphStatus.Failed,
            (await db.JobGraphs.AsNoTracking().SingleAsync(item => item.Id == graph.Id)).Status);
    }

    [Fact]
    public async Task SignalWaitReleasesTheLaneAndResolutionAppendsContinuationIdempotently() {
        await using var db = CreateContext();
        var graphs = new JobGraphService(db);
        var queue = new JobQueueService(db);
        var graph = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Interactive,
                "Identify Review",
                new GraphJobNodeRequest("search", new EnqueueJobRequest(JobType.IdentifySearch))),
            CancellationToken.None);
        var search = await queue.ClaimNextGraphNodeAsync("worker", JobGraphOrigin.Interactive, CancellationToken.None);
        await graphs.OpenSignalAsync(
            graph.Id,
            "review",
            JobGraphSignalKind.IdentifyReview,
            "queue-item-1",
            "Waiting for review",
            CancellationToken.None);
        await queue.CompleteAsync(search!.Id, "candidates ready", CancellationToken.None);

        Assert.Null(await queue.ClaimNextGraphNodeAsync("worker", JobGraphOrigin.Interactive, CancellationToken.None));
        Assert.Equal(JobGraphStatus.Waiting, (await graphs.GetAsync(graph.Id, CancellationToken.None))!.Graph.Status);

        var continuation = new GraphJobNodeRequest(
            "apply",
            new EnqueueJobRequest(JobType.ImportMetadata),
            DependsOn: [search.Id]);
        await graphs.ResolveSignalAsync(graph.Id, "review", [continuation], CancellationToken.None);
        await graphs.ResolveSignalAsync(graph.Id, "review", [continuation], CancellationToken.None);

        Assert.Single((await graphs.GetAsync(graph.Id, CancellationToken.None))!.Nodes, node => node.NodeKey == "apply");
        Assert.NotNull(await queue.ClaimNextGraphNodeAsync("worker", JobGraphOrigin.Interactive, CancellationToken.None));
    }

    [Fact]
    public async Task SignalContinuationDeclaresItsEntityResourceBeforeItBecomesRunnable() {
        await using var db = CreateContext();
        var graphs = new JobGraphService(db);
        var queue = new JobQueueService(db);
        var graph = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Interactive,
                "Downloaded movie",
                new GraphJobNodeRequest("search", new EnqueueJobRequest(JobType.AcquisitionSearch))),
            CancellationToken.None);
        var search = await queue.ClaimNextGraphNodeAsync(
            "worker",
            JobGraphOrigin.Interactive,
            CancellationToken.None);
        await graphs.OpenSignalAsync(
            graph.Id,
            "download",
            JobGraphSignalKind.ExternalTransfer,
            null,
            "Waiting for download",
            CancellationToken.None);
        await queue.CompleteAsync(search!.Id, "download submitted", CancellationToken.None);

        var entityId = Guid.NewGuid();
        await graphs.ResolveSignalAsync(
            graph.Id,
            "download",
            [new GraphJobNodeRequest(
                "import",
                new EnqueueJobRequest(
                    JobType.AcquisitionImport,
                    TargetEntityKind: EntityKind.Movie.ToCode(),
                    TargetEntityId: Guid.NewGuid().ToString()),
                ResourceKey: JobResourceKeys.Entity(entityId.ToString()))],
            CancellationToken.None);

        Assert.Contains(
            await db.JobResourceStates.AsNoTracking().ToArrayAsync(),
            resource => resource.Key == JobResourceKeys.Entity(entityId.ToString()));
        var import = await queue.ClaimNextGraphNodeAsync(
            "worker",
            JobGraphOrigin.Interactive,
            CancellationToken.None);
        Assert.Equal(JobType.AcquisitionImport, import?.Type);
    }

    [Fact]
    public async Task RecoveryReconcilesAnActiveGraphWhoseNodesAreAlreadyTerminal() {
        await using var db = CreateContext();
        var graphs = new JobGraphService(db);
        var queue = new JobQueueService(db);
        var graph = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Background,
                "Broken scan",
                new GraphJobNodeRequest("scan", new EnqueueJobRequest(JobType.ScanAudio))),
            CancellationToken.None);
        var run = await db.JobRuns.SingleAsync();
        run.Status = JobRunStatus.Failed;
        run.FinishedAt = DateTimeOffset.UtcNow;
        var graphRow = await db.JobGraphs.SingleAsync();
        graphRow.Status = JobGraphStatus.Running;
        graphRow.FinishedAt = null;
        await db.SaveChangesAsync();

        await queue.RecoverStaleRunningAsync(
            "worker",
            TimeSpan.FromMinutes(1),
            CancellationToken.None);

        db.ChangeTracker.Clear();
        var repaired = await db.JobGraphs.SingleAsync(row => row.Id == graph.Id);
        Assert.Equal(JobGraphStatus.Failed, repaired.Status);
        Assert.NotNull(repaired.FinishedAt);
    }

    [Fact]
    public async Task RecoveryDeclaresEntityResourcesForPreviouslyQueuedNodes() {
        await using var db = CreateContext();
        var graphs = new JobGraphService(db);
        var queue = new JobQueueService(db);
        var graph = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Interactive,
                "Existing downloaded movie",
                new GraphJobNodeRequest("import", new EnqueueJobRequest(JobType.AcquisitionImport))),
            CancellationToken.None);
        var entityResource = JobResourceKeys.Entity(Guid.NewGuid().ToString());
        var run = await db.JobRuns.SingleAsync(row => row.GraphId == graph.Id);
        run.ResourceKey = entityResource;
        await db.SaveChangesAsync();

        await queue.RecoverStaleRunningAsync(
            "worker",
            TimeSpan.FromMinutes(1),
            CancellationToken.None);

        Assert.Contains(
            await db.JobResourceStates.AsNoTracking().ToArrayAsync(),
            resource => resource.Key == entityResource);
        Assert.NotNull(await queue.ClaimNextGraphNodeAsync(
            "worker",
            JobGraphOrigin.Interactive,
            CancellationToken.None));
    }

    [Fact]
    public async Task CancellingGraphCancelsOpenSignalsNodesAndResourceLeases() {
        await using var db = CreateContext();
        var graphs = new JobGraphService(db);
        var queue = new JobQueueService(db);
        await queue.DeclareResourceAsync("plugin:test", 1, TimeSpan.Zero, CancellationToken.None);
        var graph = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Interactive,
                "Provider",
                new GraphJobNodeRequest(
                    "provider",
                    new EnqueueJobRequest(JobType.IdentifySearch),
                    ResourceKey: "plugin:test")),
            CancellationToken.None);
        await graphs.OpenSignalAsync(
            graph.Id,
            "download",
            JobGraphSignalKind.ExternalTransfer,
            null,
            null,
            CancellationToken.None);
        var run = await queue.ClaimNextGraphNodeAsync("worker", JobGraphOrigin.Interactive, CancellationToken.None);

        Assert.True(await graphs.CancelAsync(graph.Id, CancellationToken.None));

        var detail = await graphs.GetAsync(graph.Id, CancellationToken.None);
        Assert.Equal(JobGraphStatus.Cancelled, detail!.Graph.Status);
        Assert.Equal(JobRunStatus.Cancelled, Assert.Single(detail.Nodes).Status);
        Assert.NotNull(Assert.Single(detail.Signals).CancelledAt);
        Assert.Empty(await db.JobResourceLeases.AsNoTracking().ToArrayAsync());
        Assert.True(await queue.IsRunCancelledAsync(run!.Id, CancellationToken.None));
    }

    [Fact]
    public async Task HiddenNsfwEntityGraphsAreExcludedFromListAndDetail() {
        await using var db = CreateContext();
        var graphs = new JobGraphService(db);
        var entityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = EntityKind.Movie.ToCode(),
            Title = "Hidden Movie",
            IsNsfw = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var graph = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Interactive,
                "Identify Hidden Movie",
                new GraphJobNodeRequest("identify", new EnqueueJobRequest(JobType.IdentifySearch)),
                RootEntityKind: EntityKind.Movie.ToCode(),
                RootEntityId: entityId.ToString()),
            CancellationToken.None);

        Assert.Single(await graphs.ListAsync(hideNsfw: false, CancellationToken.None));
        Assert.Empty(await graphs.ListAsync(hideNsfw: true, CancellationToken.None));
        Assert.NotNull(await graphs.GetAsync(graph.Id, hideNsfw: false, CancellationToken.None));
        Assert.Null(await graphs.GetAsync(graph.Id, hideNsfw: true, CancellationToken.None));
    }

    [Fact]
    public async Task HiddenNsfwLibraryRootGraphsAreExcludedFromListAndDetail() {
        await using var db = CreateContext();
        var graphs = new JobGraphService(db);
        var safeRootId = Guid.NewGuid();
        var hiddenRootId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.LibraryRoots.AddRange(
            new LibraryRootRow {
                Id = safeRootId,
                Path = "/media/safe",
                Label = "Safe Library",
                CreatedAt = now,
                UpdatedAt = now
            },
            new LibraryRootRow {
                Id = hiddenRootId,
                Path = "/media/hidden",
                Label = "Hidden Library",
                IsNsfw = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();
        var safeGraph = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Background,
                "Safe Library",
                new GraphJobNodeRequest("scan", new EnqueueJobRequest(JobType.ScanLibrary)),
                RootEntityKind: JobTargetKinds.LibraryRoot,
                RootEntityId: safeRootId.ToString()),
            CancellationToken.None);
        var hiddenGraph = await graphs.StartAsync(
            new StartJobGraphRequest(
                JobGraphOrigin.Background,
                "Hidden Library",
                new GraphJobNodeRequest("scan", new EnqueueJobRequest(JobType.ScanLibrary)),
                RootEntityKind: JobTargetKinds.LibraryRoot,
                RootEntityId: hiddenRootId.ToString()),
            CancellationToken.None);

        Assert.Equal(2, (await graphs.ListAsync(hideNsfw: false, CancellationToken.None)).Count);
        var visibleGraphs = await graphs.ListAsync(hideNsfw: true, CancellationToken.None);
        Assert.Contains(visibleGraphs, graph => graph.Id == safeGraph.Id);
        Assert.DoesNotContain(visibleGraphs, graph => graph.Id == hiddenGraph.Id);
        Assert.NotNull(await graphs.GetAsync(safeGraph.Id, hideNsfw: true, CancellationToken.None));
        Assert.Null(await graphs.GetAsync(hiddenGraph.Id, hideNsfw: true, CancellationToken.None));
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"job-graphs-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }
}
