using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Subtitles;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Api.Tests;

public sealed class SubtitleAcquisitionEndpointTests {
    private static readonly Guid VideoId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly JsonSerializerOptions CodecJson =
        new(JsonSerializerDefaults.Web) { Converters = { new CodecJsonConverterFactory() } };

    [Fact]
    public async Task SearchReturnsProviderOwnedRankingEvidence() {
        var service = new FakeSubtitleAcquisitionService {
            SearchResults = [new SubtitleSearchResult(
                SubtitleProviderCodes.OpenSubtitles,
                "42:84",
                "en",
                "Example.Release.1080p",
                SubtitleFormats.Srt,
                HearingImpaired: false,
                Forced: false,
                AiTranslated: false,
                MachineTranslated: false,
                HashMatched: true,
                DownloadCount: 123,
                Rating: 9.2m,
                MatchConfidence: 100,
                QualityScore: 87,
                AutomaticEligible: true,
                MatchReasons: ["Exact file hash"],
                PageUrl: "https://www.opensubtitles.com/example")]
        };
        using var factory = CreateFactory(service);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/videos/{VideoId}/subtitles/search",
            new SearchVideoSubtitlesRequest(["en"]));
        var body = await response.Content.ReadFromJsonAsync<SearchVideoSubtitlesResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = Assert.Single(Assert.IsType<SearchVideoSubtitlesResponse>(body).Candidates);
        Assert.Equal("42:84", result.CandidateId);
        Assert.True(result.HashMatched);
        Assert.True(result.AutomaticEligible);
        Assert.Equal(100, result.MatchConfidence);
        Assert.Equal(["en"], service.SearchRequest?.Languages);
    }

    [Fact]
    public async Task DownloadQueuesAnInteractiveEntityGraphWithoutCallingTheProviderInTheRequest() {
        var service = new FakeSubtitleAcquisitionService();
        var jobs = new RecordingJobQueue();
        using var factory = CreateFactory(service, jobs);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/videos/{VideoId}/subtitles/download",
            new AcquireVideoSubtitleRequest(SubtitleProviderCodes.OpenSubtitles, "42:84"));
        var body = await response.Content.ReadFromJsonAsync<AcquireVideoSubtitleResponse>(CodecJson);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var graph = Assert.IsType<AcquireVideoSubtitleResponse>(body).Graph;
        Assert.Equal(JobGraphOrigin.Interactive, graph.Origin);
        Assert.Equal(EntityKind.Video.ToCode(), graph.RootEntityKind);
        Assert.Equal(VideoId.ToString(), graph.RootEntityId);
        Assert.Equal(JobType.AcquireSubtitle, graph.InitialNode.Type);
        Assert.Equal(JobResourceKeys.Entity(VideoId.ToString()), jobs.LastRequest?.ResourceKey);
        Assert.Equal(0, service.AcquireCalls);
    }

    [Fact]
    public async Task ConfigurationResponseNeverReturnsCredentialValues() {
        var service = new FakeSubtitleAcquisitionService {
            Configuration = new OpenSubtitlesConfiguration(
                Enabled: true,
                ApiKeyConfigured: true,
                UsernameConfigured: true,
                PasswordConfigured: true,
                IncludeAiTranslated: false,
                IncludeMachineTranslated: false)
        };
        using var factory = CreateFactory(service);
        using var client = factory.CreateAuthenticatedClient();

        var body = await client.GetStringAsync("/api/subtitle-providers/opensubtitles");

        Assert.Contains("\"apiKeyConfigured\":true", body, StringComparison.Ordinal);
        Assert.DoesNotContain("api-key-secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("password-secret", body, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeSubtitleAcquisitionService service,
        IJobQueueService? jobs = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services => {
                services.RemoveAll<ISubtitleAcquisitionService>();
                services.AddSingleton<ISubtitleAcquisitionService>(service);
                if (jobs is not null) {
                    services.RemoveAll<IJobQueueService>();
                    services.AddSingleton<IJobQueueService>(jobs);
                }
            }))
            .WithTestAuth();

    private sealed class RecordingJobQueue : IJobQueueService {
        public EnqueueJobRequest? LastRequest { get; private set; }

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            LastRequest = request;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new JobRunSnapshot(
                Guid.NewGuid(), request.Type, JobRunStatus.Queued, 0, null, request.PayloadJson ?? "{}",
                request.TargetEntityKind, request.TargetEntityId, request.TargetLabel, now, null, null,
                GraphId: Guid.NewGuid(), GraphOrigin: request.Origin, ResourceKey: request.ResourceKey));
        }

        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            EnqueueAsync(new EnqueueJobRequest(type), cancellationToken);
        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeSubtitleAcquisitionService : ISubtitleAcquisitionService {
        public OpenSubtitlesConfiguration Configuration { get; init; } =
            new(false, false, false, false, false, false);
        public IReadOnlyList<SubtitleSearchResult> SearchResults { get; init; } = [];
        public SubtitleSearchRequest? SearchRequest { get; private set; }
        public int AcquireCalls { get; private set; }

        public Task<OpenSubtitlesConfiguration> GetOpenSubtitlesConfigurationAsync(
            CancellationToken cancellationToken) => Task.FromResult(Configuration);

        public Task<OpenSubtitlesConfiguration> SaveOpenSubtitlesConfigurationAsync(
            SaveOpenSubtitlesConfiguration configuration,
            CancellationToken cancellationToken) => Task.FromResult(Configuration);

        public Task<SubtitleProviderTestResult> TestOpenSubtitlesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SubtitleProviderTestResult(true, "Connected."));

        public Task<IReadOnlyList<SubtitleSearchResult>> SearchAsync(
            Guid videoId,
            SubtitleSearchRequest request,
            CancellationToken cancellationToken) {
            SearchRequest = request;
            return Task.FromResult(SearchResults);
        }

        public Task<SubtitleAcquisitionResult> AcquireAsync(
            Guid videoId,
            string provider,
            string candidateId,
            CancellationToken cancellationToken) {
            AcquireCalls++;
            return Task.FromResult(new SubtitleAcquisitionResult(Guid.NewGuid(), AlreadyPresent: false));
        }

        public Task<AutomaticSubtitleAcquisitionResult> AcquireMissingPreferredAsync(
            Guid videoId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AutomaticSubtitleAcquisitionResult(0, []));
    }
}
