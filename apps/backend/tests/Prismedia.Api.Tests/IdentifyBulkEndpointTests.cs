using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Jobs;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Api.Tests;

public sealed class IdentifyBulkEndpointTests {
    private static readonly JsonSerializerOptions CodecJson =
        new(JsonSerializerDefaults.Web) { Converters = { new CodecJsonConverterFactory() } };

    [Fact]
    public async Task StartBulkIdentifyEnqueuesInteractiveIdentifyPriority() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            "/api/identify/bulk",
            new IdentifyBulkStartRequest(
                "tmdb",
                [Guid.Parse("11111111-1111-1111-1111-111111111111")],
                null),
            CodecJson);
        var body = await response.Content.ReadFromJsonAsync<JobCreateResponse>(CodecJson);

        var queue = factory.Services.GetRequiredService<RecordingJobQueueService>();
        var request = Assert.Single(queue.Enqueued);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(JobType.BulkIdentify, body.Job.Type);
        Assert.Equal(JobType.BulkIdentify, request.Type);
        Assert.Equal(JobPriorities.InteractiveIdentify, request.Priority);
        Assert.True(request.Priority > JobPriorities.AutoIdentify);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton<RecordingJobQueueService>();
                    services.AddScoped<IJobQueueService>(provider =>
                        provider.GetRequiredService<RecordingJobQueueService>());
                });
            })
            .WithTestAuth();

    private sealed class RecordingJobQueueService : IJobQueueService {
        private static readonly Guid CreatedJobId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private readonly List<EnqueueJobRequest> _enqueued = [];

        public IReadOnlyList<EnqueueJobRequest> Enqueued => _enqueued;

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            _enqueued.Add(request);
            return Task.FromResult(new JobRunSnapshot(
                CreatedJobId,
                request.Type,
                JobRunStatus.Queued,
                0,
                null,
                request.PayloadJson ?? "{}",
                request.TargetEntityKind,
                request.TargetEntityId,
                request.TargetLabel,
                DateTimeOffset.UnixEpoch,
                null,
                null));
        }

        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            EnqueueAsync(new EnqueueJobRequest(type), cancellationToken);

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
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
