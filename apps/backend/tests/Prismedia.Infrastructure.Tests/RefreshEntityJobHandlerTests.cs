using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers.Maintenance;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class RefreshEntityJobHandlerTests {
    [Fact]
    public async Task VideoRefreshDiscoversAndInvalidatesCurrentSidecarsBeforeQueueingExtraction() {
        var videoId = Guid.NewGuid();
        const string sourcePath = "/media/videos/movie.mkv";
        var signature = new string('a', 64);
        var persistence = new RecordingPersistence([
            new EntityRefreshTarget(videoId, EntityKindRegistry.Video.Code, "Movie", sourcePath)
        ]);
        var discovery = new StubSubtitleSidecarDiscovery([
            new VideoSubtitleSidecarDiscovery(sourcePath, [], signature, IsComplete: true)
        ]);
        var queue = new RecordingJobQueue();
        var handler = CreateHandler(persistence, discovery);

        await handler.HandleAsync(
            new JobContext(RefreshJob(videoId), queue),
            CancellationToken.None);

        Assert.Equal([sourcePath], discovery.RequestedPaths);
        var state = Assert.Single(persistence.InvalidatedSubtitleStates);
        Assert.Equal(videoId, state.EntityId);
        Assert.Equal(signature, state.Signature);
        var request = Assert.Single(queue.Enqueued, item => item.Type == JobType.ExtractSubtitles);
        Assert.Equal(videoId.ToString(), request.TargetEntityId);
        Assert.Equal(1, persistence.ClearGeneratedAssetsCalls);
    }

    [Fact]
    public async Task IncompleteSidecarDiscoveryFailsBeforeRefreshMutatesOrQueuesWork() {
        var videoId = Guid.NewGuid();
        const string sourcePath = "/media/videos/movie.mkv";
        var persistence = new RecordingPersistence([
            new EntityRefreshTarget(videoId, EntityKindRegistry.Video.Code, "Movie", sourcePath)
        ]);
        var discovery = new StubSubtitleSidecarDiscovery([
            new VideoSubtitleSidecarDiscovery(sourcePath, [], new string('b', 64), IsComplete: false)
        ]);
        var queue = new RecordingJobQueue();
        var handler = CreateHandler(persistence, discovery);

        await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync(
            new JobContext(RefreshJob(videoId), queue),
            CancellationToken.None));

        Assert.Empty(persistence.InvalidatedSubtitleStates);
        Assert.Equal(0, persistence.ClearGeneratedAssetsCalls);
        Assert.Equal(0, persistence.DownstreamChecks);
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task ReconciliationFinalizerDependsOnRequiredProbeAndPrecedesBestEffortBranches() {
        var videoId = Guid.NewGuid();
        var acquisitionId = Guid.NewGuid();
        var ancestorId = Guid.NewGuid();
        var persistence = new RecordingPersistence([
            new EntityRefreshTarget(videoId, EntityKindRegistry.Video.Code, "Movie")
        ]) {
            AutoGenerateMetadata = true,
            NeedsProbe = true,
            NeedsPreview = true
        };
        var planner = new EntityProcessingGraphPlanner(
            NullLogger<EntityProcessingGraphPlanner>.Instance,
            persistence,
            persistence,
            persistence,
            persistence,
            new StubSubtitleSidecarDiscovery([]),
            persistence);
        var queue = new RecordingJobQueue();
        var finalization = AcquisitionFinalizeJobPayload.Create(
            acquisitionId,
            BookQualityRank.Floor,
            "Imported",
            touchedAncestorIds: [ancestorId]);
        var job = RefreshJob(videoId) with {
            Type = JobType.ReconcileEntity,
            PayloadJson = finalization.ToJson(),
            GraphId = Guid.NewGuid(),
            GraphOrigin = JobGraphOrigin.Interactive
        };

        await new ReconcileEntityJobHandler(planner).HandleAsync(
            new JobContext(job, queue),
            CancellationToken.None);

        var probe = Assert.Single(queue.Nodes, node => node.Job.Type == JobType.ProbeVideo);
        var finalizer = Assert.Single(queue.Nodes, node => node.Job.Type == JobType.AcquisitionFinalize);
        var preview = Assert.Single(queue.Nodes, node => node.Job.Type == JobType.GeneratePreview);
        var ancestorProjection = Assert.Single(
            queue.Nodes,
            node => node.Job.Type == JobType.GenerateGridThumbnail && node.Job.TargetEntityId == ancestorId.ToString());
        Assert.Contains(queue.RunIds[probe.NodeKey], finalizer.DependsOn!);
        Assert.Contains(queue.RunIds[finalizer.NodeKey], ancestorProjection.DependsOn!);
        Assert.True(queue.Nodes.IndexOf(finalizer) < queue.Nodes.IndexOf(preview));
        Assert.True(queue.Nodes.IndexOf(preview) < queue.Nodes.IndexOf(ancestorProjection));
        Assert.Equal(JobNodeImportance.Required, finalizer.Importance);
        Assert.Equal(JobNodeImportance.BestEffort, preview.Importance);
    }

    private static RefreshEntityJobHandler CreateHandler(
        RecordingPersistence persistence,
        ISubtitleSidecarDiscovery discovery) =>
        new(new EntityProcessingGraphPlanner(
            NullLogger<EntityProcessingGraphPlanner>.Instance,
            persistence,
            persistence,
            persistence,
            persistence,
            discovery,
            persistence));

    private static JobRunSnapshot RefreshJob(Guid entityId) {
        var now = DateTimeOffset.UtcNow;
        return new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.RefreshEntity,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: "{}",
            TargetEntityKind: EntityKindRegistry.Video.Code,
            TargetEntityId: entityId.ToString(),
            TargetLabel: "Movie",
            CreatedAt: now,
            StartedAt: now,
            FinishedAt: null);
    }

    private sealed class StubSubtitleSidecarDiscovery(
        IReadOnlyList<VideoSubtitleSidecarDiscovery> results) : ISubtitleSidecarDiscovery {
        public IReadOnlyList<string> RequestedPaths { get; private set; } = [];

        public Task<IReadOnlyList<VideoSubtitleSidecarDiscovery>> DiscoverAsync(
            IReadOnlyCollection<string> videoPaths,
            CancellationToken cancellationToken) {
            RequestedPaths = videoPaths.ToArray();
            return Task.FromResult(results);
        }
    }

    private sealed class RecordingPersistence(IReadOnlyList<EntityRefreshTarget> tree) :
        IEntityRefreshTreePersistence,
        ILibraryScanRootPersistence,
        IDownstreamNeedsPersistence,
        IMaintenancePersistence,
        IVideoScanPersistence {
        private static readonly DownstreamNeeds NoDownstreamWork = new(
            NeedsProbe: false,
            MissingOshash: false,
            MissingMd5: false,
            NeedsPreview: false,
            NeedsTrickplay: false,
            NeedsSubtitleExtraction: false,
            NeedsGridThumbnail: false);

        public List<VideoSubtitleSidecarState> InvalidatedSubtitleStates { get; } = [];
        public int ClearGeneratedAssetsCalls { get; private set; }
        public int DownstreamChecks { get; private set; }
        public bool AutoGenerateMetadata { get; init; }
        public bool NeedsProbe { get; init; }
        public bool NeedsPreview { get; init; }

        public Task<IReadOnlyList<EntityRefreshTarget>> GetEntityTreeAsync(
            Guid entityId,
            CancellationToken cancellationToken) => Task.FromResult(tree);

        public Task<LibrarySettingsData> GetSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new LibrarySettingsData(
                AutoGenerateMetadata,
                AutoGenerateOshash: false,
                AutoGenerateMd5: false,
                AutoGeneratePreview: NeedsPreview,
                GenerateTrickplay: false,
                TrickplayIntervalSeconds: 10,
                PreviewClipDurationSeconds: 8,
                ThumbnailQuality: 2,
                TrickplayQuality: 2));

        public Task<IReadOnlyDictionary<Guid, DownstreamNeeds>> CheckDownstreamNeedsBatchAsync(
            IReadOnlyList<Guid> entityIds,
            CancellationToken cancellationToken) {
            DownstreamChecks++;
            return Task.FromResult<IReadOnlyDictionary<Guid, DownstreamNeeds>>(
                entityIds.ToDictionary(id => id, _ => NoDownstreamWork with {
                    NeedsProbe = NeedsProbe,
                    NeedsPreview = NeedsPreview
                }));
        }

        public Task InvalidateSubtitleStateAsync(
            IReadOnlyCollection<VideoSubtitleSidecarState> states,
            CancellationToken cancellationToken) {
            InvalidatedSubtitleStates.AddRange(states);
            return Task.CompletedTask;
        }

        public Task ClearGeneratedPreviewAssetsAsync(
            EntityKind kind,
            Guid entityId,
            CancellationToken cancellationToken) {
            ClearGeneratedAssetsCalls++;
            return Task.CompletedTask;
        }

        public Task<LibraryRootData?> GetLibraryRootAsync(Guid rootId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LibraryRootData>> GetEnabledRootsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateRootLastScannedAsync(Guid rootId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<string>> GetExcludedPathsForRootAsync(Guid rootId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> RemoveEntitiesInExcludedPathsAsync(Guid rootId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> RemoveEntitiesOutsideLibraryRootsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> RemoveOrphanTagsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> HasEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> HasEntityFingerprintAsync(
            Guid entityId,
            FingerprintAlgorithm algorithm,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HasEntityFileAsync(
            Guid entityId,
            EntityFileRole role,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HasSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Guid> UpsertVideoAsync(
            string filePath,
            string title,
            Guid libraryRootId,
            bool isNsfw,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> RemoveStaleVideosByRootAsync(
            Guid rootId,
            IReadOnlySet<string> validPaths,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> RemoveStaleMoviesByRootAsync(
            Guid rootId,
            IReadOnlySet<string> validFolderPaths,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> RemoveOrphanSeriesAndSeasonsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> UpsertVideosBatchAsync(
            IReadOnlyList<VideoUpsertItem> items,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DiscardPendingScanChangesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<VideoRecoveryTarget>> GetVideoRecoveryTargetsInRootAsync(
            Guid rootId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> GetActiveEntityIdsByKindAsync(
            EntityKind kind,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public string GetCacheBasePath() => throw new NotSupportedException();
    }

    private sealed class RecordingJobQueue : IJobQueueService {
        public List<EnqueueJobRequest> Enqueued { get; } = [];
        public List<GraphJobNodeRequest> Nodes { get; } = [];
        public Dictionary<string, Guid> RunIds { get; } = [];

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobRunSnapshot>>([]);

        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            Enqueued.Add(request);
            var now = DateTimeOffset.UtcNow;
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
                now,
                null,
                null));
        }

        public Task<JobRunSnapshot> AppendChildGraphNodeAsync(
            JobRunSnapshot parent,
            GraphJobNodeRequest request,
            CancellationToken cancellationToken) {
            Nodes.Add(request);
            Enqueued.Add(request.Job);
            var id = Guid.NewGuid();
            RunIds[request.NodeKey] = id;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new JobRunSnapshot(
                id,
                request.Job.Type,
                JobRunStatus.Queued,
                0,
                null,
                request.Job.PayloadJson ?? "{}",
                request.Job.TargetEntityKind,
                request.Job.TargetEntityId,
                request.Job.TargetLabel,
                now,
                null,
                null,
                GraphId: parent.GraphId,
                GraphOrigin: parent.GraphOrigin,
                NodeKey: request.NodeKey,
                ParentRunId: parent.Id,
                Importance: request.Importance,
                ResourceClass: request.ResourceClass,
                ResourceKey: request.ResourceKey));
        }

        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<int> EnqueueBatchAsync(
            IReadOnlyList<EnqueueJobRequest> requests,
            CancellationToken cancellationToken) {
            Enqueued.AddRange(requests);
            return Task.FromResult(requests.Count);
        }

        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<JobRunSnapshot?> ClaimNextAsync(
            string workerId,
            CancellationToken cancellationToken) => Task.FromResult<JobRunSnapshot?>(null);
        public Task<int> RecoverStaleRunningAsync(
            string currentWorkerId,
            TimeSpan staleAfter,
            CancellationToken cancellationToken) => Task.FromResult(0);
        public Task UpdateProgressAsync(
            Guid id,
            int progress,
            string? message,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task FailAsync(
            Guid id,
            string message,
            TimeSpan retryDelay,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(
            bool hideNsfw,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobQueueCount>>([]);
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
