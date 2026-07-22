using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class ManualReplacementServiceTests {
    [Fact]
    public async Task FailedHandoffKeepsTheReviewedCandidateReplayableWithoutCreatingAnotherChild() {
        var entityId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var candidate = Candidate();
        var sessions = new ManualReplacementSearchSessionStore();
        var session = sessions.Create(entityId, [candidate]);
        var replacements = new RecordingReplacementStore(childId);
        var queue = new RecordingQueueService(childId) {
            Failure = new AcquisitionConfigurationException(
                ApiProblemCodes.DownloadClientUnreachable,
                "temporary handoff failure")
        };
        var service = new ManualReplacementService(replacements, null!, sessions, queue);

        await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            service.QueueAsync(entityId, session.Id, candidate.Id, CancellationToken.None));
        queue.Failure = null;

        var replay = await service.QueueAsync(
            entityId,
            session.Id,
            candidate.Id,
            CancellationToken.None);

        Assert.Equal(childId, replay.Summary.Id);
        Assert.Equal(2, replacements.CreateCalls);
        Assert.All(replacements.MaterializedChildIds, id => Assert.Equal(childId, id));
        Assert.Equal(2, queue.Calls);
    }

    private static ReviewedReleaseCandidate Candidate() => new(
        Guid.NewGuid(),
        new ScoredRelease(
            new IndexerRelease(
                "Hamilton album",
                1_000,
                5,
                1,
                DownloadProtocol.Soulseek,
                "slskd://reviewed-release",
                null,
                null,
                null,
                null,
                null),
            null,
            "Soulseek",
            true,
            1,
            []));

    private sealed class RecordingReplacementStore(Guid childId) : IManualReplacementStore {
        public int CreateCalls { get; private set; }
        public List<Guid> MaterializedChildIds { get; } = [];

        public Task<ManualReplacementSearchTarget?> GetSearchTargetAsync(
            Guid entityId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid?> CreateReviewedReplacementAsync(
            Guid entityId,
            IReadOnlyList<ReviewedReleaseCandidate> candidates,
            CancellationToken cancellationToken) {
            CreateCalls++;
            MaterializedChildIds.Add(childId);
            return Task.FromResult<Guid?>(childId);
        }
    }

    private sealed class RecordingQueueService(Guid childId) : IAcquisitionQueueService {
        public Exception? Failure { get; set; }
        public int Calls { get; private set; }

        public Task<AcquisitionDetail?> QueueAsync(
            Guid acquisitionId,
            Guid candidateId,
            CancellationToken cancellationToken,
            bool manualPick = false,
            AcquisitionStatus? requiredStatus = null) {
            Calls++;
            if (Failure is not null) {
                throw Failure;
            }

            return Task.FromResult<AcquisitionDetail?>(new AcquisitionDetail(
                new AcquisitionSummary(
                    childId,
                    AcquisitionStatus.Queued,
                    "Sent to download client.",
                    "Hamilton",
                    null,
                    null,
                    null,
                    null,
                    0,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    Kind: EntityKind.AudioLibrary),
                []));
        }
    }
}
