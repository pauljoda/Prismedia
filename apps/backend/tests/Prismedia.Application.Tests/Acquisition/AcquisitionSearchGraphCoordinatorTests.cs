using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class AcquisitionSearchGraphCoordinatorTests {
    [Fact]
    public async Task PrepareClosesPriorWaitingReviewBeforeANewSearchIsPublished() {
        var acquisitionId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var lifecycle = new RecordingLifecycle(acquisitionId, graphId);
        var graphs = new RecordingGraphs(Graph(graphId, acquisitionId, JobGraphStatus.Waiting));
        var coordinator = new AcquisitionSearchGraphCoordinator(lifecycle, graphs);

        var previous = await coordinator.PrepareAsync(acquisitionId, CancellationToken.None);

        Assert.Equal(graphId, previous);
        Assert.Equal(
            [(graphId, AcquisitionGraphSignals.Review(acquisitionId))],
            graphs.Resolved);
    }

    [Fact]
    public async Task PrepareDoesNotCloseAReviewSignalWhileItsSearchNodeIsStillRunning() {
        var acquisitionId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var lifecycle = new RecordingLifecycle(acquisitionId, graphId);
        var graphs = new RecordingGraphs(Graph(graphId, acquisitionId, JobGraphStatus.Running));
        var coordinator = new AcquisitionSearchGraphCoordinator(lifecycle, graphs);

        await coordinator.PrepareAsync(acquisitionId, CancellationToken.None);

        Assert.Empty(graphs.Resolved);
    }

    [Fact]
    public async Task LinkAtomicallyMovesTheAcquisitionFromItsPriorGraphToTheNewSearchGraph() {
        var acquisitionId = Guid.NewGuid();
        var oldGraphId = Guid.NewGuid();
        var newGraphId = Guid.NewGuid();
        var lifecycle = new RecordingLifecycle(acquisitionId, oldGraphId);
        var coordinator = new AcquisitionSearchGraphCoordinator(lifecycle, graphs: null);

        await coordinator.LinkAsync(acquisitionId, oldGraphId, newGraphId, CancellationToken.None);

        Assert.Equal(newGraphId, lifecycle.GraphId);
        Assert.Equal([(acquisitionId, (Guid?)oldGraphId, newGraphId)], lifecycle.Relinks);
    }

    private static JobGraphDetailSnapshot Graph(
        Guid graphId,
        Guid acquisitionId,
        JobGraphStatus status) {
        var now = DateTimeOffset.UtcNow;
        return new JobGraphDetailSnapshot(
            new JobGraphSnapshot(
                graphId,
                graphId,
                JobGraphOrigin.Background,
                status,
                "Acquisition search",
                Guid.NewGuid(),
                null,
                null,
                null,
                now,
                now),
            [],
            [],
            [new JobGraphSignalSnapshot(
                Guid.NewGuid(),
                graphId,
                AcquisitionGraphSignals.Review(acquisitionId),
                JobGraphSignalKind.DomainEvent,
                acquisitionId.ToString(),
                "Waiting for release review",
                now,
                null,
                null)]);
    }

    private sealed class RecordingLifecycle(Guid acquisitionId, Guid? graphId)
        : IAcquisitionLifecycleStore {
        public Guid? GraphId { get; private set; } = graphId;
        public List<(Guid AcquisitionId, Guid? ExpectedGraphId, Guid GraphId)> Relinks { get; } = [];

        public Task<Guid?> GetJobGraphIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == acquisitionId ? GraphId : null);

        public Task<bool> TryRelinkJobGraphIdAsync(
            Guid id,
            Guid? expectedGraphId,
            Guid replacementGraphId,
            CancellationToken cancellationToken) {
            Relinks.Add((id, expectedGraphId, replacementGraphId));
            if (id != acquisitionId || GraphId != expectedGraphId) {
                return Task.FromResult(false);
            }

            GraphId = replacementGraphId;
            return Task.FromResult(true);
        }

        public Task<AcquisitionStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> TryTransitionStatusAsync(
            Guid id,
            IReadOnlyCollection<AcquisitionStatus> expectedStatuses,
            AcquisitionStatus status,
            string? message,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> TryClaimFailedRecoveryAsync(
            Guid id,
            IReadOnlyCollection<AcquisitionStatus> expectedStatuses,
            SelectedRelease? expectedSelectedRelease,
            string message,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingGraphs(JobGraphDetailSnapshot graph) : IJobGraphService {
        public List<(Guid GraphId, string Key)> Resolved { get; } = [];

        public Task<JobGraphDetailSnapshot?> GetAsync(Guid graphId, CancellationToken cancellationToken) =>
            Task.FromResult<JobGraphDetailSnapshot?>(graph.Graph.Id == graphId ? graph : null);

        public Task<JobGraphSignalSnapshot> ResolveSignalAsync(
            Guid graphId,
            string key,
            IReadOnlyList<GraphJobNodeRequest> continuationNodes,
            CancellationToken cancellationToken) {
            Resolved.Add((graphId, key));
            return Task.FromResult(graph.Signals.Single(signal => signal.Key == key));
        }

        public Task<JobGraphSnapshot> StartAsync(StartJobGraphRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> AppendNodeAsync(Guid graphId, GraphJobNodeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobGraphSignalSnapshot> OpenSignalAsync(Guid graphId, string key, JobGraphSignalKind kind, string? correlationId, string? message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobGraphSnapshot>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancelAsync(Guid graphId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
