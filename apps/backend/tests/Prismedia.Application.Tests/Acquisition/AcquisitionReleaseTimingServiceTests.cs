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

    private static AcquisitionReleaseTimingService Create(
        AcquisitionReleaseTimingPolicy policy,
        EntityDate? date,
        DateTimeOffset now) =>
        new(new StubProfiles(policy), new StubDates(date), new FixedTimeProvider(now));

    private sealed class StubDates(EntityDate? date) : IEntityReleaseDateStore {
        public Task<EntityDate?> GetAsync(Guid entityId, EntityDateType type, CancellationToken cancellationToken) =>
            Task.FromResult(date);
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
