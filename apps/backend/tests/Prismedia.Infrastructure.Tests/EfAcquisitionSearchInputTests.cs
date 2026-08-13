using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfAcquisitionSearchInputTests {
    [Fact]
    public async Task CarriesTheLinkedEpisodesAbsolutePositionIntoSearch() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var episodeId = Guid.NewGuid();
        var acquisitionId = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = episodeId,
            KindCode = EntityKind.VideoEpisode.ToCode(),
            Title = "A Visit from Sally Ride",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityPositions.Add(new EntityPositionRow {
            EntityId = episodeId,
            Code = EntityPositionCodes.AbsoluteEpisode,
            Value = 1316,
            UpdatedAt = now
        });
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            EntityId = episodeId,
            Kind = EntityKind.VideoEpisode,
            Status = AcquisitionStatus.Pending,
            Title = "A Visit from Sally Ride",
            SeasonNumber = 11,
            EpisodeNumber = 95,
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var search = await AcquisitionTestFactory.Store(db)
            .GetSearchInputAsync(acquisitionId, CancellationToken.None);

        Assert.Equal(1316, search?.AbsoluteEpisodeNumber);
    }

    [Theory]
    [InlineData(EntityDateType.FirstAir, 2023)]
    [InlineData(EntityDateType.Air, 2024)]
    public async Task UsesCanonicalWorkDatesFromTheDefinitionOwnedAncestor(
        EntityDateType dateType,
        int expectedYear) {
        var actual = await SearchYearAsync(
            EntityKind.VideoSeries,
            [(dateType.ToCode(), expectedYear)],
            nestedEpisode: true);

        Assert.Equal(expectedYear, actual);
    }

    [Fact]
    public async Task ResolvesLegacyAliasesThroughTheDefinitionOwnedDatePriority() {
        var actual = await SearchYearAsync(
            EntityKind.Movie,
            [(EntityDateLegacyCodes.Released, 2012), (EntityDateType.Air.ToCode(), 2016)]);

        Assert.Equal(2012, actual);
    }

    [Fact]
    public async Task UsesTheAcquisitionProfilesOrderedDatePriority() {
        var actual = await SearchYearAsync(
            EntityKind.Movie,
            [(EntityDateType.Release.ToCode(), 2015), (EntityDateType.TheatricalRelease.ToCode(), 2013)]);

        Assert.Equal(2013, actual);
    }

    private static async Task<int?> SearchYearAsync(
        EntityKind workKind,
        IReadOnlyList<(string Code, int Year)> dates,
        bool nestedEpisode = false) {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var workId = Guid.NewGuid();
        var entityId = workId;
        var acquisitionKind = workKind;
        var acquisitionId = Guid.NewGuid();
        db.Entities.Add(Entity(workId, workKind, "Work", null));
        if (nestedEpisode) {
            var seasonId = Guid.NewGuid();
            entityId = Guid.NewGuid();
            acquisitionKind = EntityKind.VideoEpisode;
            db.Entities.AddRange(
                Entity(seasonId, EntityKind.VideoSeason, "Season 1", workId),
                Entity(entityId, acquisitionKind, "Episode 1", seasonId));
        }

        db.EntityDates.AddRange(dates.Select(date => new EntityDateRow {
            EntityId = workId,
            Code = date.Code,
            Value = $"{date.Year}-01-01",
            SortableValue = new DateOnly(date.Year, 1, 1),
            UpdatedAt = now
        }));
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            EntityId = entityId,
            Kind = acquisitionKind,
            Status = AcquisitionStatus.Pending,
            Title = "Work",
            Year = 1999,
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var search = await AcquisitionTestFactory.Store(db)
            .GetSearchInputAsync(acquisitionId, CancellationToken.None);

        return search?.Year;

        EntityRow Entity(Guid id, EntityKind kind, string title, Guid? parentEntityId) => new() {
            Id = id,
            KindCode = kind.ToCode(),
            Title = title,
            ParentEntityId = parentEntityId,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
