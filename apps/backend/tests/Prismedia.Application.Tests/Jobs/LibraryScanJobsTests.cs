using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Jobs;

public sealed class LibraryScanJobsTests {
    [Fact]
    public void SelectionUnionCombinesFamiliesAndIdentifiesEmptySelection() {
        var media = new LibraryScanSelection(Videos: true, Images: false, Audio: false, Books: false);
        var booksAndAudio = new LibraryScanSelection(Videos: false, Images: false, Audio: true, Books: true);

        var selection = media.Union(booksAndAudio);

        Assert.True(LibraryScanSelection.None.IsEmpty);
        Assert.False(selection.IsEmpty);
        Assert.Equal(
            new LibraryScanSelection(Videos: true, Images: false, Audio: true, Books: true),
            selection);
    }

    [Fact]
    public void ScanJobTypesForMapsEachSelectedFamilyInCanonicalOrder() {
        var selection = new LibraryScanSelection(Videos: true, Images: true, Audio: true, Books: true);

        var types = LibraryScanJobs.ScanJobTypesFor(selection);

        Assert.Equal(
            [JobType.ScanLibrary, JobType.ScanGallery, JobType.ScanAudio, JobType.ScanBook],
            types);
    }

    [Fact]
    public async Task RootScopedQueueingTargetsOnlyThatRootAndSerializesAllScanKinds() {
        var rootId = Guid.NewGuid();
        var queue = new RecordingJobQueue();
        var selection = new LibraryScanSelection(
            Videos: true,
            Images: false,
            Audio: true,
            Books: false);

        var queued = await LibraryScanJobs.QueueScansForRootAsync(
            queue,
            rootId,
            "Large library",
            selection,
            CancellationToken.None);

        Assert.Equal(2, queued);
        Assert.Equal([JobType.ScanLibrary, JobType.ScanAudio], queue.Enqueued.Select(request => request.Type));
        Assert.All(queue.Enqueued, request => {
            Assert.Equal(LibraryScanJobs.TargetKind, request.TargetEntityKind);
            Assert.Equal(rootId.ToString(), request.TargetEntityId);
            Assert.Equal(JobResourceKeys.LibraryScan, request.ResourceKey);
            Assert.Equal(rootId, AssertPayload(request).RootId);
        });
    }

    private static ScanRootPayload AssertPayload(EnqueueJobRequest request) {
        Assert.True(ScanRootPayload.TryParse(request.PayloadJson, out var payload));
        return payload;
    }

    private sealed class RecordingJobQueue : IJobQueueService {
        public List<EnqueueJobRequest> Enqueued { get; } = [];

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            Enqueued.Add(request);
            return Task.FromResult(new JobRunSnapshot(
                Guid.NewGuid(), request.Type, JobRunStatus.Queued, 0, null,
                request.PayloadJson ?? "{}", request.TargetEntityKind, request.TargetEntityId,
                request.TargetLabel, DateTimeOffset.UtcNow, null, null));
        }

        public Task<bool> HasPendingAsync(
            JobType type,
            string? targetEntityId,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
