using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfEntityReleaseDateStoreTests {
    [Fact]
    public async Task DirectChildCoverageIncludesUndatedEpisodesAndFindsLatestAirDate() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var seasonId = Guid.NewGuid();
        var firstEpisodeId = Guid.NewGuid();
        var secondEpisodeId = Guid.NewGuid();
        var undatedEpisodeId = Guid.NewGuid();
        var unrelatedEpisodeId = Guid.NewGuid();
        db.Entities.AddRange(
            Entity(seasonId, EntityKind.VideoSeason, parentEntityId: null),
            Entity(firstEpisodeId, EntityKind.VideoEpisode, seasonId),
            Entity(secondEpisodeId, EntityKind.VideoEpisode, seasonId),
            Entity(undatedEpisodeId, EntityKind.VideoEpisode, seasonId),
            Entity(unrelatedEpisodeId, EntityKind.VideoEpisode, parentEntityId: null));
        db.EntityDates.AddRange(
            AirDate(firstEpisodeId, new DateOnly(2026, 8, 4)),
            AirDate(secondEpisodeId, new DateOnly(2026, 8, 11)),
            AirDate(unrelatedEpisodeId, new DateOnly(2027, 1, 1)));
        await db.SaveChangesAsync();

        var coverage = await new EfEntityReleaseDateStore(db).GetDirectChildCoverageAsync(
            seasonId,
            EntityKind.VideoEpisode,
            EntityDateType.Air,
            CancellationToken.None);

        Assert.Equal(3, coverage.TotalChildren);
        Assert.Equal(2, coverage.DatedChildren);
        Assert.Equal(new DateOnly(2026, 8, 11), coverage.LatestDate?.SortableValue);

        EntityRow Entity(Guid id, EntityKind kind, Guid? parentEntityId) => new() {
            Id = id,
            KindCode = kind.ToCode(),
            Title = kind.ToCode(),
            ParentEntityId = parentEntityId,
            CreatedAt = now,
            UpdatedAt = now
        };

        EntityDateRow AirDate(Guid entityId, DateOnly date) => new() {
            EntityId = entityId,
            Code = EntityDateType.Air.ToCode(),
            Value = date.ToString("yyyy-MM-dd"),
            SortableValue = date,
            Precision = DatePrecision.Day.ToCode(),
            UpdatedAt = now
        };
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
