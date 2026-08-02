using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Consumption;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Consumption;

namespace Prismedia.Infrastructure.Tests;

public sealed class ConsumptionStatisticsServiceTests {
    private static readonly Guid VideoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AudioId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid NsfwId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BookId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid AudiobookTrackId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public async Task StatisticsFilterByWindowKindEventKindAndNsfwVisibility() {
        await using var db = CreateContext();
        var now = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        Seed(db, now);
        var service = new EfConsumptionStatisticsService(db, TestUserContext.Admin());

        var visible = await service.GetAsync(
            new ConsumptionStatisticsQuery(
                now.AddDays(-7),
                now.AddSeconds(1),
                Kind: null,
                EventKind: null,
                HideNsfw: true),
            CancellationToken.None);

        Assert.Equal(3, visible.TotalEvents);
        Assert.Equal(1, visible.CompletedCount);
        Assert.Equal(2, visible.SkippedCount);
        Assert.Equal(2, visible.DistinctEntityCount);
        Assert.DoesNotContain(visible.RecentEvents, item => item.EntityId == NsfwId);
        Assert.DoesNotContain(visible.RecentEvents, item => item.EntityId == AudiobookTrackId);
        Assert.Contains(visible.TopEntities, item =>
            item.Id == VideoId &&
            item.CompletedCount == 1 &&
            item.SkippedCount == 1 &&
            item.CoverUrl == "/assets/videos/video/poster.jpg");

        var audioSkips = await service.GetAsync(
            new ConsumptionStatisticsQuery(
                now.AddDays(-7),
                now.AddSeconds(1),
                EntityKind.AudioTrack,
                ConsumptionEventKind.Skipped,
                HideNsfw: true),
            CancellationToken.None);

        Assert.Equal(1, audioSkips.TotalEvents);
        Assert.Equal(0, audioSkips.CompletedCount);
        Assert.Equal(1, audioSkips.SkippedCount);
        Assert.Equal(AudioId, Assert.Single(audioSkips.TopEntities).Id);
        Assert.All(audioSkips.RecentEvents, item => {
            Assert.Equal(EntityKind.AudioTrack, item.EntityKind);
            Assert.Equal(ConsumptionEventKind.Skipped, item.Kind);
        });
    }

    [Fact]
    public async Task StatisticsIncludeOnlyTheCurrentUsersEventsByDefault() {
        await using var db = CreateContext();
        var now = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        var otherUserId = Guid.Parse("faceb00c-0000-4000-8000-000000000002");
        db.Entities.Add(Entity(VideoId, EntityKind.Video, "Visible Video", isNsfw: false, now));
        db.EntityConsumptionEvents.AddRange(
            Event(VideoId, ConsumptionEventKind.Completed, now.AddHours(-3), 120, TestUserContext.UserId),
            Event(VideoId, ConsumptionEventKind.Skipped, now.AddHours(-2), 4, otherUserId),
            Event(VideoId, ConsumptionEventKind.Skipped, now.AddHours(-1), 3, userId: null));
        await db.SaveChangesAsync();
        var service = new EfConsumptionStatisticsService(db, TestUserContext.Admin());

        var statistics = await service.GetAsync(
            new ConsumptionStatisticsQuery(
                now.AddDays(-1),
                now.AddSeconds(1),
                Kind: null,
                EventKind: null,
                HideNsfw: true),
            CancellationToken.None);

        Assert.Equal(1, statistics.TotalEvents);
        Assert.Equal(1, statistics.CompletedCount);
        Assert.Equal(0, statistics.SkippedCount);
        Assert.Equal(1, statistics.DistinctEntityCount);
        Assert.Equal(ConsumptionEventKind.Completed, Assert.Single(statistics.RecentEvents).Kind);
        Assert.Equal(1, Assert.Single(statistics.TopEntities).CompletedCount);
        Assert.Equal(1, Assert.Single(statistics.DailyEvents).CompletedCount);

        var selectedUserStatistics = await service.GetAsync(
            new ConsumptionStatisticsQuery(
                now.AddDays(-1),
                now.AddSeconds(1),
                Kind: null,
                EventKind: null,
                HideNsfw: true,
                UserId: otherUserId),
            CancellationToken.None);

        Assert.Equal(1, selectedUserStatistics.TotalEvents);
        Assert.Equal(0, selectedUserStatistics.CompletedCount);
        Assert.Equal(1, selectedUserStatistics.SkippedCount);

        var allUsersStatistics = await service.GetAsync(
            new ConsumptionStatisticsQuery(
                now.AddDays(-1),
                now.AddSeconds(1),
                Kind: null,
                EventKind: null,
                HideNsfw: true,
                AllUsers: true),
            CancellationToken.None);

        Assert.Equal(3, allUsersStatistics.TotalEvents);
    }

    [Fact]
    public async Task StatisticsProjectFamilyRhythmAndDailyActiveTimeInTheRequestedLocalOffset() {
        await using var db = CreateContext();
        var now = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        db.Entities.AddRange(
            Entity(VideoId, EntityKind.Video, "Visible Video", isNsfw: false, now),
            Entity(AudioId, EntityKind.AudioTrack, "Visible Audio", isNsfw: false, now));
        db.EntityConsumptionEvents.AddRange(
            // 2026-06-18T02:00Z is 2026-06-17T21:00 at -05:00, so the local fold must move this
            // event to the previous calendar day and to Wednesday 21:00 rather than Thursday 02:00.
            Event(VideoId, ConsumptionEventKind.Completed, DateTimeOffset.Parse("2026-06-18T02:00:00Z"), 600, TestUserContext.UserId, durationSeconds: 900),
            Event(VideoId, ConsumptionEventKind.Skipped, DateTimeOffset.Parse("2026-06-18T02:30:00Z"), 5, TestUserContext.UserId, durationSeconds: 900),
            // A position past the reported duration must be clamped to the duration.
            Event(AudioId, ConsumptionEventKind.Completed, DateTimeOffset.Parse("2026-06-18T09:00:00Z"), 500, TestUserContext.UserId, durationSeconds: 200));
        db.EntityConsumptionDays.AddRange(
            Activity(VideoId, ConsumptionActivityKind.Viewing, new DateOnly(2026, 6, 17), 605),
            Activity(AudioId, ConsumptionActivityKind.Listening, new DateOnly(2026, 6, 18), 200));
        await db.SaveChangesAsync();
        var service = new EfConsumptionStatisticsService(db, TestUserContext.Admin());

        var statistics = await service.GetAsync(
            new ConsumptionStatisticsQuery(
                now.AddDays(-7),
                now.AddSeconds(1),
                Kind: null,
                EventKind: null,
                HideNsfw: true,
                UtcOffsetMinutes: -300),
            CancellationToken.None);

        Assert.Equal(805, statistics.ActiveSeconds);
        Assert.Equal(605, statistics.ViewingSeconds);
        Assert.Equal(200, statistics.ListeningSeconds);

        var video = Assert.Single(statistics.KindBreakdown, slice => slice.Kind == EntityKind.Video);
        Assert.Equal(2, video.TotalEvents);
        Assert.Equal(1, video.CompletedCount);
        Assert.Equal(1, video.SkippedCount);
        Assert.Equal(1, video.DistinctEntityCount);
        Assert.Equal(605, video.ActiveSeconds);
        Assert.Equal(200, Assert.Single(statistics.KindBreakdown, slice => slice.Kind == EntityKind.AudioTrack).ActiveSeconds);
        // Ordered by activity, so the two-event video family leads the single-event audio family.
        Assert.Equal(EntityKind.Video, statistics.KindBreakdown[0].Kind);

        Assert.Collection(
            statistics.DailyEvents,
            wednesday => {
                Assert.Equal(new DateOnly(2026, 6, 17), wednesday.Date);
                Assert.Equal(1, wednesday.CompletedCount);
                Assert.Equal(1, wednesday.SkippedCount);
                Assert.Equal(605, wednesday.ActiveSeconds);
                Assert.Equal(605, wednesday.ViewingSeconds);
            },
            thursday => {
                Assert.Equal(new DateOnly(2026, 6, 18), thursday.Date);
                Assert.Equal(1, thursday.CompletedCount);
                Assert.Equal(0, thursday.SkippedCount);
                Assert.Equal(200, thursday.ActiveSeconds);
                Assert.Equal(200, thursday.ListeningSeconds);
            });

        var evening = Assert.Single(statistics.Rhythm, cell => cell.Hour == 21);
        Assert.Equal((int)DayOfWeek.Wednesday, evening.DayOfWeek);
        Assert.Equal(2, evening.CompletedCount + evening.SkippedCount);
        var morning = Assert.Single(statistics.Rhythm, cell => cell.Hour == 4);
        Assert.Equal((int)DayOfWeek.Thursday, morning.DayOfWeek);
        Assert.Equal(1, morning.CompletedCount);

        var topVideo = Assert.Single(statistics.TopEntities, item => item.Id == VideoId);
        Assert.Equal(605, topVideo.ActiveSeconds);
        Assert.Equal(DateTimeOffset.Parse("2026-06-18T02:00:00Z"), topVideo.FirstEventAt);
        Assert.Equal(DateTimeOffset.Parse("2026-06-18T02:30:00Z"), topVideo.LastEventAt);
    }

    [Fact]
    public async Task StatisticsIncludeReadingAndListeningHeartbeatsWithoutCountingThemAsPlaybackEvents() {
        await using var db = CreateContext();
        var now = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        db.Entities.Add(Entity(BookId, EntityKind.Book, "Visible Book", isNsfw: false, now));
        db.EntityConsumptionDays.AddRange(
            Activity(BookId, ConsumptionActivityKind.Reading, DateOnly.FromDateTime(now.Date), 30),
            Activity(BookId, ConsumptionActivityKind.Listening, DateOnly.FromDateTime(now.Date), 15));
        await db.SaveChangesAsync();
        var service = new EfConsumptionStatisticsService(db, TestUserContext.Admin());

        var statistics = await service.GetAsync(
            new ConsumptionStatisticsQuery(
                now.AddDays(-1),
                now.AddSeconds(1),
                Kind: null,
                EventKind: null,
                HideNsfw: true),
            CancellationToken.None);

        Assert.Equal(0, statistics.TotalEvents);
        Assert.Equal(1, statistics.DistinctEntityCount);
        Assert.Equal(45, statistics.ActiveSeconds);
        Assert.Equal(30, statistics.ReadingSeconds);
        Assert.Equal(15, statistics.ListeningSeconds);
        Assert.Equal(45, Assert.Single(statistics.TopEntities).ActiveSeconds);
        Assert.Equal(45, Assert.Single(statistics.DailyEvents).ActiveSeconds);
        Assert.Empty(statistics.Rhythm);
        Assert.Equal(45, Assert.Single(statistics.KindBreakdown).ActiveSeconds);
        Assert.Empty(statistics.RecentEvents);
    }

    [Fact]
    public async Task StatisticsExcludeRestrictedAndDisabledLibraryEntitiesForEventsAndActivity() {
        await using var db = CreateContext();
        var now = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        var visibleRootId = Guid.NewGuid();
        var restrictedRootId = Guid.NewGuid();
        var disabledRootId = Guid.NewGuid();
        var visibleBookId = Guid.NewGuid();
        var restrictedBookId = Guid.NewGuid();
        var disabledBookId = Guid.NewGuid();

        db.LibraryRoots.AddRange(
            new LibraryRootRow { Id = visibleRootId, Path = "/media/visible", Label = "Visible", Enabled = true, CreatedAt = now, UpdatedAt = now },
            new LibraryRootRow { Id = restrictedRootId, Path = "/media/restricted", Label = "Restricted", Enabled = true, CreatedAt = now, UpdatedAt = now },
            new LibraryRootRow { Id = disabledRootId, Path = "/media/disabled", Label = "Disabled", Enabled = false, CreatedAt = now, UpdatedAt = now });
        db.Entities.AddRange(
            Entity(visibleBookId, EntityKind.Book, "Visible Book", isNsfw: false, now),
            Entity(restrictedBookId, EntityKind.Book, "Restricted Book", isNsfw: false, now),
            Entity(disabledBookId, EntityKind.Book, "Disabled Book", isNsfw: false, now));
        db.EntityLibraryRoots.AddRange(
            new EntityLibraryRootRow { EntityId = visibleBookId, LibraryRootId = visibleRootId },
            new EntityLibraryRootRow { EntityId = restrictedBookId, LibraryRootId = restrictedRootId },
            new EntityLibraryRootRow { EntityId = disabledBookId, LibraryRootId = disabledRootId });
        db.EntityConsumptionEvents.AddRange(
            Event(visibleBookId, ConsumptionEventKind.Completed, now.AddMinutes(-3), 30, TestUserContext.UserId),
            Event(restrictedBookId, ConsumptionEventKind.Completed, now.AddMinutes(-2), 30, TestUserContext.UserId),
            Event(disabledBookId, ConsumptionEventKind.Completed, now.AddMinutes(-1), 30, TestUserContext.UserId));
        db.EntityConsumptionDays.AddRange(
            Activity(visibleBookId, ConsumptionActivityKind.Reading, DateOnly.FromDateTime(now.Date), 10),
            Activity(restrictedBookId, ConsumptionActivityKind.Reading, DateOnly.FromDateTime(now.Date), 10),
            Activity(disabledBookId, ConsumptionActivityKind.Reading, DateOnly.FromDateTime(now.Date), 10));
        await db.SaveChangesAsync();

        var service = new EfConsumptionStatisticsService(db, TestUserContext.Member(visibleRootId));
        var statistics = await service.GetAsync(
            new ConsumptionStatisticsQuery(now.AddDays(-1), now.AddSeconds(1), null, null, HideNsfw: true),
            CancellationToken.None);

        Assert.Equal(1, statistics.TotalEvents);
        Assert.Equal(1, statistics.DistinctEntityCount);
        Assert.Equal(10, statistics.ActiveSeconds);
        Assert.Equal(10, statistics.ReadingSeconds);
        Assert.Equal(visibleBookId, Assert.Single(statistics.TopEntities).Id);
        Assert.Equal(visibleBookId, Assert.Single(statistics.RecentEvents).EntityId);
    }

    private static void Seed(PrismediaDbContext db, DateTimeOffset now) {
        db.Entities.AddRange(
            Entity(VideoId, EntityKind.Video, "Visible Video", isNsfw: false, now),
            Entity(AudioId, EntityKind.AudioTrack, "Visible Audio", isNsfw: false, now),
            Entity(NsfwId, EntityKind.Video, "Hidden Video", isNsfw: true, now),
            Entity(BookId, EntityKind.Book, "Spoken Story", isNsfw: false, now),
            Entity(AudiobookTrackId, EntityKind.AudioTrack, "Book Chapter", isNsfw: false, now, BookId));
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = VideoId,
            Role = EntityFileRole.Poster,
            Path = "/assets/videos/video/poster.jpg",
            MimeType = "image/jpeg",
            CreatedAt = now
        });
        db.EntityConsumptionEvents.AddRange(
            Event(VideoId, ConsumptionEventKind.Completed, now.AddDays(-1), 120, TestUserContext.UserId),
            Event(VideoId, ConsumptionEventKind.Skipped, now.AddHours(-3), 4, TestUserContext.UserId),
            Event(AudioId, ConsumptionEventKind.Skipped, now.AddHours(-1), 3, TestUserContext.UserId),
            Event(AudiobookTrackId, ConsumptionEventKind.Completed, now.AddMinutes(-30), 600, TestUserContext.UserId),
            Event(NsfwId, ConsumptionEventKind.Completed, now.AddHours(-2), 300, TestUserContext.UserId),
            Event(AudioId, ConsumptionEventKind.Completed, now.AddDays(-30), 90, TestUserContext.UserId));
        db.SaveChanges();
    }

    private static EntityRow Entity(
        Guid id,
        EntityKind kind,
        string title,
        bool isNsfw,
        DateTimeOffset now,
        Guid? parentEntityId = null) =>
        new() {
            Id = id,
            KindCode = kind.ToCode(),
            Title = title,
            IsNsfw = isNsfw,
            ParentEntityId = parentEntityId,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static EntityConsumptionEventRow Event(
        Guid entityId,
        ConsumptionEventKind kind,
        DateTimeOffset occurredAt,
        double? positionSeconds,
        Guid? userId,
        double? durationSeconds = null) =>
        new() {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            UserId = userId,
            Kind = kind,
            OccurredAt = occurredAt,
            PositionSeconds = positionSeconds,
            DurationSeconds = durationSeconds,
            CreatedAt = occurredAt
        };

    private static EntityConsumptionDayRow Activity(
        Guid entityId,
        ConsumptionActivityKind kind,
        DateOnly activityDate,
        double durationSeconds) =>
        new() {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            UserId = TestUserContext.UserId,
            Kind = kind,
            ActivityDate = activityDate,
            DurationSeconds = durationSeconds,
            UpdatedAt = activityDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
        };

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
