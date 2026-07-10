using Prismedia.Application.Files;
using Prismedia.Application.Entities;
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
                Assert.Equal(FileEntryKind.Directory, folder.Kind);
                Assert.Equal("Movies", folder.Path);
            },
            file => {
                Assert.Equal("loose.mp4", file.Name);
                Assert.Equal(FileEntryKind.File, file.Kind);
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
    public async Task DeleteRejectsCaseVariantSiblingEscapeOnUnix() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        var rootName = Path.GetFileName(_tempRoot.FullName);
        var caseVariantRootName = rootName.ToUpperInvariant();
        Assert.NotEqual(rootName, caseVariantRootName);
        var service = CreateService();

        var error = await Assert.ThrowsAsync<FileOperationException>(() =>
            service.DeleteAsync(
                new FileDeleteRequest(_rootId, $"../{caseVariantRootName}/escape.txt"),
                hideNsfw: false,
                CancellationToken.None));

        Assert.Equal(Prismedia.Contracts.System.ApiProblemCodes.InvalidPath, error.Code);
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
    public async Task LinkedMoveExecutesPhysicalMoveAndPathRewriteInsideOneEntityLease() {
        var ownerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var source = Path.Combine(_tempRoot.FullName, "linked-old.mp4");
        var target = Path.Combine(_tempRoot.FullName, "Organized", "linked-new.mp4");
        await File.WriteAllTextAsync(source, "video");
        SourceOwners.OwnerIds.Add(ownerId);
        Lifecycle.BeforeMutation = () => {
            Assert.True(File.Exists(source));
            Assert.False(File.Exists(target));
        };
        Lifecycle.AfterMutation = () => {
            Assert.False(File.Exists(source));
            Assert.True(File.Exists(target));
            Assert.True(Persistence.RewriteObservedInsideLifecycle);
        };
        Persistence.IsInsideLifecycle = () => Lifecycle.InsideMutation;
        var service = CreateService();

        await service.MoveAsync(
            new FileMoveRequest(_rootId, "linked-old.mp4", _rootId, "Organized/linked-new.mp4"),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Equal([ownerId], Assert.Single(Lifecycle.EntityIdBatches));
    }

    [Fact]
    public async Task LinkedDeleteExecutesPhysicalDeleteInsideEntityLease() {
        var ownerId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var source = Path.Combine(_tempRoot.FullName, "linked-delete.mp4");
        await File.WriteAllTextAsync(source, "video");
        SourceOwners.OwnerIds.Add(ownerId);
        Lifecycle.BeforeMutation = () => Assert.True(File.Exists(source));
        Lifecycle.AfterMutation = () => Assert.False(File.Exists(source));
        var service = CreateService();

        await service.DeleteAsync(
            new FileDeleteRequest(_rootId, "linked-delete.mp4"),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Equal([ownerId], Assert.Single(Lifecycle.EntityIdBatches));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkedMutationConflictLeavesPhysicalSourceUntouched(bool delete) {
        var ownerId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var source = Path.Combine(_tempRoot.FullName, "claimed.mp4");
        await File.WriteAllTextAsync(source, "video");
        SourceOwners.OwnerIds.Add(ownerId);
        Lifecycle.AllowExecution = false;
        var service = CreateService();

        await Assert.ThrowsAsync<FileConflictException>(() => delete
            ? service.DeleteAsync(
                new FileDeleteRequest(_rootId, "claimed.mp4"),
                hideNsfw: false,
                CancellationToken.None)
            : service.MoveAsync(
                new FileMoveRequest(_rootId, "claimed.mp4", _rootId, "claimed-moved.mp4"),
                hideNsfw: false,
                CancellationToken.None));

        Assert.True(File.Exists(source));
        Assert.False(File.Exists(Path.Combine(_tempRoot.FullName, "claimed-moved.mp4")));
        Assert.Empty(Persistence.Rewrites);
    }

    [Fact]
    public async Task ExcludePersistsRelativePathAndQueuesScans() {
        await File.WriteAllTextAsync(Path.Combine(_tempRoot.FullName, "skip.mkv"), "video");
        var service = CreateService();

        var response = await service.ExcludeAsync(
            new FileExclusionRequest(_rootId, "skip.mkv"),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Equal(2, response.ScansQueued);
        Assert.Equal(("skip.mkv", FileEntryKind.File), Persistence.Exclusions.Single());
        Assert.Contains(JobType.ScanLibrary, Queue.Enqueued.Select(job => job.Type));
        Assert.Contains(JobType.ScanGallery, Queue.Enqueued.Select(job => job.Type));
    }

    [Fact]
    public async Task RemoveExclusionClearsRelativePathAndQueuesScans() {
        Persistence.ExcludedPaths.Add("skip.mkv");
        var service = CreateService();

        var response = await service.RemoveExclusionAsync(
            new FileExclusionRequest(_rootId, "skip.mkv"),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Equal(2, response.ScansQueued);
        Assert.Empty(Persistence.ExcludedPaths);
        Assert.Contains(JobType.ScanLibrary, Queue.Enqueued.Select(job => job.Type));
        Assert.Contains(JobType.ScanGallery, Queue.Enqueued.Select(job => job.Type));
    }

    [Fact]
    public async Task ListChildrenMarksExcludedEntriesWithoutHidingThem() {
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Skip"));
        await File.WriteAllTextAsync(Path.Combine(_tempRoot.FullName, "Keep.mkv"), "keep");
        Persistence.ExcludedPaths.Add("Skip");
        var service = CreateService();

        var response = await service.ListChildrenAsync(
            new FileChildrenRequest(_rootId, string.Empty),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Contains(response.Entries, entry => entry.Name == "Skip" && entry.Excluded);
        Assert.Contains(response.Entries, entry => entry.Name == "Keep.mkv" && !entry.Excluded);
    }

    [Fact]
    public async Task GetDetailSuppressesLinkedEntitiesAndPreviewForExcludedPaths() {
        await File.WriteAllTextAsync(Path.Combine(_tempRoot.FullName, "skip.mkv"), "video");
        Persistence.ExcludedPaths.Add("skip.mkv");
        Persistence.LinkedEntities.Add(new FileLinkedEntity(Guid.NewGuid(), EntityKind.Video, "Should not render"));
        var service = CreateService();

        var detail = await service.GetDetailAsync(
            new FileDetailRequest(_rootId, "skip.mkv"),
            hideNsfw: false,
            CancellationToken.None);

        Assert.True(detail.Entry.Excluded);
        Assert.False(detail.CanPreview);
        Assert.Empty(detail.LinkedEntities);
        Assert.Equal(0, Persistence.LinkedEntityCalls);
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
    private RecordingSourcePathOwnerReader SourceOwners { get; } = new();
    private RecordingLifecycleMutationLease Lifecycle { get; } = new();

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

        return new FilesService(
            Persistence,
            new LocalManagedFileStorage(),
            Queue,
            new EntitySourcePathMutationCoordinator(SourceOwners, Lifecycle));
    }

    public void Dispose() {
        if (_tempRoot.Exists) {
            _tempRoot.Delete(recursive: true);
        }
    }

    private sealed class RecordingFilesPersistence : IFilesPersistence {
        public Dictionary<Guid, FileLibraryRoot> Roots { get; } = new();
        public List<(string SourcePath, string TargetPath)> Rewrites { get; } = [];
        public HashSet<string> ExcludedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Path, FileEntryKind Kind)> Exclusions { get; } = [];
        public List<FileLinkedEntity> LinkedEntities { get; } = [];
        public int LinkedEntityCalls { get; private set; }
        public Func<bool>? IsInsideLifecycle { get; set; }
        public bool RewriteObservedInsideLifecycle { get; private set; }

        public Task<IReadOnlyList<FileLibraryRoot>> ListRootsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileLibraryRoot>>(Roots.Values.ToArray());

        public Task<FileLibraryRoot?> GetRootAsync(Guid rootId, CancellationToken cancellationToken) =>
            Task.FromResult<FileLibraryRoot?>(Roots.GetValueOrDefault(rootId));

        public Task<IReadOnlyList<FileLinkedEntity>> ListLinkedEntitiesAsync(
            string absolutePath,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            LinkedEntityCalls++;
            return Task.FromResult<IReadOnlyList<FileLinkedEntity>>(LinkedEntities);
        }

        public Task<IReadOnlySet<string>> ListHiddenPathsAsync(
            string scopeDirectory,
            IReadOnlyList<string> absolutePaths,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        public Task ApplyPathPrefixRewriteAsync(
            string sourcePath,
            string targetPath,
            CancellationToken cancellationToken) {
            RewriteObservedInsideLifecycle = IsInsideLifecycle?.Invoke() == true;
            Rewrites.Add((sourcePath, targetPath));
            return Task.CompletedTask;
        }

        public Task<IReadOnlySet<string>> ListExcludedRelativePathsAsync(
            Guid rootId,
            IReadOnlyList<string> relativePaths,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(
                relativePaths
                    .Where(IsExcluded)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase));

        public Task UpsertExclusionAsync(
            Guid rootId,
            string relativePath,
            FileEntryKind kind,
            CancellationToken cancellationToken) {
            ExcludedPaths.Add(relativePath);
            Exclusions.Add((relativePath, kind));
            return Task.CompletedTask;
        }

        public Task RemoveExclusionAsync(
            Guid rootId,
            string relativePath,
            CancellationToken cancellationToken) {
            ExcludedPaths.Remove(relativePath);
            return Task.CompletedTask;
        }

        private bool IsExcluded(string relativePath) =>
            ExcludedPaths.Any(excluded =>
                string.Equals(relativePath, excluded, StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith($"{excluded}/", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class RecordingSourcePathOwnerReader : IEntitySourcePathOwnerReader {
        public HashSet<Guid> OwnerIds { get; } = [];

        public Task<IReadOnlySet<Guid>> ListDirectOwnerIdsAsync(
            string physicalPath,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(OwnerIds.ToHashSet());
    }

    private sealed class RecordingLifecycleMutationLease : IEntityLifecycleMutationLease {
        public bool AllowExecution { get; set; } = true;
        public bool InsideMutation { get; private set; }
        public Action? BeforeMutation { get; set; }
        public Action? AfterMutation { get; set; }
        public List<Guid[]> EntityIdBatches { get; } = [];

        public Task<bool> ExecuteAsync(
            Guid entityId,
            Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken) =>
            ExecuteManyAsync([entityId], mutation, cancellationToken);

        public async Task<bool> ExecuteManyAsync(
            IReadOnlyCollection<Guid> entityIds,
            Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken) {
            EntityIdBatches.Add(entityIds.Order().ToArray());
            if (!AllowExecution) {
                return false;
            }

            BeforeMutation?.Invoke();
            InsideMutation = true;
            try {
                await mutation(cancellationToken);
            } finally {
                InsideMutation = false;
            }
            AfterMutation?.Invoke();
            return true;
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
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken, JobRunLane? lane = null) =>
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
