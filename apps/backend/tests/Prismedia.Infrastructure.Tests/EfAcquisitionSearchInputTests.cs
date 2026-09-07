using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfAcquisitionSearchInputTests {
    [Fact]
    public async Task EpisodeSearchUsesTheSameCurrentSeriesCatalogAsImport() {
        await using var db = CreateContext();
        var series = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var target = Guid.NewGuid(); var other = Guid.NewGuid(); var acquisition = Guid.NewGuid();
        foreach (var item in new[] {
            (series, EntityKind.VideoSeries, (Guid?)null, 0, "Example Show"),
            (first, EntityKind.VideoSeason, (Guid?)series, 1, "Season 1"),
            (second, EntityKind.VideoSeason, (Guid?)series, 2, "Season 2"),
            (other, EntityKind.VideoEpisode, (Guid?)first, 70, "Going West"),
            (target, EntityKind.VideoEpisode, (Guid?)second, 17, "A Bug Adventure")
        }) db.Entities.Add(new EntityRow { Id = item.Item1, KindCode = item.Item2.ToCode(), ParentEntityId = item.Item3,
            SortOrder = item.Item4, Title = item.Item5, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        db.Acquisitions.Add(new AcquisitionRow { Id = acquisition, EntityId = target, Kind = EntityKind.VideoEpisode,
            Status = AcquisitionStatus.Searching, Title = "A Bug Adventure", Series = "Example Show", SeasonNumber = 2,
            EpisodeNumber = 17, ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var input = await AcquisitionTestFactory.Store(db).GetSearchInputAsync(acquisition, default);

        Assert.Equal(2, input!.EpisodeCatalog.Count);
        Assert.Equal(other, Assert.Single(input.EpisodeCatalog.Single(season => season.SeasonNumber == 1).Episodes).EntityId);
        (await db.Entities.SingleAsync(row => row.Id == other)).Title = "Repaired title";
        await db.SaveChangesAsync();
        var refreshed = await AcquisitionTestFactory.Store(db).GetSearchInputAsync(acquisition, default);
        Assert.Equal("Repaired title", Assert.Single(refreshed!.EpisodeCatalog.Single(season => season.SeasonNumber == 1).Episodes).Title);
    }

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
