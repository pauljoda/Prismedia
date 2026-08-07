using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

/// <summary>
/// Covers the request-to-search ordering for structural media and the immediate child-search fallback
/// after a whole-unit search proves barren.
/// </summary>
public sealed class RequestAcquisitionWorkflowTests {
    [Fact]
    public async Task ArtistFanoutHydratesEveryAlbumBeforeStartingItsAcquisition() {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var events = new List<string>();
        var hydrator = new RecordingChildHydrator(events);
        var requests = new RecordingGraphAcquisitionStarter(events);
        var handler = new RequestAcquisitionFanoutJobHandler(
            requests,
            hydrator,
            NullLogger<RequestAcquisitionFanoutJobHandler>.Instance);
        var payload = new RequestAcquisitionFanoutPayload(
            [first, second],
            TargetLibraryRootId: null,
            ProfileId: null,
            HideNsfw: true,
            HydrateChildren: true);

        await handler.HandleAsync(
            new JobContext(Job(payload), new ProgressJobQueue()),
            CancellationToken.None);

        Assert.Equal([
            $"hydrate:{first}",
            $"start:{first}",
            $"hydrate:{second}",
            $"start:{second}"
        ], events);
        Assert.All(requests.ParentContexts, context => Assert.NotNull(context));
        Assert.All(requests.Origins, origin => Assert.Equal(JobGraphOrigin.Interactive, origin));
    }

    [Fact]
    public async Task ReviewedSeriesFanoutAppliesCachedEpisodesBeforeStartingSearch() {
        var season = Guid.NewGuid();
        var events = new List<string>();
        var hydrator = new RecordingChildHydrator(events);
        var requests = new RecordingGraphAcquisitionStarter(events);
        var handler = new RequestAcquisitionFanoutJobHandler(
            requests,
            hydrator,
            NullLogger<RequestAcquisitionFanoutJobHandler>.Instance);
        var payload = new RequestAcquisitionFanoutPayload(
            [season],
            TargetLibraryRootId: null,
            ProfileId: null,
            HideNsfw: true,
            HydrateChildren: false,
            ReviewedProposal: Proposal(season));

        await handler.HandleAsync(
            new JobContext(Job(payload), new ProgressJobQueue()),
            CancellationToken.None);

        Assert.Equal([$"metadata:{season}", $"start:{season}"], events);
    }

    [Fact]
    public async Task ReviewedMetadataFailureStopsFanoutBeforeAnySearchStarts() {
        var season = Guid.NewGuid();
        var events = new List<string>();
        var requests = new RecordingGraphAcquisitionStarter(events) { FailMetadataApply = true };
        var handler = new RequestAcquisitionFanoutJobHandler(
            requests,
            new RecordingChildHydrator(events),
            NullLogger<RequestAcquisitionFanoutJobHandler>.Instance);
        var payload = new RequestAcquisitionFanoutPayload(
            [season],
            TargetLibraryRootId: null,
            ProfileId: null,
            HideNsfw: true,
            HydrateChildren: false,
            ReviewedProposal: Proposal(season));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            new JobContext(Job(payload), new ProgressJobQueue()),
            CancellationToken.None));

        Assert.Equal([$"metadata:{season}"], events);
    }

    [Fact]
    public void LegacyFanoutPayloadStillHydratesShallowChildren() {
        var child = Guid.NewGuid();

        var payload = RequestAcquisitionFanoutPayload.Parse(
            $$"""{"childEntityIds":["{{child}}"],"targetLibraryRootId":null,"profileId":null,"hideNsfw":true}""");

        Assert.True(payload.HydrateChildren);
        Assert.Null(payload.ReviewedProposal);
    }

    [Fact]
    public async Task BarrenAlbumSearchImmediatelyRequestsItsWantedTracks() {
        var entityId = Guid.NewGuid();
        var requester = new RecordingMissingChildRequester();
        var fallback = new AcquisitionMissingChildFallback(
            requester,
            NullLogger<AcquisitionMissingChildFallback>.Instance);
        var input = new AcquisitionSearchInput(
            Guid.NewGuid(),
            "Atlas",
            "Divide Music",
            EntityKind.AudioLibrary,
            entityId);

        var outcome = await fallback.TryStartAsync(
            input,
            new AcquisitionSearchOutcome([], []),
            CancellationToken.None);

        Assert.Equal((7, 7), outcome);
        Assert.Equal([entityId], requester.EntityIds);
    }

    [Fact]
    public async Task AcceptedAlbumReleaseDoesNotStartTrackFallback() {
        var requester = new RecordingMissingChildRequester();
        var fallback = new AcquisitionMissingChildFallback(
            requester,
            NullLogger<AcquisitionMissingChildFallback>.Instance);
        var accepted = new ScoredRelease(
            new IndexerRelease(
                "Divide Music Atlas FLAC",
                100,
                Seeders: null,
                Peers: null,
                DownloadProtocol.Soulseek,
                DownloadUrl: "slskd:test",
                MagnetUrl: null,
                InfoHash: null,
                InfoUrl: null,
                Language: null,
                PublishedAt: null),
            IndexerConfigId: null,
            "Soulseek",
            Accepted: true,
            Score: 100,
            Rejections: []);
        var input = new AcquisitionSearchInput(
            Guid.NewGuid(),
            "Atlas",
            "Divide Music",
            EntityKind.AudioLibrary,
            Guid.NewGuid());

        var outcome = await fallback.TryStartAsync(
            input,
            new AcquisitionSearchOutcome([accepted], []),
            CancellationToken.None);

        Assert.Null(outcome);
        Assert.Empty(requester.EntityIds);
    }

    private static JobRunSnapshot Job(RequestAcquisitionFanoutPayload payload) {
        var now = DateTimeOffset.UtcNow;
        return new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.RequestAcquisitionFanout,
            JobRunStatus.Running,
            0,
            null,
            payload.ToJson(),
            EntityKind.MusicArtist.ToCode(),
            Guid.NewGuid().ToString(),
            "Divide Music",
            now,
            now,
            null,
            GraphId: Guid.NewGuid(),
            GraphOrigin: JobGraphOrigin.Interactive);
    }

    private static EntityMetadataProposal Proposal(Guid targetEntityId) => new(
        "reviewed-series",
        "tmdb",
        EntityKind.VideoSeries,
        1m,
        "external-id",
        new EntityMetadataPatch(
            "Ted Lasso",
            null,
            new Dictionary<string, string>(),
            [],
            [],
            null,
            [],
            new Dictionary<string, string>(),
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
            null),
        [],
        [],
        [],
        targetEntityId);

    private sealed class RecordingChildHydrator(List<string> events) : IRequestChildHydrator {
        public Task<RequestChildHydrationResult?> HydrateAsync(
            Guid entityId,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            events.Add($"hydrate:{entityId}");
            return Task.FromResult<RequestChildHydrationResult?>(
                new RequestChildHydrationResult(Hydrated: true, Enrichment: null));
        }
    }

    private sealed class RecordingGraphAcquisitionStarter(List<string> events) : IRequestGraphAcquisitionStarter {
        public List<JobContext?> ParentContexts { get; } = [];
        public List<JobGraphOrigin> Origins { get; } = [];
        public bool FailMetadataApply { get; init; }

        public Task ApplyReviewedMetadataAsync(
            Guid entityId,
            EntityMetadataProposal proposal,
            CancellationToken cancellationToken) {
            events.Add($"metadata:{entityId}");
            if (FailMetadataApply) {
                throw new InvalidOperationException("Metadata apply failed.");
            }
            return Task.CompletedTask;
        }

        public Task<RequestCommitResponse?> RequestEntityFromGraphAsync(
            Guid entityId,
            bool hideNsfw,
            CancellationToken cancellationToken,
            AcquisitionTargeting? targeting = null,
            BookRendition? bookRendition = null,
            bool hydrateChildren = true) {
            events.Add($"start:{entityId}");
            return Task.FromResult<RequestCommitResponse?>(new RequestCommitResponse(
                null,
                [new RequestCommitItem(
                    entityId.ToString(),
                    "Album",
                    RequestCommitOutcome.Requested,
                    entityId,
                    Guid.NewGuid())]));
        }

        public Task<RequestCommitResponse?> RequestEntityFromGraphAsync(
            Guid entityId,
            bool hideNsfw,
            CancellationToken cancellationToken,
            AcquisitionTargeting? targeting,
            BookRendition? bookRendition,
            bool hydrateChildren,
            JobContext? parentContext,
            JobGraphOrigin origin) {
            ParentContexts.Add(parentContext);
            Origins.Add(origin);
            return RequestEntityFromGraphAsync(
                entityId,
                hideNsfw,
                cancellationToken,
                targeting,
                bookRendition,
                hydrateChildren);
        }
    }

    private sealed class RecordingMissingChildRequester : IMissingChildAcquisitionRequester {
        public List<Guid> EntityIds { get; } = [];

        public Task<(int Covered, int Missing)> RequestMissingChildrenAsync(
            Guid entityId,
            CancellationToken cancellationToken) {
            EntityIds.Add(entityId);
            return Task.FromResult((Covered: 7, Missing: 7));
        }
    }

    private sealed class ProgressJobQueue : IJobQueueService {
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
