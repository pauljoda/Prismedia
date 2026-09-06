using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Requests;

namespace Prismedia.Infrastructure.Tests;

public sealed class RequestReviewPreparationServiceTests {
    [Fact]
    public async Task StartReturnsTheSeedAndDeduplicatesWhileBackgroundEnrichmentRuns() {
        var source = new ControlledProgressiveReviewSource();
        using var services = new ServiceCollection()
            .AddSingleton(source)
            .AddScoped<IPluginRequestProgressiveReviewSource>(provider =>
                provider.GetRequiredService<ControlledProgressiveReviewSource>())
            .BuildServiceProvider();
        var service = new RequestReviewPreparationService(
            services.GetRequiredService<IServiceScopeFactory>(),
            new TestApplicationLifetime());
        var request = new RequestReviewRequest(
            RequestMediaKind.Series,
            "series-metadata",
            new ExternalIdentity("tmdb", "series:one"));

        var first = await service.StartAsync(request, hideNsfw: true, CancellationToken.None);
        var repeated = await service.StartAsync(request, hideNsfw: true, CancellationToken.None);

        Assert.NotNull(first);
        Assert.True(first.Enrichment!.Running);
        Assert.Equal(["person-one", "season-one"], first.Enrichment.PendingProposalIds);
        Assert.Equal(first.Enrichment.ReviewId, repeated!.Enrichment!.ReviewId);
        Assert.Equal(1, source.StartCount);
        await source.EnrichmentStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        source.AllowEnrichment.TrySetResult();
        RequestReviewResponse? completed = null;
        for (var attempt = 0; attempt < 100; attempt++) {
            completed = service.Get(first.Enrichment.ReviewId);
            if (completed?.Enrichment?.Running == false) {
                break;
            }
            await Task.Delay(10);
        }

        Assert.NotNull(completed);
        Assert.False(completed.Enrichment!.Running);
        Assert.Empty(completed.Enrichment.PendingProposalIds);
        Assert.Equal("Hydrated person", Assert.Single(completed.Proposal.Relationships).Patch.Title);
        Assert.Equal("Episode 1", Assert.Single(Assert.Single(completed.Proposal.Children).Children).Patch.Title);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReopeningAReviewRechecksTheProviderRevisionAndAvailability(bool unavailable) {
        var source = new ControlledProgressiveReviewSource(empty: true);
        using var services = new ServiceCollection()
            .AddSingleton<IPluginRequestProgressiveReviewSource>(source)
            .BuildServiceProvider();
        var service = new RequestReviewPreparationService(
            services.GetRequiredService<IServiceScopeFactory>(), new TestApplicationLifetime());
        var request = new RequestReviewRequest(RequestMediaKind.Series, "series-metadata", new ExternalIdentity("tmdb", "series:updated"));
        var first = await service.StartAsync(request, false, CancellationToken.None);
        var cached = await service.StartAsync(request, false, CancellationToken.None);
        Assert.Equal(first!.Enrichment!.ReviewId, cached!.Enrichment!.ReviewId);
        Assert.Equal(1, source.StartCount);
        source.ProviderRevision = unavailable ? null : "2.0.1";

        var refreshed = await service.StartAsync(request, false, CancellationToken.None);

        if (unavailable) {
            Assert.Null(refreshed);
            Assert.Equal(1, source.StartCount);
        } else {
            Assert.NotNull(refreshed);
            Assert.NotEqual(first.Enrichment.ReviewId, refreshed.Enrichment!.ReviewId);
            Assert.Equal(2, source.StartCount);
        }
        // A pinned open review remains readable; reopening must choose current provider evidence.
        Assert.NotNull(service.Get(first.Enrichment.ReviewId));
    }

    [Fact]
    public async Task FailedEnrichmentLeavesThePartialReviewUsableWithoutIdentifyingForever() {
        var source = new ControlledProgressiveReviewSource(fail: true);
        using var services = new ServiceCollection()
            .AddSingleton(source)
            .AddScoped<IPluginRequestProgressiveReviewSource>(provider =>
                provider.GetRequiredService<ControlledProgressiveReviewSource>())
            .BuildServiceProvider();
        var service = new RequestReviewPreparationService(
            services.GetRequiredService<IServiceScopeFactory>(),
            new TestApplicationLifetime());

        var started = await service.StartAsync(
            new RequestReviewRequest(
                RequestMediaKind.Series,
                "series-metadata",
                new ExternalIdentity("tmdb", "series:failed")),
            hideNsfw: true,
            CancellationToken.None);
        source.AllowEnrichment.TrySetResult();

        RequestReviewResponse? failed = null;
        for (var attempt = 0; attempt < 100; attempt++) {
            failed = service.Get(started!.Enrichment!.ReviewId);
            if (failed?.Enrichment?.Running == false) {
                break;
            }
            await Task.Delay(10);
        }

        Assert.NotNull(failed);
        Assert.False(failed.Enrichment!.Running);
        Assert.Empty(failed.Enrichment.PendingProposalIds);
        Assert.Contains("Provider failed", failed.Enrichment.Error, StringComparison.Ordinal);
    }

    private sealed class ControlledProgressiveReviewSource(bool fail = false, bool empty = false) : IPluginRequestProgressiveReviewSource {
        public string? ProviderRevision { get; set; } = "2.0.0";
        public Task<string?> GetReviewProviderRevisionAsync(RequestReviewRequest request, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderRevision);
        public int StartCount { get; private set; }
        public TaskCompletionSource EnrichmentStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowEnrichment { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<RequestReviewResponse?> StartReviewAsync(
            RequestReviewRequest request,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            StartCount++;
            var review = Review(request, hydrated: false);
            if (empty) {
                review = review with { Proposal = review.Proposal with { Children = [], Relationships = [] } };
            }
            return Task.FromResult<RequestReviewResponse?>(review);
        }

        public async Task<RequestReviewResponse> EnrichReviewAsync(
            RequestReviewResponse seed,
            bool hideNsfw,
            Func<RequestReviewProgressUpdate, CancellationToken, Task> publish,
            CancellationToken cancellationToken) {
            EnrichmentStarted.TrySetResult();
            await AllowEnrichment.Task.WaitAsync(cancellationToken);
            if (fail) {
                throw new InvalidOperationException("Provider failed.");
            }
            var relationship = Review(
                new RequestReviewRequest(seed.Kind, seed.PluginId, seed.ExternalIdentity),
                hydrated: false) with {
                    Proposal = seed.Proposal with {
                        Relationships = [Person("Hydrated person")]
                    }
                };
            await publish(new RequestReviewProgressUpdate("person-one", relationship), cancellationToken);
            var completed = Review(
                new RequestReviewRequest(seed.Kind, seed.PluginId, seed.ExternalIdentity),
                hydrated: true) with {
                    Proposal = relationship.Proposal with {
                        Children = [Season(withEpisode: true)]
                    }
                };
            await publish(new RequestReviewProgressUpdate("season-one", completed), cancellationToken);
            return completed;
        }

        private static RequestReviewResponse Review(RequestReviewRequest request, bool hydrated) {
            var root = new EntityMetadataProposal(
                "series-root",
                request.PluginId,
                EntityKind.VideoSeries,
                1,
                "external-id",
                Patch("Series", request.ExternalIdentity),
                [],
                [Season(hydrated)],
                [],
                null,
                [Person(hydrated ? "Hydrated person" : "Person shell")]);
            return new RequestReviewResponse(
                request.PluginId,
                request.ExternalIdentity,
                EntityKind.VideoSeries,
                request.Kind,
                root,
                hydrated ? "complete" : "seed",
                [
                    new("series-root", RequestMediaKind.Series, EntityKind.VideoSeries, request.ExternalIdentity, true),
                    new("season-one", RequestMediaKind.Season, EntityKind.VideoSeason, new ExternalIdentity("tvdb", "season:one"), true)
                ]);
        }

        private static EntityMetadataProposal Season(bool withEpisode) =>
            new(
                "season-one",
                "series-metadata",
                EntityKind.VideoSeason,
                1,
                "structure",
                Patch("Season 1", new ExternalIdentity("tvdb", "season:one")),
                [],
                withEpisode
                    ? [new EntityMetadataProposal(
                        "episode-one",
                        "series-metadata",
                        EntityKind.VideoEpisode,
                        1,
                        "structure",
                        Patch("Episode 1", new ExternalIdentity("episode-db", "episode:one")),
                        [],
                        [],
                        [],
                        null,
                        [])]
                    : [],
                [],
                null,
                []);

        private static EntityMetadataProposal Person(string title) =>
            new(
                "person-one",
                "series-metadata",
                EntityKind.Person,
                1,
                "relationship",
                Patch(title, new ExternalIdentity("tmdb", "person:one")),
                [],
                [],
                [],
                null,
                []);

        private static EntityMetadataPatch Patch(string title, ExternalIdentity identity) =>
            new(
                title,
                null,
                new Dictionary<string, string> { [identity.Namespace] = identity.Value },
                [],
                [],
                null,
                [],
                new Dictionary<string, string>(),
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                null);
    }

    private sealed class TestApplicationLifetime : IHostApplicationLifetime {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
