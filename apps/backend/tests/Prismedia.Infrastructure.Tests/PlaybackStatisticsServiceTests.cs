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
            Event(VideoId, PlaybackEventKind.Completed, now.AddDays(-1), positionSeconds: 120),
            Event(VideoId, PlaybackEventKind.Skipped, now.AddHours(-3), positionSeconds: 4),
            Event(AudioId, PlaybackEventKind.Skipped, now.AddHours(-1), positionSeconds: 3),
            Event(AudiobookTrackId, PlaybackEventKind.Completed, now.AddMinutes(-30), positionSeconds: 600),
            Event(NsfwId, PlaybackEventKind.Completed, now.AddHours(-2), positionSeconds: 300),
            Event(AudioId, PlaybackEventKind.Completed, now.AddDays(-30), positionSeconds: 90));
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
        double? positionSeconds) =>
        new() {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Kind = kind,
            OccurredAt = occurredAt,
            PositionSeconds = positionSeconds,
            DurationSeconds = null,
            CreatedAt = occurredAt
        };

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
