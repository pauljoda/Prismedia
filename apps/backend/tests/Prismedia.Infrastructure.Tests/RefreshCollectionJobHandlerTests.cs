using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers.Maintenance;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class RefreshCollectionJobHandlerTests {
    [Fact]
    public async Task UntargetedRefreshUpdatesEveryDynamicCollection() {
        var firstOwnerId = Guid.NewGuid();
        var secondOwnerId = Guid.NewGuid();
        var first = new CollectionRefreshData(Guid.NewGuid(), "A", firstOwnerId, CollectionMode.Dynamic, RuleJson);
        var second = new CollectionRefreshData(Guid.NewGuid(), "B", secondOwnerId, CollectionMode.Hybrid, RuleJson);
        var matchId = Guid.NewGuid();
        var persistence = new RecordingCollectionRefreshPersistence([first, second]);
        var ruleEngine = new StaticRuleEngine([new CollectionRuleMatch(EntityKind.AudioTrack, matchId)]);
        var handler = new RefreshCollectionJobHandler(
            NullLogger<RefreshCollectionJobHandler>.Instance,
            persistence,
            ruleEngine);

        await handler.HandleAsync(new JobContext(Job(targetEntityId: null), new NoopJobQueue()), CancellationToken.None);

        Assert.Equal([first.EntityId, second.EntityId], persistence.Refreshes.Select(call => call.CollectionEntityId));
        Assert.Equal([firstOwnerId, secondOwnerId], ruleEngine.UserIds);
        Assert.All(persistence.Refreshes, call => {
            var item = Assert.Single(call.ResolvedItems);
            Assert.Equal(EntityKind.AudioTrack, item.EntityKind);
            Assert.Equal(matchId, item.EntityId);
        });
    }

    private const string RuleJson = """{"type":"group","operator":"and","children":[]}""";

    private static JobRunSnapshot Job(string? targetEntityId) {
        var now = DateTimeOffset.UtcNow;
        return new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.RefreshCollection,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: "{}",
            TargetEntityKind: null,
            TargetEntityId: targetEntityId,
            TargetLabel: null,
            CreatedAt: now,
            StartedAt: now,
            FinishedAt: null);
    }

    private sealed class RecordingCollectionRefreshPersistence(
        IReadOnlyList<CollectionRefreshData> collections) : ICollectionRefreshPersistence {
        public List<RefreshCall> Refreshes { get; } = [];

        public Task<CollectionRefreshData?> GetDynamicCollectionAsync(Guid collectionEntityId, CancellationToken cancellationToken) =>
            Task.FromResult(collections.FirstOrDefault(collection => collection.EntityId == collectionEntityId));

        public Task<IReadOnlyList<CollectionRefreshData>> ListDynamicCollectionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(collections);

        public Task RefreshCollectionItemsAsync(
            Guid collectionEntityId,
            IReadOnlyList<CollectionRuleMatch> resolvedItems,
            CancellationToken cancellationToken) {
            Refreshes.Add(new RefreshCall(collectionEntityId, resolvedItems));
            return Task.CompletedTask;
        }

        public sealed record RefreshCall(
            Guid CollectionEntityId,
            IReadOnlyList<CollectionRuleMatch> ResolvedItems);
    }

    private sealed class StaticRuleEngine(IReadOnlyList<CollectionRuleMatch> matches) : ICollectionRuleEngine {
        public List<Guid> UserIds { get; } = [];

        public Task<IReadOnlyList<CollectionRuleMatch>> EvaluateAsync(
            string ruleTreeJson,
            Guid userId,
            CancellationToken cancellationToken) {
            UserIds.Add(userId);
            return Task.FromResult(matches);
        }
    }

    private sealed class NoopJobQueue : IJobQueueService {
        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobRunSnapshot>>([]);

        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            Task.FromResult(Job(targetEntityId: null));

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Job(request.TargetEntityId));

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

        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken) =>
            Task.FromResult<JobRunSnapshot?>(null);

        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobQueueCount>>([]);

        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
