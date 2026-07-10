using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Files;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class FilesEndpointTests : IDisposable {
    private static readonly Guid RootId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private readonly DirectoryInfo _tempRoot = Directory.CreateTempSubdirectory("prismedia-files-api-");

    [Fact]
    public async Task ContentEndpointSupportsByteRangeRequests() {
        await File.WriteAllTextAsync(Path.Combine(_tempRoot.FullName, "clip.mp4"), "0123456789");
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/files/content?rootId={RootId}&path=clip.mp4");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(2, 5);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("bytes", response.Headers.AcceptRanges.Single());
        Assert.Equal("bytes 2-5/10", response.Content.Headers.ContentRange?.ToString());
        Assert.Equal("2345", body);
    }

    [Fact]
    public async Task ContentEndpointSupportsHeadProbes() {
        await File.WriteAllTextAsync(Path.Combine(_tempRoot.FullName, "clip.mp4"), "0123456789");
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Head,
            $"/api/files/content?rootId={RootId}&path=clip.mp4"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, response.Content.Headers.ContentLength);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("bytes", response.Headers.AcceptRanges.Single());
    }

    [Fact]
    public async Task ChildrenEndpointRejectsTraversalOutsideRoot() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync($"/api/files/detail?rootId={RootId}&path=../outside.txt");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Prismedia.Contracts.System.ApiProblem>();
        Assert.Equal("invalid_path", problem?.Code);
    }

    [Fact]
    public async Task ExclusionEndpointMarksExistingPathExcluded() {
        await File.WriteAllTextAsync(Path.Combine(_tempRoot.FullName, "skip.mp4"), "video");
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            "/api/files/exclusions",
            new FileExclusionRequest(RootId, "skip.mp4"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FileOperationResponse>();
        Assert.Equal(1, body?.ScansQueued);
    }

    [Fact]
    public async Task RemoveExclusionEndpointClearsPathExclusion() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.DeleteAsync(
            $"/api/files/exclusions?rootId={RootId}&path=skip.mp4");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FileOperationResponse>();
        Assert.Equal(1, body?.ScansQueued);
    }

    public void Dispose() {
        if (_tempRoot.Exists) {
            _tempRoot.Delete(recursive: true);
        }
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddScoped<IFilesPersistence>(_ => new FakeFilesPersistence(_tempRoot.FullName));
                    services.RemoveAll<IEntitySourcePathOwnerReader>();
                    services.AddSingleton<IEntitySourcePathOwnerReader, NoSourceOwners>();
                    services.AddScoped<IJobQueueService, FakeJobQueue>();
                });
            })
            .WithTestAuth();

    private sealed class FakeFilesPersistence(string rootPath) : IFilesPersistence {
        public Task<IReadOnlyList<FileLibraryRoot>> ListRootsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileLibraryRoot>>([Root()]);

        public Task<FileLibraryRoot?> GetRootAsync(Guid rootId, CancellationToken cancellationToken) =>
            Task.FromResult<FileLibraryRoot?>(rootId == RootId ? Root() : null);

        public Task<IReadOnlyList<FileLinkedEntity>> ListLinkedEntitiesAsync(
            string absolutePath,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileLinkedEntity>>([]);

        public Task<IReadOnlySet<string>> ListHiddenPathsAsync(
            string scopeDirectory,
            IReadOnlyList<string> absolutePaths,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

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

        private FileLibraryRoot Root() =>
            new(RootId, rootPath, "API Root", true, true, false, false, false, false);
    }

    private sealed class NoSourceOwners : IEntitySourcePathOwnerReader {
        public Task<IReadOnlySet<Guid>> ListDirectOwnerIdsAsync(
            string physicalPath,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
    }

    private sealed class FakeJobQueue : IJobQueueService {
        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new JobRunSnapshot(
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

        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            EnqueueAsync(new EnqueueJobRequest(type), cancellationToken);

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
