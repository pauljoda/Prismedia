using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class ReviewedFulfillmentOwnershipReaderTests {
    [Fact]
    public async Task ActiveManagerRequestProjectsItsCanonicalMovieOwnerAndIgnoresReleasedHistory() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var movie = AddEntity(db, EntityKind.Movie, "Requested movie");
        AddIdentity(db, movie.Id, ExternalIdProviders.Tmdb, "123");
        var currentConnection = AddConnection(db, "Current Radarr");
        var releasedConnection = AddConnection(db, "Previous Radarr");
        var currentRequest = AddRequest(db, currentConnection.Id, movie.Id, ManagedRequestPhase.AwaitingFiles);
        var releasedRequest = AddRequest(db, releasedConnection.Id, movie.Id, ManagedRequestPhase.OwnershipReleased);
        var now = DateTimeOffset.UtcNow;
        var releasedReservation = Reservation(releasedRequest.Id, releasedConnection.Id, movie.Id);
        releasedReservation.ReleasedAt = now;
        db.FulfillmentReservations.AddRange(
            Reservation(currentRequest.Id, currentConnection.Id, movie.Id),
            releasedReservation);
        await db.SaveChangesAsync();

        var result = await Reader(db).ListAsync(
            new(EntityKind.Movie, new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "123" }),
            default);

        var ownership = Assert.Single(result);
        Assert.Equal(movie.Id, ownership.EntityId);
        Assert.Null(ownership.TargetEntityIds);
        Assert.Equal(FulfillmentOwnerKind.ExternalManager, ownership.OwnerKind);
        Assert.Equal(currentConnection.Id, ownership.ConnectionId);
        Assert.Equal(currentConnection.Name, ownership.ConnectionName);
        Assert.Equal(currentRequest.Id, ownership.RequestId);
        Assert.Equal(ManagedRequestPhase.AwaitingFiles, ownership.RequestPhase);
        Assert.False(ownership.HasLocalSource);
    }

    [Fact]
    public async Task FiniteSeriesProjectsOnlyOwnedSelectedEpisodesAndLeavesDisjointEpisodesRequestable() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var series = AddEntity(db, EntityKind.VideoSeries, "Series");
        var season = AddEntity(db, EntityKind.VideoSeason, "Season 1", series.Id);
        var first = AddEntity(db, EntityKind.VideoEpisode, "First", season.Id);
        var second = AddEntity(db, EntityKind.VideoEpisode, "Second", season.Id);
        AddIdentity(db, series.Id, ExternalIdProviders.Tvdb, "42");
        AddIdentity(db, first.Id, ExternalIdProviders.Tvdb, "101");
        AddIdentity(db, second.Id, ExternalIdProviders.Tvdb, "102");
        AddPosition(db, season.Id, EntityPositionCodes.Season, 1);
        AddPosition(db, first.Id, EntityPositionCodes.Episode, 1);
        AddPosition(db, second.Id, EntityPositionCodes.Episode, 2);
        var connection = AddConnection(db, "Sonarr");
        var request = AddRequest(db, connection.Id, series.Id, ManagedRequestPhase.Rejected);
        db.FulfillmentReservations.Add(Reservation(request.Id, connection.Id, first.Id));
        await db.SaveChangesAsync();
        var reader = Reader(db);

        var disjoint = await reader.ListAsync(SeriesWork("42", ("102", 2)), default);
        var overlapping = await reader.ListAsync(SeriesWork("42", ("101", 1), ("102", 2)), default);

        Assert.Empty(disjoint);
        var ownership = Assert.Single(overlapping);
        Assert.Equal(series.Id, ownership.EntityId);
        Assert.Equal([first.Id], ownership.TargetEntityIds);
        Assert.Equal(ManagedRequestPhase.Rejected, ownership.RequestPhase);

        db.FulfillmentReservations.Add(Reservation(request.Id, connection.Id, second.Id));
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = first.Id,
            Role = EntityFileRole.Source,
            Path = "/media/series/first.mkv",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var partiallyAvailable = Assert.Single(await reader.ListAsync(SeriesWork("42", ("101", 1), ("102", 2)), default));
        Assert.Equal(new[] { first.Id, second.Id }.Order(), partiallyAvailable.TargetEntityIds!.Order());
        Assert.False(partiallyAvailable.HasLocalSource);
    }

    [Fact]
    public async Task NativeSeriesMonitoringOwnsOnlyReviewedDescendantEpisodes() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var series = AddEntity(db, EntityKind.VideoSeries, "Monitored series");
        var season = AddEntity(db, EntityKind.VideoSeason, "Season 1", series.Id);
        var episode = AddEntity(db, EntityKind.VideoEpisode, "Episode", season.Id);
        AddIdentity(db, series.Id, ExternalIdProviders.Tvdb, "42");
        AddIdentity(db, episode.Id, ExternalIdProviders.Tvdb, "101");
        AddPosition(db, season.Id, EntityPositionCodes.Season, 1);
        AddPosition(db, episode.Id, EntityPositionCodes.Episode, 1);
        db.Monitors.Add(new() {
            Id = Guid.NewGuid(), EntityId = series.Id, Kind = EntityKind.VideoSeries, Status = MonitorStatus.Active
        });
        await db.SaveChangesAsync();

        var ownership = Assert.Single(await Reader(db).ListAsync(SeriesWork("42", ("101", 1)), default));

        Assert.Equal(series.Id, ownership.EntityId);
        Assert.Equal([episode.Id], ownership.TargetEntityIds);
        Assert.Null(ownership.OwnerKind);
        Assert.Null(ownership.ConnectionId);
        Assert.Null(ownership.RequestId);
        Assert.False(ownership.HasLocalSource);
    }

    [Fact]
    public async Task AmbiguousCanonicalIdentityDoesNotChooseAnArbitraryLibraryEntity() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var first = AddEntity(db, EntityKind.Movie, "First duplicate");
        var second = AddEntity(db, EntityKind.Movie, "Second duplicate");
        AddIdentity(db, first.Id, ExternalIdProviders.Tmdb, "55");
        AddIdentity(db, second.Id, ExternalIdProviders.Tmdb, "55");
        var connection = AddConnection(db, "Radarr");
        var request = AddRequest(db, connection.Id, first.Id, ManagedRequestPhase.PendingCreation);
        db.FulfillmentReservations.Add(Reservation(request.Id, connection.Id, first.Id));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<Prismedia.Application.Entities.ExternalIdentityAmbiguityException>(() => Reader(db).ListAsync(
            new(EntityKind.Movie, new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "55" }),
            default));
    }

    [Fact]
    public async Task CoordinateOnlySeriesReviewFindsOwnedEpisodeWithinExactParentAndIgnoresOptionalAbsoluteNumber() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var series = AddEntity(db, EntityKind.VideoSeries, "Owned series");
        var season = AddEntity(db, EntityKind.VideoSeason, "Season 1", series.Id);
        var episode = AddEntity(db, EntityKind.VideoEpisode, "Episode 3", season.Id);
        AddIdentity(db, series.Id, ExternalIdProviders.Tvdb, "42");
        AddPosition(db, season.Id, EntityPositionCodes.Season, 1);
        AddPosition(db, episode.Id, EntityPositionCodes.Episode, 3);
        AddPosition(db, episode.Id, EntityPositionCodes.AbsoluteEpisode, 3);

        var otherSeries = AddEntity(db, EntityKind.VideoSeries, "Other series");
        var otherSeason = AddEntity(db, EntityKind.VideoSeason, "Season 1", otherSeries.Id);
        var otherEpisode = AddEntity(db, EntityKind.VideoEpisode, "Other episode 3", otherSeason.Id);
        AddIdentity(db, otherSeries.Id, ExternalIdProviders.Tvdb, "99");
        AddPosition(db, otherSeason.Id, EntityPositionCodes.Season, 1);
        AddPosition(db, otherEpisode.Id, EntityPositionCodes.Episode, 3);

        var connection = AddConnection(db, "Sonarr");
        var request = AddRequest(db, connection.Id, series.Id, ManagedRequestPhase.Completed);
        var unrelatedRequest = AddRequest(db, connection.Id, otherSeries.Id, ManagedRequestPhase.Completed);
        db.FulfillmentReservations.AddRange(
            Reservation(request.Id, connection.Id, episode.Id),
            Reservation(unrelatedRequest.Id, connection.Id, otherEpisode.Id));
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(), EntityId = episode.Id, Role = EntityFileRole.Source,
            Path = "/media/series/episode-3.mkv", CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var ownership = Assert.Single(await Reader(db).ListAsync(new(
            EntityKind.VideoSeries,
            new Dictionary<string, string> { [ExternalIdProviders.Tvdb] = "42" },
            [new(EntityKind.VideoEpisode, new Dictionary<string, string>(),
                SeasonNumber: 1, EpisodeNumber: 3, AbsoluteNumber: null)]), default));

        Assert.Equal(series.Id, ownership.EntityId);
        Assert.Equal([episode.Id], ownership.TargetEntityIds);
        Assert.Equal(request.Id, ownership.RequestId);
        Assert.True(ownership.HasLocalSource);
    }

    [Fact]
    public async Task DuplicateCoordinatesWithinTheReviewedSeriesRemainAmbiguous() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var series = AddEntity(db, EntityKind.VideoSeries, "Ambiguous series");
        var season = AddEntity(db, EntityKind.VideoSeason, "Season 1", series.Id);
        AddIdentity(db, series.Id, ExternalIdProviders.Tvdb, "42");
        AddPosition(db, season.Id, EntityPositionCodes.Season, 1);
        foreach (var title in new[] { "First episode 3", "Second episode 3" }) {
            var episode = AddEntity(db, EntityKind.VideoEpisode, title, season.Id);
            AddPosition(db, episode.Id, EntityPositionCodes.Episode, 3);
        }
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<Prismedia.Application.Entities.ExternalIdentityAmbiguityException>(() =>
            Reader(db).ListAsync(new(
                EntityKind.VideoSeries,
                new Dictionary<string, string> { [ExternalIdProviders.Tvdb] = "42" },
                [new(EntityKind.VideoEpisode, new Dictionary<string, string>(), 1, 3)]), default));
    }

    private static ManagedLookupInput SeriesWork(string seriesId, params (string Id, int Episode)[] episodes) =>
        new(EntityKind.VideoSeries,
            new Dictionary<string, string> {
                [ExternalIdProviders.Tvdb] = seriesId,
                [ExternalIdProviders.Tmdb] = "999"
            },
            episodes.Select(episode => new ManagedLookupTarget(
                EntityKind.VideoEpisode,
                new Dictionary<string, string> { [ExternalIdProviders.Tvdb] = episode.Id },
                SeasonNumber: 1,
                EpisodeNumber: episode.Episode)).ToArray());

    private static EntityRow AddEntity(
        PrismediaDbContext db,
        EntityKind kind,
        string title,
        Guid? parentId = null) {
        var entity = new EntityRow {
            Id = Guid.NewGuid(),
            KindCode = kind.ToCode(),
            Title = title,
            ParentEntityId = parentId
        };
        db.Entities.Add(entity);
        return entity;
    }

    private static void AddIdentity(
        PrismediaDbContext db,
        Guid entityId,
        string provider,
        string value) =>
        db.EntityExternalIds.Add(new() {
            Id = Guid.NewGuid(), EntityId = entityId, Provider = provider, Value = value
        });

    private static void AddPosition(
        PrismediaDbContext db,
        Guid entityId,
        string code,
        int value) => db.EntityPositions.Add(new() {
            EntityId = entityId, Code = code, Value = value
        });

    private static IntegrationConnectionRow AddConnection(PrismediaDbContext db, string name) {
        var connection = new IntegrationConnectionRow {
            Id = Guid.NewGuid(),
            PluginId = "fixture-manager",
            Name = name,
            BaseUrl = "https://manager.test/",
            Enabled = true,
            Revision = 1,
            Status = ConnectionStatus.Ready
        };
        db.IntegrationConnections.Add(connection);
        return connection;
    }

    private static ManagedRequestRow AddRequest(
        PrismediaDbContext db,
        Guid connectionId,
        Guid entityId,
        ManagedRequestPhase phase) {
        var root = new LibraryRootRow {
            Id = Guid.NewGuid(),
            Path = $"/media/{Guid.NewGuid():N}",
            Label = "Fixture library",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.LibraryRoots.Add(root);
        var request = new ManagedRequestRow {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            EntityId = entityId,
            LibraryRootId = root.Id,
            Revision = 1,
            Phase = phase,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.ManagedRequests.Add(request);
        return request;
    }

    private static FulfillmentReservationRow Reservation(Guid ownerId, Guid connectionId, Guid entityId) =>
        new() {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            OwnerKind = FulfillmentOwnerKind.ExternalManager,
            ConnectionId = connectionId,
            EntityId = entityId,
            ExternalIdsJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static EfReviewedFulfillmentOwnershipReader Reader(PrismediaDbContext db) =>
        new(db, new EfEntityExternalIdentityStore(db, TimeProvider.System));
}
