using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Infrastructure.Serialization;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Jobs;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class JobEndpointServiceTests {
    // The codec enums serialize as their string code; deserializing the typed DTO client-side
    // needs the same converter, so this also asserts the wire format round-trips unchanged.
    private static readonly JsonSerializerOptions CodecJson =
        new(JsonSerializerDefaults.Web) { Converters = { new CodecJsonConverterFactory() } };
    [Fact]
    public async Task JobsEndpointListsJobsFromQueueService() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.GetFromJsonAsync<JobListResponse>("/api/jobs", CodecJson);

        Assert.NotNull(response);
        var job = Assert.Single(response.Items);
        Assert.Equal(JobType.ScanLibrary, job.Type);
        Assert.Equal(JobRunStatus.Queued, job.Status);
    }

    [Fact]
    public async Task CreateJobEndpointQueuesThroughQueueService() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync("/api/jobs/probe-video", null);
        var payload = await response.Content.ReadFromJsonAsync<JobCreateResponse>(CodecJson);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("/api/jobs/22222222-2222-2222-2222-222222222222", response.Headers.Location?.OriginalString);
        Assert.NotNull(payload);
        Assert.Equal(JobType.ProbeVideo, payload.Job.Type);
        Assert.Equal(JobRunStatus.Queued, payload.Job.Status);
    }

    [Fact]
    public async Task CreateJobEndpointRejectsUnknownJobType() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync("/api/jobs/not-real", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CancelJobsEndpointCancelsByOptionalType() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.DeleteFromJsonAsync<JobCancelResponse>("/api/jobs?type=scan-library");

        Assert.NotNull(response);
        Assert.Equal(1, response.Cancelled);
    }

    [Fact]
    public async Task CancelJobsEndpointCancelsAllWhenTypeIsOmitted() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.DeleteFromJsonAsync<JobCancelResponse>("/api/jobs");

        Assert.NotNull(response);
        Assert.Equal(3, response.Cancelled);
    }

    [Fact]
    public async Task ClearFailuresEndpointClearsByOptionalType() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync("/api/jobs/failures/clear?type=import-metadata", null);
        var payload = await response.Content.ReadFromJsonAsync<JobFailureClearResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(2, payload.Cleared);
    }

    private static WebApplicationFactory<Program> CreateFactory() {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddScoped<IJobQueueService, FakeJobQueueService>();
                });
            })
            .WithTestAuth();
    }

    private sealed class FakeJobQueueService : IJobQueueService {
        private static readonly Guid ExistingJobId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid CreatedJobId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        private static JobRunSnapshot Snap(Guid id, JobType type, JobRunStatus status) =>
            new(id, type, status, 0, null, "{}", null, null, null, DateTimeOffset.UnixEpoch, null, null);

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) {
            IReadOnlyList<JobRunSnapshot> jobs = [Snap(ExistingJobId, JobType.ScanLibrary, JobRunStatus.Queued)];
            return Task.FromResult(jobs);
        }

        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            Task.FromResult(Snap(CreatedJobId, type, JobRunStatus.Queued));

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Snap(CreatedJobId, request.Type, JobRunStatus.Queued));

        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) =>
            Task.FromResult(requests.Count);

        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) =>
            Task.FromResult(type is null ? 3 : type == JobType.ScanLibrary ? 1 : 0);

        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == ExistingJobId);

        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) =>
            Task.FromResult(type == JobType.ImportMetadata ? 2 : 0);

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
            Task.FromResult<IReadOnlyList<JobQueueCount>>([
                new("scan-library", "queued", 1),
            ]);

        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
