using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Playback;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Playback;

namespace Prismedia.Infrastructure.Tests;

public sealed class PlaybackStatisticsServiceTests {
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
        var service = new EfPlaybackStatisticsService(db, TestUserContext.Admin());

        var visible = await service.GetAsync(
            new PlaybackStatisticsQuery(
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
            new PlaybackStatisticsQuery(
                now.AddDays(-7),
                now.AddSeconds(1),
                EntityKind.AudioTrack,
                PlaybackEventKind.Skipped,
                HideNsfw: true),
            CancellationToken.None);

        Assert.Equal(1, audioSkips.TotalEvents);
        Assert.Equal(0, audioSkips.CompletedCount);
        Assert.Equal(1, audioSkips.SkippedCount);
        Assert.Equal(AudioId, Assert.Single(audioSkips.TopEntities).Id);
        Assert.All(audioSkips.RecentEvents, item => {
            Assert.Equal(EntityKind.AudioTrack, item.EntityKind);
            Assert.Equal(PlaybackEventKind.Skipped, item.Kind);
        });
    }

    [Fact]
    public async Task StatisticsIncludeOnlyTheCurrentUsersEventsByDefault() {
        await using var db = CreateContext();
        var now = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        var otherUserId = Guid.Parse("faceb00c-0000-4000-8000-000000000002");
        db.Entities.Add(Entity(VideoId, EntityKind.Video, "Visible Video", isNsfw: false, now));
        db.EntityPlaybackEvents.AddRange(
            Event(VideoId, PlaybackEventKind.Completed, now.AddHours(-3), 120, TestUserContext.UserId),
            Event(VideoId, PlaybackEventKind.Skipped, now.AddHours(-2), 4, otherUserId),
            Event(VideoId, PlaybackEventKind.Skipped, now.AddHours(-1), 3, userId: null));
        await db.SaveChangesAsync();
        var service = new EfPlaybackStatisticsService(db, TestUserContext.Admin());

        var statistics = await service.GetAsync(
            new PlaybackStatisticsQuery(
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
        Assert.Equal(PlaybackEventKind.Completed, Assert.Single(statistics.RecentEvents).Kind);
        Assert.Equal(1, Assert.Single(statistics.TopEntities).CompletedCount);
        Assert.Equal(1, Assert.Single(statistics.DailyEvents).CompletedCount);

        var selectedUserStatistics = await service.GetAsync(
            new PlaybackStatisticsQuery(
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
            new PlaybackStatisticsQuery(
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
    public async Task StatisticsProjectFamilyRhythmAndWatchTimeInTheRequestedLocalOffset() {
        await using var db = CreateContext();
        var now = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        db.Entities.AddRange(
            Entity(VideoId, EntityKind.Video, "Visible Video", isNsfw: false, now),
            Entity(AudioId, EntityKind.AudioTrack, "Visible Audio", isNsfw: false, now));
        db.EntityPlaybackEvents.AddRange(
            // 2026-06-18T02:00Z is 2026-06-17T21:00 at -05:00, so the local fold must move this
            // event to the previous calendar day and to Wednesday 21:00 rather than Thursday 02:00.
            Event(VideoId, PlaybackEventKind.Completed, DateTimeOffset.Parse("2026-06-18T02:00:00Z"), 600, TestUserContext.UserId, durationSeconds: 900),
            Event(VideoId, PlaybackEventKind.Skipped, DateTimeOffset.Parse("2026-06-18T02:30:00Z"), 5, TestUserContext.UserId, durationSeconds: 900),
            // A position past the reported duration must be clamped to the duration.
            Event(AudioId, PlaybackEventKind.Completed, DateTimeOffset.Parse("2026-06-18T09:00:00Z"), 500, TestUserContext.UserId, durationSeconds: 200));
        await db.SaveChangesAsync();
        var service = new EfPlaybackStatisticsService(db, TestUserContext.Admin());

        var statistics = await service.GetAsync(
            new PlaybackStatisticsQuery(
                now.AddDays(-7),
                now.AddSeconds(1),
                Kind: null,
                EventKind: null,
                HideNsfw: true,
                UtcOffsetMinutes: -300),
            CancellationToken.None);

        Assert.Equal(805, statistics.WatchSeconds);

        var video = Assert.Single(statistics.KindBreakdown, slice => slice.Kind == EntityKind.Video);
        Assert.Equal(2, video.TotalEvents);
        Assert.Equal(1, video.CompletedCount);
        Assert.Equal(1, video.SkippedCount);
        Assert.Equal(1, video.DistinctEntityCount);
        Assert.Equal(605, video.WatchSeconds);
        Assert.Equal(200, Assert.Single(statistics.KindBreakdown, slice => slice.Kind == EntityKind.AudioTrack).WatchSeconds);
        // Ordered by activity, so the two-event video family leads the single-event audio family.
        Assert.Equal(EntityKind.Video, statistics.KindBreakdown[0].Kind);

        Assert.Collection(
            statistics.DailyEvents,
            wednesday => {
                Assert.Equal(new DateOnly(2026, 6, 17), wednesday.Date);
                Assert.Equal(1, wednesday.CompletedCount);
                Assert.Equal(1, wednesday.SkippedCount);
                Assert.Equal(605, wednesday.WatchSeconds);
            },
            thursday => {
                Assert.Equal(new DateOnly(2026, 6, 18), thursday.Date);
                Assert.Equal(1, thursday.CompletedCount);
                Assert.Equal(0, thursday.SkippedCount);
                Assert.Equal(200, thursday.WatchSeconds);
            });

        var evening = Assert.Single(statistics.Rhythm, cell => cell.Hour == 21);
        Assert.Equal((int)DayOfWeek.Wednesday, evening.DayOfWeek);
        Assert.Equal(2, evening.CompletedCount + evening.SkippedCount);
        var morning = Assert.Single(statistics.Rhythm, cell => cell.Hour == 4);
        Assert.Equal((int)DayOfWeek.Thursday, morning.DayOfWeek);
        Assert.Equal(1, morning.CompletedCount);

        var topVideo = Assert.Single(statistics.TopEntities, item => item.Id == VideoId);
        Assert.Equal(605, topVideo.WatchSeconds);
        Assert.Equal(DateTimeOffset.Parse("2026-06-18T02:00:00Z"), topVideo.FirstEventAt);
        Assert.Equal(DateTimeOffset.Parse("2026-06-18T02:30:00Z"), topVideo.LastEventAt);
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
        db.EntityPlaybackEvents.AddRange(
            Event(VideoId, PlaybackEventKind.Completed, now.AddDays(-1), 120, TestUserContext.UserId),
            Event(VideoId, PlaybackEventKind.Skipped, now.AddHours(-3), 4, TestUserContext.UserId),
            Event(AudioId, PlaybackEventKind.Skipped, now.AddHours(-1), 3, TestUserContext.UserId),
            Event(AudiobookTrackId, PlaybackEventKind.Completed, now.AddMinutes(-30), 600, TestUserContext.UserId),
            Event(NsfwId, PlaybackEventKind.Completed, now.AddHours(-2), 300, TestUserContext.UserId),
            Event(AudioId, PlaybackEventKind.Completed, now.AddDays(-30), 90, TestUserContext.UserId));
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

    private static EntityPlaybackEventRow Event(
        Guid entityId,
        PlaybackEventKind kind,
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

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
