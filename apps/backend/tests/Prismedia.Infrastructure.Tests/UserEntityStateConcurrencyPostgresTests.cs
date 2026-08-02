using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Playback;
using Prismedia.Application.Security;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using static Prismedia.Infrastructure.Tests.EntityConcurrencyTestSupport;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// PostgreSQL regressions for the single wide user-state row shared by ratings, playback, and
/// reading progress. These tests require the real xmin system column; the in-memory provider
/// cannot exercise the update predicates or concurrent first-insert path.
/// </summary>
public sealed class UserEntityStateConcurrencyPostgresTests {
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ConcurrentUpdatesPreserveSiblingFieldsAndTheLatestResumeSignal() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        await SeedAsync(database, userId, entityId, EntityKind.Video, includeState: true);

        await using var ratingContext = database.CreateContext();
        await using var earlierPlaybackContext = database.CreateContext();
        await using var laterPlaybackContext = database.CreateContext();
        var gate = new SaveBarrier(3);
        var earlierReportAt = DateTimeOffset.Parse("2026-07-31T18:00:00Z");
        var laterReportAt = earlierReportAt.AddSeconds(1);
        var ratingService = CreateService(
            ratingContext,
            userId,
            new GatedEntityWriteRepository(CreateRepository(ratingContext, userId), gate));
        var earlierPlaybackService = CreateService(
            earlierPlaybackContext,
            userId,
            new GatedEntityWriteRepository(CreateRepository(earlierPlaybackContext, userId), gate),
            new FixedTimeProvider(earlierReportAt));
        var laterPlaybackService = CreateService(
            laterPlaybackContext,
            userId,
            new GatedEntityWriteRepository(CreateRepository(laterPlaybackContext, userId), gate),
            new FixedTimeProvider(laterReportAt));

        await Task.WhenAll(
            ratingService.RateAsync(entityId, 5, CancellationToken.None),
            earlierPlaybackService.UpdateConsumptionAsync(entityId, 60, null, null, CancellationToken.None),
            laterPlaybackService.UpdateConsumptionAsync(entityId, 90, null, null, CancellationToken.None));

        await using var verification = database.CreateContext();
        var state = await verification.UserEntityStates.SingleAsync(row =>
            row.UserId == userId && row.EntityId == entityId);
        Assert.Equal(5, state.RatingValue);
        Assert.Equal(90, state.ResumeSeconds);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ConcurrentFirstStateInsertIsMappedAndRetriedAsOneMergedRow() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        await SeedAsync(database, userId, entityId, EntityKind.Video, includeState: false);

        await using var ratingContext = database.CreateContext();
        await using var playbackContext = database.CreateContext();
        var gate = new SaveBarrier(2);
        var ratingService = CreateService(
            ratingContext,
            userId,
            new GatedEntityWriteRepository(CreateRepository(ratingContext, userId), gate));
        var playbackService = CreateService(
            playbackContext,
            userId,
            new GatedEntityWriteRepository(CreateRepository(playbackContext, userId), gate));

        await Task.WhenAll(
            ratingService.RateAsync(entityId, 4, CancellationToken.None),
            playbackService.UpdateConsumptionAsync(entityId, 75, null, null, CancellationToken.None));

        await using var verification = database.CreateContext();
        var states = await verification.UserEntityStates
            .Where(row => row.UserId == userId && row.EntityId == entityId)
            .ToArrayAsync();
        var state = Assert.Single(states);
        Assert.Equal(4, state.RatingValue);
        Assert.Equal(75, state.ResumeSeconds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "PostgreSQL")]
    public async Task RetriedCompletionConflictCommitsExactlyOnePlaybackEvent(bool useAmbientTransaction) {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        await SeedAsync(database, userId, entityId, EntityKind.Video, includeState: true);
        var newerSignalAt = DateTimeOffset.Parse("2026-07-31T18:00:00Z");
        var historicalCompletionAt = newerSignalAt.AddMinutes(-5);
        await SetPlaybackStateAsync(
            database,
            userId,
            entityId,
            resumeSeconds: 120,
            lastPlayedAt: newerSignalAt,
            completedAt: null);

        await using var context = database.CreateContext();
        await using var ambientTransaction = useAmbientTransaction
            ? await context.Database.BeginTransactionAsync()
            : null;
        var repository = new ConflictOnceEntityWriteRepository(
            CreateRepository(context, userId),
            cancellationToken => TouchStateAsync(database, userId, entityId, cancellationToken));
        var service = CreateService(context, userId, repository);

        await service.RecordCompletedConsumptionAsync(
            entityId,
            historicalCompletionAt,
            positionSeconds: null,
            durationSeconds: null,
            CancellationToken.None);
        if (ambientTransaction is not null) {
            await ambientTransaction.CommitAsync();
        }

        await using var verification = database.CreateContext();
        var completedEvent = Assert.Single(await verification.EntityConsumptionEvents
            .Where(row => row.UserId == userId && row.EntityId == entityId &&
                          row.Kind == ConsumptionEventKind.Completed)
            .ToArrayAsync());
        Assert.Equal(historicalCompletionAt, completedEvent.OccurredAt);
        var state = await verification.UserEntityStates.SingleAsync(row =>
            row.UserId == userId && row.EntityId == entityId);
        Assert.Equal(1, state.CompletionCount);
        Assert.Equal(120, state.ResumeSeconds);
        Assert.Equal(newerSignalAt, state.LastActiveAt);
        Assert.Null(state.CompletedAt);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task RetriedReadingActivityConflictCommitsExactlyOneActivityEvent() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        await SeedAsync(database, userId, entityId, EntityKind.Book, includeState: true);
        var newerProgressAt = DateTimeOffset.Parse("2026-07-31T18:00:00Z");
        var historicalResetAt = newerProgressAt.AddMinutes(-5);
        await SetProgressStateAsync(
            database,
            userId,
            entityId,
            index: 4,
            total: 10,
            updatedAt: newerProgressAt,
            completedAt: newerProgressAt);

        await using var context = database.CreateContext();
        var repository = new ConflictOnceEntityWriteRepository(
            CreateRepository(context, userId),
            cancellationToken => TouchStateAsync(database, userId, entityId, cancellationToken));
        var service = CreateService(context, userId, repository, new FixedTimeProvider(historicalResetAt));

        await service.UpdateProgressAsync(
            entityId,
            entityId,
            ProgressUnit.Page,
            index: 0,
            total: 10,
            mode: ReaderMode.Paged,
            completed: null,
            reset: true,
            location: "chapter-1",
            activitySeconds: 15,
            activityKind: ConsumptionActivityKind.Reading,
            CancellationToken.None);

        await using var verification = database.CreateContext();
        var activity = Assert.Single(await verification.EntityConsumptionDays
            .Where(row => row.UserId == userId && row.EntityId == entityId)
            .ToArrayAsync());
        Assert.Equal(ConsumptionActivityKind.Reading, activity.Kind);
        Assert.Equal(15, activity.DurationSeconds);
        var state = await verification.UserEntityStates.SingleAsync(row =>
            row.UserId == userId && row.EntityId == entityId);
        Assert.Equal(15, state.ActiveSeconds);
        Assert.Equal(4, state.ProgressIndex);
        Assert.Equal(newerProgressAt, state.ProgressUpdatedAt);
        Assert.Equal(newerProgressAt, state.ProgressCompletedAt);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ProgressOrderingUsesItsDedicatedTimestampInsteadOfSiblingStateRecency() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        await SeedAsync(database, userId, entityId, EntityKind.Book, includeState: true);
        var progressAt = DateTimeOffset.Parse("2026-07-31T12:00:00Z");
        var progressSignalAt = progressAt.AddMinutes(1);
        await SetProgressStateAsync(
            database,
            userId,
            entityId,
            index: 1,
            total: 10,
            updatedAt: progressAt,
            completedAt: null);
        await using (var siblingUpdate = database.CreateContext()) {
            var siblingState = await siblingUpdate.UserEntityStates.FindAsync([userId, entityId]);
            Assert.NotNull(siblingState);
            siblingState!.RatingValue = 5;
            siblingState.LastActiveAt = progressAt.AddHours(1);
            siblingState.UpdatedAt = progressAt.AddHours(1);
            await siblingUpdate.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var service = CreateService(
            context,
            userId,
            CreateRepository(context, userId),
            new FixedTimeProvider(progressSignalAt));
        await service.UpdateProgressAsync(
            entityId,
            entityId,
            ProgressUnit.Page,
            index: 2,
            total: 10,
            mode: ReaderMode.Paged,
            completed: null,
            reset: false,
            location: "page-2",
            activitySeconds: null,
            activityKind: null,
            CancellationToken.None);

        await using var verification = database.CreateContext();
        var state = await verification.UserEntityStates.SingleAsync(row =>
            row.UserId == userId && row.EntityId == entityId);
        Assert.Equal(2, state.ProgressIndex);
        Assert.Equal(progressSignalAt, state.ProgressUpdatedAt);
        Assert.Equal(5, state.RatingValue);
        Assert.Equal(progressAt.AddHours(1), state.LastActiveAt);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task SharedSessionIdsDeduplicateAccessPerEntity() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var userId = Guid.NewGuid();
        var firstEntityId = Guid.NewGuid();
        var secondEntityId = Guid.NewGuid();
        await SeedAsync(database, userId, firstEntityId, EntityKind.AudioTrack, includeState: false);
        await using (var seed = database.CreateContext()) {
            var now = DateTimeOffset.UtcNow;
            seed.Entities.Add(new Prismedia.Infrastructure.Persistence.Entities.EntityRow {
                Id = secondEntityId,
                KindCode = EntityKind.AudioTrack.ToCode(),
                Title = "Second track",
                CreatedAt = now,
                UpdatedAt = now
            });
            await seed.SaveChangesAsync();
        }

        const string sharedSessionId = "shared-player-session";
        await using (var firstContext = database.CreateContext()) {
            var firstService = CreateService(
                firstContext,
                userId,
                CreateRepository(firstContext, userId));
            await firstService.RecordAccessedAsync(
                firstEntityId,
                DateTimeOffset.UtcNow,
                positionSeconds: 0,
                durationSeconds: 180,
                sharedSessionId,
                CancellationToken.None);
        }
        await using (var secondContext = database.CreateContext()) {
            var secondService = CreateService(
                secondContext,
                userId,
                CreateRepository(secondContext, userId));
            await secondService.RecordAccessedAsync(
                secondEntityId,
                DateTimeOffset.UtcNow,
                positionSeconds: 0,
                durationSeconds: 200,
                sharedSessionId,
                CancellationToken.None);
        }

        await using var verification = database.CreateContext();
        var events = await verification.EntityConsumptionEvents
            .Where(row => row.UserId == userId &&
                          row.SessionId == sharedSessionId &&
                          row.Kind == ConsumptionEventKind.Accessed)
            .OrderBy(row => row.EntityId)
            .ToArrayAsync();
        Assert.Equal(2, events.Length);
        Assert.Equal(
            new[] { firstEntityId, secondEntityId }.Order().ToArray(),
            events.Select(row => row.EntityId).Order().ToArray());
    }

}
