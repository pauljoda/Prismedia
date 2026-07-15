using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Guards the user-scoped TV progress roll-up from episode playback into its season and series.
/// Container cursors are independent: advancing the series must never imply progress in an
/// earlier or later season that the user has not watched.
/// </summary>
public sealed class VideoContainerProgressTests {
    private static readonly Guid SeriesId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SeasonOneId = Guid.Parse("10000000-0000-0000-0000-000000000011");
    private static readonly Guid SeasonTwoId = Guid.Parse("10000000-0000-0000-0000-000000000012");
    private static readonly Guid SeasonOneEpisodeOneId = Guid.Parse("10000000-0000-0000-0000-000000000111");
    private static readonly Guid SeasonOneEpisodeTwoId = Guid.Parse("10000000-0000-0000-0000-000000000112");
    private static readonly Guid SeasonTwoEpisodeOneId = Guid.Parse("10000000-0000-0000-0000-000000000121");
    private static readonly Guid SeasonTwoEpisodeTwoId = Guid.Parse("10000000-0000-0000-0000-000000000122");

    [Fact]
    public async Task PartialEpisodeInSecondSeasonAdvancesSeriesAndOnlyThatSeason() {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Capabilities.UpdatePlaybackAsync(
            SeasonTwoEpisodeOneId,
            resumeSeconds: 50,
            durationSeconds: null,
            completed: null,
            CancellationToken.None);

        var seriesProgress = await fixture.ProgressAsync(SeriesId);
        var seasonTwoProgress = await fixture.ProgressAsync(SeasonTwoId);

        Assert.Equal(SeasonTwoEpisodeOneId, seriesProgress!.CurrentEntityId);
        Assert.Equal(2, seriesProgress.Index);
        Assert.Equal(4, seriesProgress.Total);
        Assert.Null(seriesProgress.CompletedAt);
        Assert.Equal(SeasonTwoEpisodeOneId, seasonTwoProgress!.CurrentEntityId);
        Assert.Equal(0, seasonTwoProgress.Index);
        Assert.Equal(2, seasonTwoProgress.Total);
        Assert.Null(await fixture.ProgressAsync(SeasonOneId));
    }

    [Fact]
    public async Task CompletedEpisodePointsSeasonAndSeriesAtNextUnstartedEpisode() {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Capabilities.UpdatePlaybackAsync(
            SeasonOneEpisodeOneId,
            resumeSeconds: 95,
            durationSeconds: null,
            completed: null,
            CancellationToken.None);

        var seriesProgress = await fixture.ProgressAsync(SeriesId);
        var seasonProgress = await fixture.ProgressAsync(SeasonOneId);

        Assert.Equal(SeasonOneEpisodeTwoId, seriesProgress!.CurrentEntityId);
        Assert.Equal(1, seriesProgress.Index);
        Assert.Null(seriesProgress.CompletedAt);
        Assert.Equal(SeasonOneEpisodeTwoId, seasonProgress!.CurrentEntityId);
        Assert.Equal(1, seasonProgress.Index);
        Assert.Null(seasonProgress.CompletedAt);
    }

    [Fact]
    public async Task SeasonBoundaryCompletesCurrentSeasonAndContinuesSeriesIntoNextSeason() {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Capabilities.UpdatePlaybackAsync(
            SeasonOneEpisodeTwoId,
            resumeSeconds: 95,
            durationSeconds: null,
            completed: null,
            CancellationToken.None);

        var seriesProgress = await fixture.ProgressAsync(SeriesId);
        var seasonOneProgress = await fixture.ProgressAsync(SeasonOneId);

        Assert.Equal(SeasonTwoEpisodeOneId, seriesProgress!.CurrentEntityId);
        Assert.Equal(2, seriesProgress.Index);
        Assert.Null(seriesProgress.CompletedAt);
        Assert.Equal(SeasonOneEpisodeTwoId, seasonOneProgress!.CurrentEntityId);
        Assert.Equal(1, seasonOneProgress.Index);
        Assert.NotNull(seasonOneProgress.CompletedAt);
        Assert.Null(await fixture.ProgressAsync(SeasonTwoId));
    }

    [Fact]
    public async Task EarlierSeasonPlaybackDoesNotMoveSeriesBackwardAfterSecondSeasonStarted() {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Capabilities.UpdatePlaybackAsync(
            SeasonTwoEpisodeOneId,
            resumeSeconds: 50,
            durationSeconds: null,
            completed: null,
            CancellationToken.None);

        await fixture.Capabilities.UpdatePlaybackAsync(
            SeasonOneEpisodeOneId,
            resumeSeconds: 50,
            durationSeconds: null,
            completed: null,
            CancellationToken.None);

        var seriesProgress = await fixture.ProgressAsync(SeriesId);
        var seasonOneProgress = await fixture.ProgressAsync(SeasonOneId);

        Assert.Equal(SeasonTwoEpisodeOneId, seriesProgress!.CurrentEntityId);
        Assert.Equal(2, seriesProgress.Index);
        Assert.Equal(SeasonOneEpisodeOneId, seasonOneProgress!.CurrentEntityId);
        Assert.Equal(0, seasonOneProgress.Index);
    }

    [Fact]
    public async Task FinalEpisodeCompletesBothSeasonAndSeries() {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Capabilities.UpdatePlaybackAsync(
            SeasonTwoEpisodeTwoId,
            resumeSeconds: 95,
            durationSeconds: null,
            completed: null,
            CancellationToken.None);

        var seriesProgress = await fixture.ProgressAsync(SeriesId);
        var seasonProgress = await fixture.ProgressAsync(SeasonTwoId);

        Assert.Equal(SeasonTwoEpisodeTwoId, seriesProgress!.CurrentEntityId);
        Assert.Equal(3, seriesProgress.Index);
        Assert.NotNull(seriesProgress.CompletedAt);
        Assert.Equal(SeasonTwoEpisodeTwoId, seasonProgress!.CurrentEntityId);
        Assert.Equal(1, seasonProgress.Index);
        Assert.NotNull(seasonProgress.CompletedAt);
    }

    private sealed class Fixture : IAsyncDisposable {
        private Fixture(
            PrismediaDbContext db,
            EfEntityRepository repository,
            EntityCapabilityService capabilities) {
            Db = db;
            Repository = repository;
            Capabilities = capabilities;
        }

        private PrismediaDbContext Db { get; }
        private EfEntityRepository Repository { get; }
        public EntityCapabilityService Capabilities { get; }

        public static async Task<Fixture> CreateAsync() {
            var db = new PrismediaDbContext(new DbContextOptionsBuilder<PrismediaDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
            var now = DateTimeOffset.Parse("2026-07-15T12:00:00Z");
            db.Entities.AddRange(
                Row(SeriesId, EntityKind.VideoSeries, "Series", now),
                Row(SeasonOneId, EntityKind.VideoSeason, "Season 1", now, SeriesId, 1),
                Row(SeasonTwoId, EntityKind.VideoSeason, "Season 2", now, SeriesId, 2),
                Row(SeasonOneEpisodeOneId, EntityKind.Video, "S01E01", now, SeasonOneId, 1),
                Row(SeasonOneEpisodeTwoId, EntityKind.Video, "S01E02", now, SeasonOneId, 2),
                Row(SeasonTwoEpisodeOneId, EntityKind.Video, "S02E01", now, SeasonTwoId, 1),
                Row(SeasonTwoEpisodeTwoId, EntityKind.Video, "S02E02", now, SeasonTwoId, 2));
            db.EntityTechnical.AddRange(
                Technical(SeasonOneEpisodeOneId, now),
                Technical(SeasonOneEpisodeTwoId, now),
                Technical(SeasonTwoEpisodeOneId, now),
                Technical(SeasonTwoEpisodeTwoId, now));
            await db.SaveChangesAsync();

            var user = TestUserContext.Admin();
            var repository = new EfEntityRepository(
                db,
                user,
                EntityMappers.Kinds(db),
                EntityMappers.Capabilities(db, user));
            var capabilities = new EntityCapabilityService(
                repository,
                new EfEntitySourceOwnershipProjection(db),
                timeProvider: new FixedTimeProvider(now));
            return new Fixture(db, repository, capabilities);
        }

        public async Task<CapabilityProgress?> ProgressAsync(Guid entityId) =>
            (await Repository.FindShallowAsync(entityId, CancellationToken.None))?.Progress;

        public ValueTask DisposeAsync() => Db.DisposeAsync();

        private static EntityRow Row(
            Guid id,
            EntityKind kind,
            string title,
            DateTimeOffset now,
            Guid? parentId = null,
            int? sortOrder = null) =>
            new() {
                Id = id,
                KindCode = EntityKindRegistry.ToCode(kind),
                Title = title,
                ParentEntityId = parentId,
                SortOrder = sortOrder,
                CreatedAt = now,
                UpdatedAt = now
            };

        private static EntityTechnicalRow Technical(Guid entityId, DateTimeOffset now) =>
            new() {
                EntityId = entityId,
                DurationSeconds = 100,
                UpdatedAt = now
            };
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
