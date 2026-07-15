using System.Net;
using System.Net.Http.Json;
using System.IO.Compression;
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
    private static readonly Guid NsfwRootId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private readonly DirectoryInfo _tempRoot = Directory.CreateTempSubdirectory("prismedia-files-api-");

    [Fact]
    public async Task RootsEndpointHonorsExplicitNsfwHidingForTokenClients() {
        using var factory = CreateFactory(allowNsfw: true);
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.GetFromJsonAsync<FileRootsResponse>("/api/files/roots?hideNsfw=true");

        var root = Assert.Single(Assert.IsType<FileRootsResponse>(response).Roots);
        Assert.Equal(RootId, root.Id);
    }

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
    public async Task DownloadEndpointStreamsFilesAsAttachments() {
        await File.WriteAllTextAsync(Path.Combine(_tempRoot.FullName, "notes.txt"), "download me");
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            $"/api/files/download?rootId={RootId}&path=notes.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("notes.txt", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("download me", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FolderArchivePreparationReportsProgressAndDownloadsNestedZip() {
        var folder = Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Season 1"));
        Directory.CreateDirectory(Path.Combine(folder.FullName, "Extras"));
        await File.WriteAllTextAsync(Path.Combine(folder.FullName, "episode 1.txt"), "episode");
        await File.WriteAllTextAsync(Path.Combine(folder.FullName, "Extras", "notes.txt"), "notes");
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var startResponse = await client.PostAsJsonAsync(
            "/api/files/archives",
            new FileArchiveRequest(RootId, "Season 1"));
        var preparation = Assert.IsType<FileArchivePreparation>(
            await startResponse.Content.ReadFromJsonAsync<FileArchivePreparation>());

        Assert.Equal(HttpStatusCode.Accepted, startResponse.StatusCode);
        Assert.Equal("Season 1.zip", preparation.FileName);
        Assert.Equal(2, preparation.TotalFiles);

        for (var attempt = 0; attempt < 100 && !preparation.Ready && preparation.Error is null; attempt++) {
            await Task.Delay(20);
            preparation = Assert.IsType<FileArchivePreparation>(
                await client.GetFromJsonAsync<FileArchivePreparation>($"/api/files/archives/{preparation.Id}"));
        }

        Assert.True(preparation.Ready, preparation.Error ?? "Archive did not become ready.");
        Assert.Equal(100, preparation.ProgressPercent);
        Assert.Equal(2, preparation.ProcessedFiles);

        using var archiveResponse = await client.GetAsync($"/api/files/archives/{preparation.Id}/content");
        await using var archiveStream = await archiveResponse.Content.ReadAsStreamAsync();
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

        Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);
        Assert.Equal("attachment", archiveResponse.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("Season 1.zip", archiveResponse.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal(
            ["episode 1.txt", "Extras/notes.txt"],
            archive.Entries.Where(entry => entry.Name.Length > 0).Select(entry => entry.FullName).Order().ToArray());
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

    private WebApplicationFactory<Program> CreateFactory(bool allowNsfw = false) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddScoped<IFilesPersistence>(_ => new FakeFilesPersistence(_tempRoot.FullName));
                    services.RemoveAll<IEntitySourcePathOwnerReader>();
                    services.AddSingleton<IEntitySourcePathOwnerReader, NoSourceOwners>();
                    services.AddScoped<IJobQueueService, FakeJobQueue>();
                });
            })
            .WithTestAuth(allowNsfw: allowNsfw);

    private sealed class FakeFilesPersistence(string rootPath) : IFilesPersistence {
        public Task<IReadOnlyList<FileLibraryRoot>> ListRootsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileLibraryRoot>>([Root(), Root(NsfwRootId, isNsfw: true)]);

        public Task<FileLibraryRoot?> GetRootAsync(Guid rootId, CancellationToken cancellationToken) =>
            Task.FromResult<FileLibraryRoot?>(
                rootId == RootId
                    ? Root()
                    : rootId == NsfwRootId
                        ? Root(NsfwRootId, isNsfw: true)
                        : null);

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

        private FileLibraryRoot Root(Guid? id = null, bool isNsfw = false) =>
            new(id ?? RootId, rootPath, isNsfw ? "NSFW API Root" : "API Root", true, true, false, false, false, isNsfw);
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
