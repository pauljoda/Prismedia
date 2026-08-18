using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Ports;
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
            var payload = AssertPayload(request);
            Assert.Equal(rootId, payload.RootId);
            Assert.False(payload.ChangesOnly);
        });
    }

    [Fact]
    public async Task ChangedPathQueueingPersistsEachKindBeforeQueuingSurgicalJobs() {
        var rootId = Guid.NewGuid();
        var queue = new RecordingJobQueue();
        var intake = new RecordingChangeIntake();
        var paths = new[] { "/media/tv/Series/Season 01/Episode.mkv" };

        var queued = await LibraryScanJobs.QueueChangedPathsForRootAsync(
            queue,
            intake,
            rootId,
            "TV",
            new LibraryScanSelection(true, false, true, false),
            paths,
            CancellationToken.None);

        // A touched video routes only to the scan kinds that can own it: the enabled audio
        // family never sees the path, so one file change queues one job instead of one per family.
        Assert.Equal(1, queued);
        Assert.Equal(
            [JobType.ScanLibrary.ToCode()],
            intake.Records.Select(record => record.ScanKind));
        Assert.All(intake.Records, record => Assert.Equal(paths, record.Paths));
        Assert.All(queue.Enqueued, request => Assert.True(AssertPayload(request).ChangesOnly));
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

    private sealed class RecordingChangeIntake : ILibraryFileChangeIntake {
        public List<(Guid RootId, string ScanKind, IReadOnlyCollection<string> Paths)> Records { get; } = [];

        public Task RecordAsync(
            Guid rootId,
            string scanKind,
            IReadOnlyCollection<string> absolutePaths,
            CancellationToken cancellationToken) {
            Records.Add((rootId, scanKind, absolutePaths));
            return Task.CompletedTask;
        }

        public Task<LibraryFileChangeBatch> LoadAsync(
            Guid rootId,
            string scanKind,
            int limit,
            CancellationToken cancellationToken) => Task.FromResult(LibraryFileChangeBatch.Empty);

        public Task<bool> HasPendingAsync(Guid rootId, string scanKind, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task CompleteAsync(
            Guid rootId,
            string scanKind,
            IReadOnlyCollection<string> absolutePaths,
            DateTimeOffset observedThrough,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
