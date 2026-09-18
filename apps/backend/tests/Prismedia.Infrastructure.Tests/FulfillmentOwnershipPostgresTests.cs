using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class FulfillmentOwnershipPostgresTests {
    [Fact]
    public async Task NativeCreationBulkRetryAndUpgradeChildrenRespectTheSameOwner() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (connection, movie) = await SeedAsync(db);
        var cancelled = Acquisition(movie, AcquisitionStatus.Cancelled);
        var imported = Acquisition(movie, AcquisitionStatus.Imported);
        db.Acquisitions.AddRange(cancelled, imported); await db.SaveChangesAsync();
        await ReserveAsync(db, connection, movie);
        db.Acquisitions.Add(Acquisition(movie));
        await ConflictAsync(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        await ConflictAsync(() => db.Acquisitions.Where(row => row.Id == cancelled.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, AcquisitionStatus.Pending)));
        var child = Acquisition(null); child.UpgradeOfAcquisitionId = imported.Id;
        db.Acquisitions.Add(child);
        await ConflictAsync(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Acquisitions.CountAsync());
        Assert.Equal(AcquisitionStatus.Cancelled, (await db.Acquisitions.SingleAsync(row => row.Id == cancelled.Id)).Status);
    }

    [Fact]
    public async Task FailedNativeIntentAndPausedMonitorStillReserveTheScope() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (connection, movie) = await SeedAsync(db);
        db.Acquisitions.Add(Acquisition(movie, AcquisitionStatus.Failed)); await db.SaveChangesAsync();
        await ConflictAsync(() => ReserveAsync(db, connection, movie)); db.ChangeTracker.Clear();
        await db.Acquisitions.ExecuteDeleteAsync();
        db.Monitors.Add(new() { Id = Guid.NewGuid(), EntityId = movie, Kind = EntityKind.Movie, Status = MonitorStatus.Paused });
        await db.SaveChangesAsync();
        await ConflictAsync(() => ReserveAsync(db, connection, movie)); db.ChangeTracker.Clear();
        await db.Monitors.ExecuteDeleteAsync();
        await ReserveAsync(db, connection, movie);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentNativeAndExternalAcceptanceHaveExactlyOneWinner(bool externalFirst) {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var first = database.CreateContext();
        var (connection, movie) = await SeedAsync(first);
        await using var transaction = await first.Database.BeginTransactionAsync();
        if (externalFirst) await new EfFulfillmentReservationStore(first).ReserveAsync(Guid.NewGuid(), FulfillmentOwnerKind.ExternalManager, connection, movie, null, default);
        else { first.Acquisitions.Add(Acquisition(movie)); await first.SaveChangesAsync(); }
        await using var second = database.CreateContext();
        Task losing;
        if (externalFirst) { second.Acquisitions.Add(Acquisition(movie)); losing = second.SaveChangesAsync(); }
        else losing = ReserveAsync(second, connection, movie);
        await transaction.CommitAsync();
        await ConflictAsync(() => losing);
        Assert.Equal(externalFirst ? 1 : 0, await first.FulfillmentReservations.CountAsync());
        Assert.Equal(externalFirst ? 0 : 1, await first.Acquisitions.CountAsync());
    }

    [Fact]
    public async Task ExternalAcceptanceWaitsForNativeLifecycleBeforeTakingOwnershipLock() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var native = database.CreateContext();
        var (connection, movie) = await SeedAsync(native);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new EfEntityLifecycleMutationLease(native, new EfEntityHierarchyReader(native));
        var winner = lease.ExecuteAsync(movie, async token => {
            entered.SetResult(); await proceed.Task;
            native.Acquisitions.Add(Acquisition(movie)); await native.SaveChangesAsync(token);
        }, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var external = database.CreateContext();
        await external.Database.OpenConnectionAsync();
        var pid = await external.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
        var loser = ReserveAsync(external, connection, movie);
        try {
            await using var observer = database.CreateContext();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await observer.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = {pid} AND wait_event_type = 'Lock') AS \"Value\"")
                .SingleAsync(deadline.Token)) await Task.Delay(10, deadline.Token);
        } finally { proceed.TrySetResult(); }
        Assert.True(await winner.WaitAsync(TimeSpan.FromSeconds(10)));
        await ConflictAsync(() => loser.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Single(await native.Acquisitions.ToArrayAsync());
        Assert.Empty(await native.FulfillmentReservations.ToArrayAsync());
    }

    [Fact]
    public async Task AncestorScopesConflictButIndependentEpisodesAndBookRenditionsDoNot() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (connection, _) = await SeedAsync(db);
        var series = Entity(EntityKind.VideoSeries);
        var season = Entity(EntityKind.VideoSeason, series.Id);
        var first = Entity(EntityKind.VideoEpisode, season.Id); var second = Entity(EntityKind.VideoEpisode, season.Id);
        var book = Entity(EntityKind.Book);
        db.Entities.AddRange(series, season, first, second, book); await db.SaveChangesAsync();
        await ReserveAsync(db, connection, first.Id);
        db.Acquisitions.Add(Acquisition(second.Id, kind: EntityKind.VideoEpisode)); await db.SaveChangesAsync();
        db.Acquisitions.Add(Acquisition(season.Id, kind: EntityKind.VideoSeason));
        await ConflictAsync(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        db.Monitors.Add(new() { Id = Guid.NewGuid(), EntityId = series.Id, Kind = EntityKind.VideoSeries, Status = MonitorStatus.Active });
        await ConflictAsync(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        await ReserveAsync(db, connection, book.Id, BookRendition.Ebook);
        var audio = Acquisition(book.Id, kind: EntityKind.Book); audio.BookRendition = BookRendition.Audiobook;
        db.Acquisitions.Add(audio); await db.SaveChangesAsync();
        var legacyEbook = Acquisition(book.Id, kind: EntityKind.Book);
        db.Acquisitions.Add(legacyEbook);
        await ConflictAsync(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateMovieIdentityAndUnboundProviderRequestCannotBypassReservation() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (connection, movie) = await SeedAsync(db);
        var duplicate = Entity(EntityKind.Movie); db.Entities.Add(duplicate);
        db.EntityExternalIds.AddRange(Identity(movie, ExternalIdProviders.Tmdb, "123"), Identity(duplicate.Id, ExternalIdProviders.Tmdb, "123"));
        await db.SaveChangesAsync(); await ReserveAsync(db, connection, movie);
        db.Acquisitions.Add(Acquisition(duplicate.Id));
        await ConflictAsync(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        // The accepted identity remains protected after a later metadata edit removes its current alias.
        await db.EntityExternalIds.Where(row => row.EntityId == movie).ExecuteDeleteAsync();
        var unbound = Acquisition(null); unbound.IdentityNamespace = ExternalIdProviders.Tmdb; unbound.IdentityValue = "123";
        db.Acquisitions.Add(unbound);
        await ConflictAsync(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        await ConflictAsync(() => ReserveAsync(db, connection, duplicate.Id));
    }

    [Fact]
    public async Task LegacyUnboundMonitorAndUpgradeInheritTheirAcquisitionsProviderIdentity() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (connection, movie) = await SeedAsync(db);
        db.EntityExternalIds.Add(Identity(movie, ExternalIdProviders.Tmdb, "123"));
        var imported = Acquisition(null, AcquisitionStatus.Imported);
        imported.IdentityNamespace = ExternalIdProviders.Tmdb; imported.IdentityValue = "123";
        db.Acquisitions.Add(imported); await db.SaveChangesAsync();
        var monitor = new MonitorRow { Id = Guid.NewGuid(), AcquisitionId = imported.Id, Kind = EntityKind.Movie, Status = MonitorStatus.Paused };
        db.Monitors.Add(monitor); await db.SaveChangesAsync();
        await ConflictAsync(() => ReserveAsync(db, connection, movie)); db.ChangeTracker.Clear();
        await db.Monitors.ExecuteDeleteAsync(); await ReserveAsync(db, connection, movie);
        db.Monitors.Add(monitor);
        await ConflictAsync(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        var upgrade = Acquisition(null); upgrade.UpgradeOfAcquisitionId = imported.Id;
        db.Acquisitions.Add(upgrade);
        await ConflictAsync(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task EquivalentEpisodeCoordinatesAcrossDuplicateSeriesStillConflict() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (connection, _) = await SeedAsync(db);
        var seriesA = Entity(EntityKind.VideoSeries); var seriesB = Entity(EntityKind.VideoSeries);
        var seasonA = Entity(EntityKind.VideoSeason, seriesA.Id); var seasonB = Entity(EntityKind.VideoSeason, seriesB.Id);
        var episodeA = Entity(EntityKind.VideoEpisode, seasonA.Id); var episodeB = Entity(EntityKind.VideoEpisode, seasonB.Id);
        db.Entities.AddRange(seriesA, seriesB, seasonA, seasonB, episodeA, episodeB);
        db.EntityExternalIds.AddRange(Identity(seriesA.Id, ExternalIdProviders.Tvdb, "42"), Identity(seriesB.Id, ExternalIdProviders.Tvdb, "42"));
        db.EntityPositions.AddRange(new() { EntityId = seasonA.Id, Code = EntityPositionCodes.Season, Value = 2 },
            new() { EntityId = seasonB.Id, Code = EntityPositionCodes.Season, Value = 2 },
            new() { EntityId = episodeA.Id, Code = EntityPositionCodes.Episode, Value = 3 },
            new() { EntityId = episodeB.Id, Code = EntityPositionCodes.Episode, Value = 3 });
        await db.SaveChangesAsync(); await ReserveAsync(db, connection, episodeA.Id);
        db.Acquisitions.Add(Acquisition(episodeB.Id, kind: EntityKind.VideoEpisode));
        await ConflictAsync(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task MetadataEditsAndReparentingCannotSilentlyMergeCompetingOwners() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (connection, movie) = await SeedAsync(db);
        db.EntityExternalIds.Add(Identity(movie, ExternalIdProviders.Tmdb, "123")); await db.SaveChangesAsync();
        await ReserveAsync(db, connection, movie);
        var other = Entity(EntityKind.Movie); db.Entities.Add(other); await db.SaveChangesAsync();
        db.Acquisitions.Add(Acquisition(other.Id)); await db.SaveChangesAsync();
        db.EntityExternalIds.Add(Identity(other.Id, ExternalIdProviders.Tmdb, "123"));
        await ConflictAsync(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        Assert.False(await db.EntityExternalIds.AnyAsync(row => row.EntityId == other.Id));
        var series = Entity(EntityKind.VideoSeries); var otherSeries = Entity(EntityKind.VideoSeries);
        var episode = Entity(EntityKind.VideoEpisode, series.Id);
        db.Entities.AddRange(series, otherSeries, episode); await db.SaveChangesAsync();
        await ReserveAsync(db, connection, episode.Id);
        db.Acquisitions.Add(Acquisition(otherSeries.Id, kind: EntityKind.VideoSeries)); await db.SaveChangesAsync();
        await ConflictAsync(() => db.Entities.Where(row => row.Id == episode.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ParentEntityId, otherSeries.Id)));
        Assert.Equal(series.Id, (await db.Entities.AsNoTracking().SingleAsync(row => row.Id == episode.Id)).ParentEntityId);
    }

    [Fact]
    public async Task IndependentConnectionsCannotClaimSameScopeAndReleasedIntentCannotBeReactivated() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (connection, movie) = await SeedAsync(db);
        var otherConnection = Connection(); db.IntegrationConnections.Add(otherConnection); await db.SaveChangesAsync();
        var owner = Guid.NewGuid(); await ReserveAsync(db, connection, movie, ownerId: owner);
        await ReserveAsync(db, connection, movie, ownerId: owner);
        await ConflictAsync(() => ReserveAsync(db, otherConnection.Id, movie)); db.ChangeTracker.Clear();
        await db.FulfillmentReservations.Where(row => row.OwnerId == owner)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ReleasedAt, DateTimeOffset.UtcNow));
        await ReserveAsync(db, otherConnection.Id, movie);
        await ConflictAsync(() => db.FulfillmentReservations.Where(row => row.OwnerId == owner)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ReleasedAt, (DateTimeOffset?)null)));
    }

    [Fact]
    public async Task EligibilityReaderUsesGuardMatcherForHierarchyIdentityAndBookRendition() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var (connection, movie) = await SeedAsync(db);
        var duplicate = Entity(EntityKind.Movie);
        var series = Entity(EntityKind.VideoSeries);
        var episode = Entity(EntityKind.VideoEpisode, series.Id);
        var book = Entity(EntityKind.Book);
        db.Entities.AddRange(duplicate, series, episode, book);
        db.EntityExternalIds.AddRange(
            Identity(movie, ExternalIdProviders.Tmdb, "123"),
            Identity(duplicate.Id, ExternalIdProviders.Tmdb, "123"));
        await db.SaveChangesAsync();
        await ReserveAsync(db, connection, movie);
        await ReserveAsync(db, connection, episode.Id);
        await ReserveAsync(db, connection, book.Id, BookRendition.Ebook);

        var owners = await new EfExternalFulfillmentOwnershipReader(db).ListAsync([
            new(duplicate.Id, EntityKind.Movie),
            new(series.Id, EntityKind.VideoSeries),
            new(book.Id, EntityKind.Book, BookRendition.Audiobook),
        ], default);
        var ebookOwner = await new EfExternalFulfillmentOwnershipReader(db).ListAsync([
            new(book.Id, EntityKind.Book, BookRendition.Ebook),
        ], default);

        Assert.Equal("Fixture", owners[duplicate.Id].ConnectionName);
        Assert.Equal("Fixture", owners[series.Id].ConnectionName);
        Assert.False(owners.ContainsKey(book.Id));
        Assert.Equal("Fixture", ebookOwner[book.Id].ConnectionName);
        Assert.Equal(2, owners.Count);
    }

    private static async Task ReserveAsync(PrismediaDbContext db, Guid connection, Guid entity, BookRendition? rendition = null, Guid? ownerId = null) {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await new EfFulfillmentReservationStore(db).ReserveAsync(ownerId ?? Guid.NewGuid(), FulfillmentOwnerKind.ExternalManager,
            connection, entity, rendition, default);
        await transaction.CommitAsync();
    }

    private static async Task ConflictAsync(Func<Task> action) {
        var error = await Assert.ThrowsAnyAsync<Exception>(action);
        Assert.True(FulfillmentOwnershipViolation.IsConflict(error), error.ToString());
    }

    private static async Task<(Guid Connection, Guid Movie)> SeedAsync(PrismediaDbContext db) {
        var connection = Connection(); var movie = Entity(EntityKind.Movie);
        db.IntegrationConnections.Add(connection); db.Entities.Add(movie); await db.SaveChangesAsync();
        return (connection.Id, movie.Id);
    }

    private static IntegrationConnectionRow Connection() => new() { Id = Guid.NewGuid(), PluginId = "fixture", Name = "Fixture", BaseUrl = "http://fixture.invalid", Revision = 1 };
    private static EntityRow Entity(EntityKind kind, Guid? parent = null) => new() { Id = Guid.NewGuid(), KindCode = kind.ToCode(), Title = "Fixture", ParentEntityId = parent };
    private static EntityExternalIdRow Identity(Guid entity, string provider, string value) => new() { Id = Guid.NewGuid(), EntityId = entity, Provider = provider, Value = value };
    private static AcquisitionRow Acquisition(Guid? entity, AcquisitionStatus status = AcquisitionStatus.Pending, EntityKind kind = EntityKind.Movie) =>
        new() { Id = Guid.NewGuid(), EntityId = entity, Kind = kind, Status = status, Title = "Fixture" };
}
