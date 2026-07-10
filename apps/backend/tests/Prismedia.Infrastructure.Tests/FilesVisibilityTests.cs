using Prismedia.Application.Files;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Files;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class FilesVisibilityTests {
    private static readonly Guid SafeRootId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NsfwRootId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task ListRootsAsyncOmitsNsfwRootsWhenHidden() {
        var service = CreateService();

        var response = await service.ListRootsAsync(hideNsfw: true, CancellationToken.None);

        Assert.Equal(SafeRootId, Assert.Single(response.Roots).Id);
    }

    [Fact]
    public async Task ListChildrenAsyncOmitsEntriesAssociatedWithNsfwEntitiesWhenHidden() {
        var service = CreateService(hiddenPaths: new HashSet<string>(["/media/safe/hidden"], StringComparer.OrdinalIgnoreCase));

        var response = await service.ListChildrenAsync(
            new FileChildrenRequest(SafeRootId, ""),
            hideNsfw: true,
            CancellationToken.None);

        Assert.Equal("visible", Assert.Single(response.Entries).Name);
    }

    [Fact]
    public async Task ListChildrenAsyncRejectsHiddenNsfwAssociatedFolders() {
        var service = CreateService(hiddenPaths: new HashSet<string>(["/media/safe/hidden"], StringComparer.OrdinalIgnoreCase));

        var error = await Assert.ThrowsAsync<FileOperationException>(() =>
            service.ListChildrenAsync(
                new FileChildrenRequest(SafeRootId, "hidden"),
                hideNsfw: true,
                CancellationToken.None));

        Assert.Equal("not_found", error.Code);
    }

    [Fact]
    public async Task GetDetailAsyncRejectsHiddenNsfwAssociatedPaths() {
        var service = CreateService(hiddenPaths: new HashSet<string>(["/media/safe/hidden.mkv"], StringComparer.OrdinalIgnoreCase));

        var error = await Assert.ThrowsAsync<FileOperationException>(() =>
            service.GetDetailAsync(
                new FileDetailRequest(SafeRootId, "hidden.mkv"),
                hideNsfw: true,
                CancellationToken.None));

        Assert.Equal("not_found", error.Code);
    }

    private static FilesService CreateService(IReadOnlySet<string>? hiddenPaths = null) =>
        new(
            new FakeFilesPersistence(hiddenPaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)),
            new FakeStorage(),
            new FakeJobQueue(),
            new EntitySourcePathMutationCoordinator(new NoSourceOwners(), new UnreachableLifecycleLease()));

    private sealed class FakeFilesPersistence(IReadOnlySet<string> hiddenPaths) : IFilesPersistence {
        public Task<IReadOnlyList<FileLibraryRoot>> ListRootsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileLibraryRoot>>([
                new(SafeRootId, "/media/safe", "Safe", true, true, false, false, false, false),
                new(NsfwRootId, "/media/nsfw", "Hidden", true, true, false, false, false, true),
            ]);

        public Task<FileLibraryRoot?> GetRootAsync(Guid rootId, CancellationToken cancellationToken) =>
            Task.FromResult<FileLibraryRoot?>(rootId switch {
                var id when id == SafeRootId => new(SafeRootId, "/media/safe", "Safe", true, true, false, false, false, false),
                var id when id == NsfwRootId => new(NsfwRootId, "/media/nsfw", "Hidden", true, true, false, false, false, true),
                _ => null,
            });

        public Task<IReadOnlyList<FileLinkedEntity>> ListLinkedEntitiesAsync(
            string absolutePath,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileLinkedEntity>>([]);

        public Task<IReadOnlySet<string>> ListHiddenPathsAsync(
            string scopeDirectory,
            IReadOnlyList<string> absolutePaths,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(
                absolutePaths
                    .Where(path => hiddenPaths.Contains(path))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlySet<string>> ListExcludedRelativePathsAsync(
            Guid rootId,
            IReadOnlyList<string> relativePaths,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        public Task UpsertExclusionAsync(
            Guid rootId,
            string relativePath,
            FileEntryKind kind,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RemoveExclusionAsync(
            Guid rootId,
            string relativePath,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ApplyPathPrefixRewriteAsync(
            string sourcePath,
            string targetPath,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeStorage : IManagedFileStorage {
        public Task<IReadOnlyList<FileEntry>> ListChildrenAsync(
            ResolvedFilePath directory,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileEntry>>([
                new(directory.Root.Id, "visible", "visible", FileEntryKind.Directory, null, null, null),
                new(directory.Root.Id, "hidden", "hidden", FileEntryKind.Directory, null, null, null),
            ]);

        public Task<FileDetail> GetDetailAsync(
            ResolvedFilePath path,
            IReadOnlyList<FileLinkedEntity> linkedEntities,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FileDetail(
                new FileEntry(path.Root.Id, path.RelativePath, Path.GetFileName(path.RelativePath), FileEntryKind.File, null, "video/mp4", null),
                path.AbsolutePath,
                null,
                linkedEntities,
                true));

        public Task<FileContentInfo> GetContentInfoAsync(
            ResolvedFilePath path,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FileContentInfo(path.AbsolutePath, "video/mp4", null, 0));

        public Task CreateDirectoryAsync(ResolvedFilePath path, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteFileAsync(ResolvedFilePath path, Stream content, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MoveAsync(ResolvedFilePath source, ResolvedFilePath target, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(ResolvedFilePath path, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoSourceOwners : IEntitySourcePathOwnerReader {
        public Task<IReadOnlySet<Guid>> ListDirectOwnerIdsAsync(
            string physicalPath,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
    }

    private sealed class UnreachableLifecycleLease : IEntityLifecycleMutationLease {
        public Task<bool> ExecuteAsync(
            Guid entityId,
            Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unlinked visibility tests must not acquire an Entity lifecycle lease.");
    }

    private sealed class FakeJobQueue : IJobQueueService {
        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobRunSnapshot>>([]);

        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

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
            Task.FromResult<IReadOnlyList<JobQueueCount>>([]);

        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
