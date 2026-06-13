using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class BulkIdentifyJobHandlerTests {
    [Fact]
    public async Task HandleAsyncConvertsLegacyBatchIntoPerEntitySearchRequests() {
        var queue = new RecordingIdentifyQueueService();
        var handler = new BulkIdentifyJobHandler(queue, NullLogger<BulkIdentifyJobHandler>.Instance);
        var entityIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var query = new IdentifyQuery("Abbey Road", Url: null, ExternalIds: null);

        await handler.HandleAsync(
            new JobContext(CreateJob(entityIds, "musicbrainz", query, hideNsfw: true), new NoopJobQueue()),
            CancellationToken.None);

        var call = Assert.Single(queue.BatchRequests);
        Assert.Equal(entityIds, call.EntityIds);
        Assert.Equal("musicbrainz", call.Request.Provider);
        Assert.Equal("Abbey Road", call.Request.Query?.Title);
        Assert.True(call.HideNsfw);
    }

    private static JobRunSnapshot CreateJob(
        IReadOnlyList<Guid> entityIds,
        string provider,
        IdentifyQuery? query,
        bool hideNsfw) =>
        new(
            Guid.NewGuid(),
            JobType.BulkIdentify,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: new BulkIdentifyPayload(entityIds, provider, query, hideNsfw).ToJson(),
            TargetEntityKind: null,
            TargetEntityId: null,
            TargetLabel: "Bulk identify test",
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

    private sealed class RecordingIdentifyQueueService : IIdentifyQueueService {
        public List<(IReadOnlyList<Guid> EntityIds, IdentifyQueueSearchRequest Request, bool HideNsfw)> BatchRequests { get; } = [];

        public Task<IdentifyBulkAcceptedResponse> RequestSearchBatchAsync(
            IReadOnlyList<Guid> entityIds,
            IdentifyQueueSearchRequest request,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            BatchRequests.Add((entityIds, request, hideNsfw));
            return Task.FromResult(new IdentifyBulkAcceptedResponse(entityIds.Count, entityIds.Count));
        }

        public Task<IReadOnlyList<IdentifyQueueItem>> ListAsync(bool includeCompleted, bool hideNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentifyQueueItem> AddAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentifyQueueItem?> GetAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentifyQueueItem> RequestSearchAsync(Guid entityId, IdentifyQueueSearchRequest request, bool hideNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentifyQueueItem> ApplyAsync(Guid entityId, ApplyIdentifyQueueItemRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentifyQueueItem> SaveProposalAsync(Guid entityId, EntityMetadataProposal proposal, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentifyQueueItem?> DeleteAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoopJobQueue : IJobQueueService {
        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobRunSnapshot>>([]);

        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken, JobRunLane? lane = null) =>
            Task.FromResult<JobRunSnapshot?>(null);

        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeferAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobQueueCount>>([]);

        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
