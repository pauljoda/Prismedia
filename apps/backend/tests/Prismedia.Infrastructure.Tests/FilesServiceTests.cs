using Prismedia.Application.Files;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Files;

namespace Prismedia.Infrastructure.Tests;

public sealed class FilesServiceTests : IDisposable {
    private readonly DirectoryInfo _tempRoot = Directory.CreateTempSubdirectory("prismedia-files-");
    private readonly Guid _rootId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task ListChildrenReturnsDirectVisibleFilesAndFolders() {
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Movies"));
        await File.WriteAllTextAsync(Path.Combine(_tempRoot.FullName, "Movies", "nested.mkv"), "nested");
        await File.WriteAllTextAsync(Path.Combine(_tempRoot.FullName, "loose.mp4"), "video");
        await File.WriteAllTextAsync(Path.Combine(_tempRoot.FullName, ".hidden"), "hidden");
        var service = CreateService();

        var response = await service.ListChildrenAsync(
            new FileChildrenRequest(_rootId, string.Empty),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Equal("", response.Path);
        Assert.Collection(
            response.Entries,
            folder => {
                Assert.Equal("Movies", folder.Name);
                Assert.Equal("directory", folder.Kind);
                Assert.Equal("Movies", folder.Path);
            },
            file => {
                Assert.Equal("loose.mp4", file.Name);
                Assert.Equal("file", file.Kind);
                Assert.Equal("loose.mp4", file.Path);
            });
    }

    [Fact]
    public async Task DetailRejectsTraversalOutsideLibraryRoot() {
        var service = CreateService();

        await Assert.ThrowsAsync<FileOperationException>(() =>
            service.GetDetailAsync(
                new FileDetailRequest(_rootId, "../outside.txt"),
                hideNsfw: false,
                CancellationToken.None));
    }

    [Fact]
    public async Task UploadPreservesNestedRelativePathsUnderTargetFolder() {
        var service = CreateService();
        await using var stream = new MemoryStream("cover"u8.ToArray());

        await service.UploadAsync(
            new FileUploadRequest(_rootId, "Incoming", [
                new FileUploadItem("Album/Cover.jpg", stream),
            ]),
            hideNsfw: false,
            CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_tempRoot.FullName, "Incoming", "Album", "Cover.jpg")));
        Assert.Contains(JobType.ScanLibrary, Queue.Enqueued.Select(job => job.Type));
    }

    [Fact]
    public async Task CreateFolderReturnsConflictWhenTargetExists() {
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Existing"));
        var service = CreateService();

        await Assert.ThrowsAsync<FileConflictException>(() =>
            service.CreateFolderAsync(
                new FileCreateFolderRequest(_rootId, string.Empty, "Existing"),
                hideNsfw: false,
                CancellationToken.None));
    }

    [Fact]
    public async Task DeletePermanentlyRemovesFilesInsideRoot() {
        var filePath = Path.Combine(_tempRoot.FullName, "delete-me.txt");
        await File.WriteAllTextAsync(filePath, "gone");
        var service = CreateService();

        await service.DeleteAsync(new FileDeleteRequest(_rootId, "delete-me.txt"), hideNsfw: false, CancellationToken.None);

        Assert.False(File.Exists(filePath));
        Assert.Contains(JobType.ScanLibrary, Queue.Enqueued.Select(job => job.Type));
    }

    [Fact]
    public async Task MoveRewritesCatalogPathsAndQueuesScans() {
        var source = Path.Combine(_tempRoot.FullName, "old.mp4");
        await File.WriteAllTextAsync(source, "video");
        var service = CreateService();

        await service.MoveAsync(
            new FileMoveRequest(_rootId, "old.mp4", _rootId, "Organized/new.mp4"),
            hideNsfw: false,
            CancellationToken.None);

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(_tempRoot.FullName, "Organized", "new.mp4")));
        Assert.Equal(
            Path.Combine(_tempRoot.FullName, "Organized", "new.mp4"),
            Persistence.Rewrites.Single().TargetPath);
        Assert.Contains(JobType.ScanLibrary, Queue.Enqueued.Select(job => job.Type));
    }

    [Fact]
    public async Task StorageBlocksWriteThroughSymlinkedFolders() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        var outside = Directory.CreateTempSubdirectory("prismedia-files-outside-");
        try {
            Directory.CreateSymbolicLink(Path.Combine(_tempRoot.FullName, "linked"), outside.FullName);
            var service = CreateService();
            await using var stream = new MemoryStream("bad"u8.ToArray());

            await Assert.ThrowsAsync<FileOperationException>(() =>
                service.UploadAsync(
                    new FileUploadRequest(_rootId, "linked", [
                        new FileUploadItem("escape.txt", stream),
                    ]),
                    hideNsfw: false,
                    CancellationToken.None));
        } finally {
            outside.Delete(recursive: true);
        }
    }

    private RecordingFilesPersistence Persistence { get; } = new();
    private RecordingJobQueue Queue { get; } = new();

    private FilesService CreateService() {
        Persistence.Roots[_rootId] = new FileLibraryRoot(
            _rootId,
            _tempRoot.FullName,
            "Test Root",
            true,
            true,
            true,
            false,
            false,
            false);

        return new FilesService(Persistence, new LocalManagedFileStorage(), Queue);
    }

    public void Dispose() {
        if (_tempRoot.Exists) {
            _tempRoot.Delete(recursive: true);
        }
    }

    private sealed class RecordingFilesPersistence : IFilesPersistence {
        public Dictionary<Guid, FileLibraryRoot> Roots { get; } = new();
        public List<(string SourcePath, string TargetPath)> Rewrites { get; } = [];

        public Task<IReadOnlyList<FileLibraryRoot>> ListRootsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileLibraryRoot>>(Roots.Values.ToArray());

        public Task<FileLibraryRoot?> GetRootAsync(Guid rootId, CancellationToken cancellationToken) =>
            Task.FromResult<FileLibraryRoot?>(Roots.GetValueOrDefault(rootId));

        public Task<IReadOnlyList<FileLinkedEntity>> ListLinkedEntitiesAsync(
            string absolutePath,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileLinkedEntity>>([]);

        public Task<IReadOnlySet<string>> ListHiddenPathsAsync(
            IReadOnlyList<string> absolutePaths,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        public Task ApplyPathPrefixRewriteAsync(
            string sourcePath,
            string targetPath,
            CancellationToken cancellationToken) {
            Rewrites.Add((sourcePath, targetPath));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingJobQueue : IJobQueueService {
        public List<EnqueueJobRequest> Enqueued { get; } = [];

        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            EnqueueAsync(new EnqueueJobRequest(type), cancellationToken);

        public Task<JobRunSnapshot> EnqueueAsync(
            EnqueueJobRequest request,
            CancellationToken cancellationToken) {
            Enqueued.Add(request);
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
                null));
        }

        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken) =>
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
