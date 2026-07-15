using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Subtitles;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Tests;

public sealed class SubtitleAcquisitionEndpointTests {
    private static readonly Guid VideoId = Guid.Parse("11111111-2222-3333-4444-555555555555");

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
    public async Task ExpiredCandidateReturnsStableGoneProblem() {
        var service = new FakeSubtitleAcquisitionService {
            AcquireException = new SubtitleCandidateUnavailableException("Candidate expired.")
        };
        using var factory = CreateFactory(service);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/videos/{VideoId}/subtitles/download",
            new AcquireVideoSubtitleRequest(SubtitleProviderCodes.OpenSubtitles, "42:84"));
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal(ApiProblemCodes.SubtitleCandidateUnavailable, problem?.Code);
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

    private static WebApplicationFactory<Program> CreateFactory(FakeSubtitleAcquisitionService service) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services => {
                services.RemoveAll<ISubtitleAcquisitionService>();
                services.AddSingleton<ISubtitleAcquisitionService>(service);
            }))
            .WithTestAuth();

    private sealed class FakeSubtitleAcquisitionService : ISubtitleAcquisitionService {
        public OpenSubtitlesConfiguration Configuration { get; init; } =
            new(false, false, false, false, false, false);
        public IReadOnlyList<SubtitleSearchResult> SearchResults { get; init; } = [];
        public SubtitleSearchRequest? SearchRequest { get; private set; }
        public Exception? AcquireException { get; init; }

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
            CancellationToken cancellationToken) =>
            AcquireException is null
                ? Task.FromResult(new SubtitleAcquisitionResult(Guid.NewGuid(), AlreadyPresent: false))
                : Task.FromException<SubtitleAcquisitionResult>(AcquireException);

        public Task<AutomaticSubtitleAcquisitionResult> AcquireMissingPreferredAsync(
            Guid videoId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AutomaticSubtitleAcquisitionResult(0, []));
    }
}
