using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfReleaseCalendarServiceTests {
    [Fact]
    public async Task ListsReleaseMilestonesAndMarksTheProfileGate() {
        await using var db = CreateContext();
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var entityId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var acquisitionId = Guid.NewGuid();
        var monitorId = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = EntityKind.Movie.ToCode(),
            Title = "Toy Story 5",
            IsWanted = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow {
            Id = profileId,
            Kind = EntityKind.Movie,
            DisplayName = "Wait for digital",
            TargetLibraryRootId = Guid.NewGuid(),
            PathTemplate = MediaNamingTemplates.MovieDefault,
            SearchAfterDateType = EntityDateType.DigitalRelease,
            SearchDelayDays = 2,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            EntityId = entityId,
            ProfileId = profileId,
            Kind = EntityKind.Movie,
            Status = AcquisitionStatus.WaitingForRelease,
            Title = "Toy Story 5",
            PosterUrl = "https://images.test/toy-story-5.jpg",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            EntityId = entityId,
            AcquisitionId = acquisitionId,
            Kind = EntityKind.Movie,
            Status = MonitorStatus.Active,
            Title = "Toy Story 5",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityDates.AddRange(
            Date(entityId, EntityDateType.TheatricalRelease, "2026-06-19", new DateOnly(2026, 6, 19), now),
            Date(entityId, EntityDateType.DigitalRelease, "2026-08-14", new DateOnly(2026, 8, 14), now),
            Date(entityId, EntityDateType.Birth, "1995-01-01", new DateOnly(1995, 1, 1), now));
        await db.SaveChangesAsync();

        var service = new EfReleaseCalendarService(db, new FixedTimeProvider(now));
        var events = await service.ListAsync(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Equal(2, events.Count);
        Assert.Contains(events, item => item.DateType == EntityDateType.TheatricalRelease && !item.IsSearchGate);
        var gate = Assert.Single(events, item => item.IsSearchGate);
        Assert.Equal(EntityDateType.DigitalRelease, gate.DateType);
        Assert.Equal(new DateOnly(2026, 8, 16), gate.SearchNotBefore);
        Assert.False(gate.IsSearchEligible);
        Assert.Equal(AcquisitionStatus.WaitingForRelease, gate.AcquisitionStatus);
    }

    [Fact]
    public async Task IncludesStructuralParentContextForNestedCalendarEntries() {
        await using var db = CreateContext();
        var now = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        var seriesId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var monitorId = Guid.NewGuid();
        db.Entities.AddRange(
            new EntityRow {
                Id = seriesId,
                KindCode = EntityKind.VideoSeries.ToCode(),
                Title = "It's Always Sunny in Philadelphia",
                CreatedAt = now,
                UpdatedAt = now
            },
            new EntityRow {
                Id = seasonId,
                KindCode = EntityKind.VideoSeason.ToCode(),
                Title = "Season 15",
                ParentEntityId = seriesId,
                IsWanted = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            EntityId = seasonId,
            Kind = EntityKind.VideoSeason,
            Status = MonitorStatus.Active,
            Title = "Season 15",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityDates.Add(Date(
            seasonId,
            EntityDateType.Air,
            "2026-07-20",
            new DateOnly(2026, 7, 20),
            now));
        await db.SaveChangesAsync();

        var service = new EfReleaseCalendarService(db, new FixedTimeProvider(now));
        var calendarEvent = Assert.Single(await service.ListAsync(
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31),
            hideNsfw: false,
            CancellationToken.None));

        Assert.Equal(seriesId, calendarEvent.ParentEntityId);
        Assert.Equal(EntityKind.VideoSeries, calendarEvent.ParentKind);
        Assert.Equal("It's Always Sunny in Philadelphia", calendarEvent.ParentTitle);
    }

    private static EntityDateRow Date(
        Guid entityId,
        EntityDateType type,
        string value,
        DateOnly sortable,
        DateTimeOffset now) => new() {
            EntityId = entityId,
            Code = type.ToCode(),
            Value = value,
            SortableValue = sortable,
            Precision = DatePrecision.Day.ToCode(),
            UpdatedAt = now
        };

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
