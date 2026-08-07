using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class AcquisitionReleaseTimingServiceTests {
    private static readonly Guid EntityId = Guid.Parse("22222222-3333-4444-8555-666666666666");

    [Fact]
    public async Task ExactFutureMilestoneWaitsUntilDatePlusDelay() {
        var service = Create(
            new AcquisitionReleaseTimingPolicy(EntityDateType.DigitalRelease, 2),
            new EntityDate(
                EntityDateType.DigitalRelease.ToCode(),
                "2026-08-14",
                new DateOnly(2026, 8, 14),
                DatePrecision.Day.ToCode()),
            new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero));

        var decision = await service.EvaluateAsync(EntityId, null, EntityKind.Movie, CancellationToken.None);

        Assert.False(decision.CanSearch);
        Assert.Equal(new DateOnly(2026, 8, 16), decision.SearchNotBefore);
    }

    [Fact]
    public async Task MonthPrecisionUsesTheLastDayBeforeApplyingDelay() {
        var service = Create(
            new AcquisitionReleaseTimingPolicy(EntityDateType.PhysicalRelease, 1),
            new EntityDate(
                EntityDateType.PhysicalRelease.ToCode(),
                "2026-09",
                new DateOnly(2026, 9, 1),
                DatePrecision.Month.ToCode()),
            new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));

        var waiting = await service.EvaluateAsync(EntityId, null, EntityKind.Movie, CancellationToken.None);
        Assert.False(waiting.CanSearch);
        Assert.Equal(new DateOnly(2026, 10, 1), waiting.SearchNotBefore);

        var readyService = Create(
            new AcquisitionReleaseTimingPolicy(EntityDateType.PhysicalRelease, 1),
            waiting.Date,
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.True((await readyService.EvaluateAsync(EntityId, null, EntityKind.Movie, CancellationToken.None)).CanSearch);
    }

    [Fact]
    public async Task MissingConfiguredMilestoneWaitsForProviderMetadata() {
        var service = Create(
            new AcquisitionReleaseTimingPolicy(EntityDateType.StreamingRelease, 0),
            date: null,
            new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero));

        var decision = await service.EvaluateAsync(EntityId, null, EntityKind.Movie, CancellationToken.None);

        Assert.False(decision.CanSearch);
        Assert.True(decision.WaitingForMetadata);
    }

    [Fact]
    public async Task FutureEpisodeAirDateWaitsWithoutAProfileGate() {
        var airDate = new EntityDate(
            EntityDateType.Air.ToCode(),
            "2026-08-11",
            new DateOnly(2026, 8, 11),
            DatePrecision.Day.ToCode());
        var service = Create(
            AcquisitionReleaseTimingPolicy.Immediate,
            airDate,
            new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero));

        var decision = await service.EvaluateAsync(
            EntityId,
            profileId: null,
            EntityKind.VideoEpisode,
            CancellationToken.None);

        Assert.False(decision.CanSearch);
        Assert.False(decision.WaitingForMetadata);
        Assert.Equal(EntityDateType.Air, decision.DateType);
        Assert.Equal(EntityDateType.Air, decision.ResolvedDateType);
        Assert.Equal(airDate, decision.Date);
        Assert.Equal(new DateOnly(2026, 8, 11), decision.SearchNotBefore);
    }

    [Fact]
    public async Task EpisodeWithoutAirDateWaitsForProviderMetadataBeforeSearching() {
        var service = Create(
            AcquisitionReleaseTimingPolicy.Immediate,
            date: null,
            new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero));

        var decision = await service.EvaluateAsync(
            EntityId,
            profileId: null,
            EntityKind.VideoEpisode,
            CancellationToken.None);

        Assert.False(decision.CanSearch);
        Assert.True(decision.WaitingForMetadata);
    }

    [Fact]
    public async Task SeasonWithFutureEpisodesSearchesChildrenInsteadOfAnUnreleasedPack() {
        var latestAirDate = new EntityDate(
            EntityDateType.Air.ToCode(),
            "2026-08-11",
            new DateOnly(2026, 8, 11),
            DatePrecision.Day.ToCode());
        var service = Create(
            AcquisitionReleaseTimingPolicy.Immediate,
            date: null,
            new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero),
            new EntityReleaseDateCoverage(6, 6, latestAirDate));

        var decision = await service.EvaluateAsync(
            EntityId,
            profileId: null,
            EntityKind.VideoSeason,
            CancellationToken.None);

        Assert.False(decision.CanSearch);
        Assert.False(decision.WaitingForMetadata);
        Assert.True(decision.PreferChildAcquisitions);
        Assert.Equal(new DateOnly(2026, 8, 11), decision.SearchNotBefore);
    }

    [Fact]
    public async Task SeasonWaitsForCompleteEpisodeDateCoverageBeforeIndexerWork() {
        var service = Create(
            AcquisitionReleaseTimingPolicy.Immediate,
            date: null,
            new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero),
            new EntityReleaseDateCoverage(6, 5, LatestDate: null));

        var decision = await service.EvaluateAsync(
            EntityId,
            profileId: null,
            EntityKind.VideoSeason,
            CancellationToken.None);

        Assert.False(decision.CanSearch);
        Assert.True(decision.WaitingForMetadata);
        Assert.True(decision.PreferChildAcquisitions);
    }

    [Fact]
    public async Task SeasonWithEveryEpisodeAiredCanSearchAsAWholeUnit() {
        var latestAirDate = new EntityDate(
            EntityDateType.Air.ToCode(),
            "2026-08-06",
            new DateOnly(2026, 8, 6),
            DatePrecision.Day.ToCode());
        var service = Create(
            AcquisitionReleaseTimingPolicy.Immediate,
            date: null,
            new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero),
            new EntityReleaseDateCoverage(6, 6, latestAirDate));

        var decision = await service.EvaluateAsync(
            EntityId,
            profileId: null,
            EntityKind.VideoSeason,
            CancellationToken.None);

        Assert.True(decision.CanSearch);
        Assert.False(decision.PreferChildAcquisitions);
    }

    [Fact]
    public async Task StreamingPreferenceFallsBackToDigitalVodDate() {
        var digitalDate = new EntityDate(
            EntityDateType.DigitalRelease.ToCode(),
            "2026-07-25",
            new DateOnly(2026, 7, 25),
            DatePrecision.Day.ToCode());
        var service = new AcquisitionReleaseTimingService(
            new StubProfiles(new AcquisitionReleaseTimingPolicy(EntityDateType.StreamingRelease, 0)),
            new StubDates(new Dictionary<EntityDateType, EntityDate> {
                [EntityDateType.DigitalRelease] = digitalDate
            }),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero)));

        var decision = await service.EvaluateAsync(EntityId, null, EntityKind.Movie, CancellationToken.None);

        Assert.False(decision.CanSearch);
        Assert.False(decision.WaitingForMetadata);
        Assert.Equal(EntityDateType.StreamingRelease, decision.DateType);
        Assert.Equal(EntityDateType.DigitalRelease, decision.ResolvedDateType);
        Assert.Equal(digitalDate, decision.Date);
        Assert.Equal(new DateOnly(2026, 7, 25), decision.SearchNotBefore);
        Assert.Contains("using the digital release date", decision.Message);
    }

    [Fact]
    public void SupportedMilestonesResolveThroughTheProfileDefinition() {
        Assert.True(AcquisitionReleaseTimingService.Supports(EntityKind.VideoEpisode, EntityDateType.FirstAir));
        Assert.True(AcquisitionReleaseTimingService.Supports(EntityKind.Book, EntityDateType.Publication));
        Assert.False(AcquisitionReleaseTimingService.Supports(EntityKind.AudioLibrary, EntityDateType.TheatricalRelease));
    }

    private static AcquisitionReleaseTimingService Create(
        AcquisitionReleaseTimingPolicy policy,
        EntityDate? date,
        DateTimeOffset now,
        EntityReleaseDateCoverage? childCoverage = null) =>
        new(new StubProfiles(policy), new StubDates(date, childCoverage), new FixedTimeProvider(now));

    private sealed class StubDates(
        EntityDate? date,
        EntityReleaseDateCoverage? childCoverage = null) : IEntityReleaseDateStore {
        public StubDates(IReadOnlyDictionary<EntityDateType, EntityDate> dates) : this((EntityDate?)null) {
            Dates = dates;
        }

        private IReadOnlyDictionary<EntityDateType, EntityDate>? Dates { get; }

        public Task<EntityDate?> GetAsync(Guid entityId, EntityDateType type, CancellationToken cancellationToken) =>
            Task.FromResult(Dates?.GetValueOrDefault(type) ?? date);

        public Task<EntityReleaseDateCoverage> GetDirectChildCoverageAsync(
            Guid parentEntityId,
            EntityKind childKind,
            EntityDateType type,
            CancellationToken cancellationToken) =>
            Task.FromResult(childCoverage ?? new EntityReleaseDateCoverage(0, 0, LatestDate: null));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubProfiles(AcquisitionReleaseTimingPolicy policy) : IBookAcquisitionProfileStore {
        public Task<AcquisitionReleaseTimingPolicy> GetReleaseTimingAsync(
            Guid? profileId,
            EntityKind kind,
            CancellationToken cancellationToken) => Task.FromResult(policy);

        public Task<BookAcquisitionRules> GetRulesAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BookImportProfile?> GetImportProfileAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> GetAutoPickAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> GetAutoRedownloadAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> GetDownloadCategoryAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<BookAcquisitionProfileView>> ListAsync(bool hideNsfw, IReadOnlySet<Guid>? allowedRootIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BookAcquisitionProfileView?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BookAcquisitionProfileView> SaveAsync(BookAcquisitionProfileSaveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
