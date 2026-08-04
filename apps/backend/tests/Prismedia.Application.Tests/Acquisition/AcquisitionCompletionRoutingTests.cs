using System.Reflection;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class AcquisitionCompletionRoutingTests {
    [Theory]
    [InlineData(JobGraphStatus.Completed)]
    [InlineData(JobGraphStatus.Waiting)]
    public async Task CompletedDownloadFallsBackWhenItsLinkedWorkflowCannotAcceptTheImport(
        JobGraphStatus linkedGraphStatus) {
        var acquisitionId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var staleGraphId = Guid.NewGuid();
        var freshGraphId = Guid.NewGuid();
        var store = DispatchProxy.Create<IAcquisitionStore, CompletionAcquisitionStore>();
        var storeProxy = (CompletionAcquisitionStore)(object)store;
        storeProxy.Detail = DownloadedAlbum(acquisitionId, entityId, staleGraphId);
        var queue = new CompletionJobQueue(freshGraphId);
        var graphs = new RejectingCompletionGraph(staleGraphId, linkedGraphStatus);
        var service = new AcquisitionCompletionService(store, queue, graphs);

        await service.ScheduleAsync(acquisitionId, CancellationToken.None, JobGraphOrigin.Background);

        var request = Assert.Single(queue.Requests);
        Assert.Equal(JobType.AcquisitionImport, request.Type);
        Assert.Equal(acquisitionId.ToString(), request.TargetEntityId);
        Assert.Equal(entityId.ToString(), request.GraphRootEntityId);
        Assert.Equal(JobGraphOrigin.Background, request.Origin);
    }

    [Theory]
    [InlineData(EntityKind.Book, true, JobType.AcquisitionUpgradeReplace)]
    [InlineData(EntityKind.Movie, true, JobType.AcquisitionUpgradeReplace)]
    [InlineData(EntityKind.Video, true, JobType.AcquisitionUpgradeReplace)]
    [InlineData(EntityKind.VideoEpisode, true, JobType.AcquisitionUpgradeReplace)]
    [InlineData(EntityKind.VideoSeason, true, JobType.AcquisitionImport)]
    [InlineData(EntityKind.AudioLibrary, true, JobType.AcquisitionImport)]
    [InlineData(EntityKind.AudioTrack, true, JobType.AcquisitionImport)]
    [InlineData(EntityKind.AudioLibrary, false, JobType.AcquisitionImport)]
    public void RoutesSingleFileAndAlbumCompletionsToTheirOwningWorkflow(
        EntityKind kind,
        bool isUpgrade,
        JobType expected) {
        Assert.Equal(expected, AcquisitionCompletionService.CompletionJobType(kind, isUpgrade));
    }

    [Fact]
    public void EveryRequestAcquisitionKindHasDefinitionOwnedCompletionRouting() {
        var acquisitionKinds = RequestKindRegistry.All
            .Select(descriptor => descriptor.AcquisitionKind)
            .Distinct()
            .Order()
            .ToArray();

        foreach (var kind in acquisitionKinds) {
            var definition = EntityKindRegistry.Describe(kind);
            var expected = definition.UpgradeMode == EntityUpgradeMode.Import
                ? JobType.AcquisitionImport
                : JobType.AcquisitionUpgradeReplace;
            Assert.Equal(expected, AcquisitionCompletionService.CompletionJobType(kind, isUpgrade: true));
        }
    }

    private static AcquisitionDetail DownloadedAlbum(Guid acquisitionId, Guid entityId, Guid graphId) =>
        new(
            new AcquisitionSummary(
                acquisitionId,
                AcquisitionStatus.Downloaded,
                "Download complete; importing.",
                "Frozen",
                null,
                null,
                2013,
                null,
                1,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                Kind: EntityKind.AudioLibrary,
                EntityId: entityId,
                JobGraphId: graphId),
            []);

    public class CompletionAcquisitionStore : DispatchProxy {
        public AcquisitionDetail? Detail { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch {
                nameof(IAcquisitionStore.GetAsync) => Task.FromResult(Detail),
                nameof(IAcquisitionStore.GetUpgradeOwnedQualityAsync) => Task.FromResult<UpgradeOwnedQuality?>(null),
                nameof(IAcquisitionStore.SetJobGraphIdAsync) => Task.CompletedTask,
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
    }

    private sealed class CompletionJobQueue(Guid graphId) : IJobQueueService {
        public List<EnqueueJobRequest> Requests { get; } = [];

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            Requests.Add(request);
            return Task.FromResult(new JobRunSnapshot(
                Guid.NewGuid(),
                request.Type,
                JobRunStatus.Queued,
                0,
                null,
                request.PayloadJson ?? "{}",
                request.TargetEntityKind,
                request.TargetEntityId,
                request.TargetLabel,
                DateTimeOffset.UtcNow,
                null,
                null,
                GraphId: graphId,
                GraphOrigin: request.Origin));
        }

        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RejectingCompletionGraph(Guid graphId, JobGraphStatus status) : IJobGraphService {
        private readonly JobGraphDetailSnapshot _detail = new(
            new JobGraphSnapshot(
                graphId,
                graphId,
                JobGraphOrigin.Interactive,
                status,
                "Frozen",
                Guid.NewGuid(),
                null,
                EntityKind.AudioLibrary.ToCode(),
                Guid.NewGuid().ToString(),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow),
            [],
            [],
            []);

        public Task<JobGraphDetailSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<JobGraphDetailSnapshot?>(id == graphId ? _detail : null);
        public Task<JobRunSnapshot> AppendNodeAsync(Guid id, GraphJobNodeRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The linked workflow is already terminal.");
        public Task<JobGraphSnapshot> StartAsync(StartJobGraphRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<JobGraphSignalSnapshot> OpenSignalAsync(Guid id, string key, JobGraphSignalKind kind, string? correlationId, string? message, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<JobGraphSignalSnapshot> ResolveSignalAsync(Guid id, string key, IReadOnlyList<GraphJobNodeRequest> continuationNodes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<JobGraphSnapshot>> ListAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
