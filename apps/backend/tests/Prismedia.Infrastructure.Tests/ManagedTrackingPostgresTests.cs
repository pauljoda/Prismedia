using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Queue;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Tests;

public sealed partial class ManagedTrackingPostgresTests : IDisposable {
    private readonly string workspace = Directory.CreateTempSubdirectory("prismedia-tracking-").FullName;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OwnershipMigrationBackfillsAcceptedScopesAndCanBeRolledBack(bool adopted) {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db);
        if (adopted) await AdoptAsync(Store(db), fixture);
        else await Store(db).CreateAsync(fixture.ConnectionId, fixture.Request, "Holding", default);
        await database.MigrateAsync("20260916203046_AddManagedLibraryTracking");
        await database.MigrateAsync("20260916205953_AddFulfillmentReservations");
        db.ChangeTracker.Clear();
        var owner = Assert.Single(await db.FulfillmentReservations.ToArrayAsync());
        Assert.Equal(fixture.EntityId, owner.EntityId);
        Assert.Equal(fixture.Request.OperationId, owner.OwnerId);
        db.Acquisitions.Add(new() { Id = Guid.NewGuid(), EntityId = fixture.EntityId, Kind = EntityKind.Movie, Status = AcquisitionStatus.Pending });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(FulfillmentOwnershipViolation.IsConflict(error));
    }

    [Fact]
    public async Task RenameMissingAndRestoreKeepEntityFileAndUserStateWhileUpdatingAvailability() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db);
        var store = Store(db);
        var tracking = await AdoptAsync(store, fixture);
        Assert.Equal(ManagedTrackingStatus.Tracking, tracking.Tracking.Status);
        var original = Assert.Single(tracking.Tracking.Bindings);
        var renamed = Path.Combine(workspace, "external", "Renamed", "upgraded.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(renamed)!);
        File.Move(fixture.Path, renamed);
        var snapshot = fixture.Snapshot with { Files = [fixture.Snapshot.Files[0] with { RemoteId = "22", Path = "/movies/Renamed/upgraded.mkv" }] };
        var evidence = await store.ObserveAsync(fixture.ConnectionId, snapshot, default);
        var plan = ManagedSourceReconciliation.Plan(tracking.Tracking.Bindings, evidence.Files);
        Assert.Null(plan.ReviewReason);
        await store.ApplyAsync(tracking, evidence, null, plan.Changes, default);
        var saved = await db.EntityFiles.AsNoTracking().SingleAsync(file => file.Id == fixture.SourceId);
        Assert.Equal(renamed, saved.Path);
        Assert.Equal(fixture.EntityId, saved.EntityId);
        Assert.Equal("Retained title", (await db.Entities.AsNoTracking().SingleAsync()).Title);
        Assert.True((await db.Entities.AsNoTracking().SingleAsync()).IsOrganized);
        Assert.Single(await db.ManagedSourceBindings.ToArrayAsync());
        Assert.Contains(await db.JobRuns.ToArrayAsync(), job => job.Type == JobType.RefreshEntity && job.TargetEntityId == fixture.EntityId.ToString());
        tracking = (await store.FindAsync(tracking.Tracking.Id, default))!;
        File.Move(renamed, renamed + ".offline");
        evidence = await store.ObserveAsync(fixture.ConnectionId, snapshot, default);
        plan = ManagedSourceReconciliation.Plan(tracking.Tracking.Bindings, evidence.Files);
        await store.ApplyAsync(tracking, evidence, null, plan.Changes, default);
        Assert.Equal(EntityFileRole.UnavailableSource, (await db.EntityFiles.AsNoTracking().SingleAsync()).Role);
        Assert.False((await db.EntityAvailability.AsNoTracking().SingleAsync()).HasSourceMedia);
        Assert.Equal(original.Entities[0].EntityId, (await db.Entities.AsNoTracking().SingleAsync()).Id);
        tracking = (await store.FindAsync(tracking.Tracking.Id, default))!;
        File.Move(renamed + ".offline", renamed);
        evidence = await store.ObserveAsync(fixture.ConnectionId, snapshot, default);
        plan = ManagedSourceReconciliation.Plan(tracking.Tracking.Bindings, evidence.Files);
        await store.ApplyAsync(tracking, evidence, null, plan.Changes, default);
        Assert.Equal(EntityFileRole.Source, (await db.EntityFiles.AsNoTracking().SingleAsync()).Role);
        Assert.True((await db.EntityAvailability.AsNoTracking().SingleAsync()).HasSourceMedia);
        Assert.Equal(fixture.SourceId, (await db.EntityFiles.AsNoTracking().SingleAsync()).Id);
    }

    [Fact]
    public async Task ConfirmedHoldingRemovalKeepsReadableFilesAndArchivesOnlyAfterBytesDisappear() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db);
        var store = Store(db);
        var work = await AdoptAsync(store, fixture);

        await store.ConfirmRemovalAsync(work, "Removed upstream", default);

        var entity = await db.Entities.AsNoTracking().SingleAsync(row => row.Id == fixture.EntityId);
        Assert.False(entity.IsLibraryArchived);
        Assert.Equal(EntityFileRole.Source, (await db.EntityFiles.AsNoTracking().SingleAsync()).Role);
        work = (await store.FindAsync(work.Tracking.Id, default))!;
        File.Move(fixture.Path, fixture.Path + ".offline");

        await store.ConfirmRemovalAsync(work, "Still removed upstream", default);

        entity = await db.Entities.AsNoTracking().SingleAsync(row => row.Id == fixture.EntityId);
        Assert.True(entity.IsLibraryArchived);
        Assert.Equal(EntityFileRole.UnavailableSource, (await db.EntityFiles.AsNoTracking().SingleAsync()).Role);
        Assert.Single(await db.ManagedSourceBindings.AsNoTracking().ToArrayAsync());
        Assert.Null(Assert.Single(await db.FulfillmentReservations.AsNoTracking().ToArrayAsync()).ReleasedAt);

        work = (await store.FindAsync(work.Tracking.Id, default))!;
        File.Move(fixture.Path + ".offline", fixture.Path);
        await store.ConfirmRemovalAsync(work, "Still removed with restored bytes", default);
        entity = await db.Entities.AsNoTracking().SingleAsync(row => row.Id == fixture.EntityId);
        Assert.False(entity.IsLibraryArchived);
        Assert.Equal(EntityFileRole.Source, (await db.EntityFiles.AsNoTracking().SingleAsync()).Role);

        work = (await store.FindAsync(work.Tracking.Id, default))!;
        await store.RecordReappearanceAsync(work.Tracking.Id, work.Tracking.Revision,
            "Remote identity reappeared", default);
        var reviewed = (await store.FindAsync(work.Tracking.Id, default))!.Tracking;
        Assert.Equal(ManagedTrackingStatus.Removed, reviewed.Status);
        Assert.Equal("Remote identity reappeared", reviewed.Problem);
    }

    [Fact]
    public async Task ConcurrentReplaysReturnTheSameAcceptedIntentAndSingleOwnershipReservation() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var first = database.CreateContext();
        var fixture = await SeedAsync(first);
        await using var second = database.CreateContext();
        var results = await Task.WhenAll(
            Store(first).CreateAsync(fixture.ConnectionId, fixture.Request, "Holding", default),
            Store(second).CreateAsync(fixture.ConnectionId, fixture.Request, "Holding", default));
        Assert.All(results, result => Assert.Equal(fixture.Request.OperationId, result.Id));
        Assert.Single(await first.ManagedHoldings.ToArrayAsync());
        Assert.Single(await first.FulfillmentReservations.ToArrayAsync());
        Assert.Single(await first.JobRuns.ToArrayAsync());
    }

    [Fact]
    public async Task PendingIntentIsIdempotentReservesRootAndSurvivesQueueHistoryRemoval() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db);
        var store = Store(db);
        var request = fixture.Request;
        var first = await store.CreateAsync(fixture.ConnectionId, request, "Holding", default);
        Assert.Equal(first.Id, (await store.CreateAsync(fixture.ConnectionId, request, "Holding", default)).Id);
        Assert.Equal(first.Id, Assert.Single(await new LibraryScanPersistenceService(db).ListManagedHoldingsForRootAsync(fixture.RootId, default)));
        Assert.Equal(JobResourceKeys.LibraryScan, (await db.JobRuns.SingleAsync()).ResourceKey);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(fixture.ConnectionId, request with { Item = request.Item with { RemoteId = "another" } }, "Other", default));
        await db.JobGraphs.ExecuteDeleteAsync();
        await store.QueueDueAsync(default);
        Assert.Single(await db.JobRuns.ToArrayAsync());
        Assert.Single(await db.ManagedHoldings.ToArrayAsync());
    }

    [Fact]
    public async Task ExistingOwnerAtReplacementPathAndLateFileChangesCannotCommitRebinding() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db);
        var store = Store(db); var tracking = await AdoptAsync(store, fixture);
        var replacement = Path.Combine(workspace, "external", "other.mkv");
        await File.WriteAllBytesAsync(replacement, [1, 2, 3]);
        var other = Guid.NewGuid();
        db.Entities.Add(new() { Id = other, KindCode = EntityKind.Movie.ToCode(), Title = "Another film" });
        db.EntityFiles.Add(new() { Id = Guid.NewGuid(), EntityId = other, Path = replacement, SizeBytes = 3 });
        await db.SaveChangesAsync();
        var snapshot = fixture.Snapshot with { Files = [fixture.Snapshot.Files[0] with { Path = "/movies/other.mkv" }] };
        var evidence = await store.ObserveAsync(fixture.ConnectionId, snapshot, default);
        var plan = ManagedSourceReconciliation.Plan(tracking.Tracking.Bindings, evidence.Files);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ApplyAsync(tracking, evidence, null, plan.Changes, default));
        db.ChangeTracker.Clear();
        Assert.Equal(fixture.Path, (await db.EntityFiles.SingleAsync(file => file.Id == fixture.SourceId)).Path);
        evidence = await store.ObserveAsync(fixture.ConnectionId, fixture.Snapshot, default);
        await File.WriteAllBytesAsync(fixture.Path, [1, 2, 3, 4]);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ApplyAsync(tracking, evidence, null, [], default));
    }

    [Fact]
    public async Task UntargetedScanAndTrackingShareOneDurableClaimResource() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var queue = new JobQueueService(db);
        var scan = await queue.EnqueueAsync(JobType.ScanLibrary, default);
        var tracking = await queue.EnqueueAsync(new EnqueueJobRequest(JobType.ManagedLibraryReconcile,
            TargetEntityKind: JobTargetKinds.ManagedHolding, TargetEntityId: Guid.NewGuid().ToString()), default);
        Assert.Equal(JobResourceKeys.LibraryScan, scan.ResourceKey);
        Assert.Equal(scan.ResourceKey, tracking.ResourceKey);
        Assert.NotNull(await queue.ClaimNextAsync("first", default));
        Assert.Null(await queue.ClaimNextAsync("second", default));
    }

    [Fact]
    public async Task ReviewWithdrawsAvailabilityAndStaleWorkersCannotOverwriteItsRevision() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db); var store = Store(db); var work = await AdoptAsync(store, fixture);
        await store.RecordProblemAsync(work.Tracking.Id, work.Tracking.Revision, ManagedTrackingStatus.NeedsReview, "Coverage changed", default);
        Assert.False((await db.EntityAvailability.AsNoTracking().SingleAsync()).HasSourceMedia);
        Assert.False(Assert.Single((await store.FindAsync(work.Tracking.Id, default))!.Tracking.Bindings).IsAvailable);
        var observation = await store.ObserveAsync(fixture.ConnectionId, fixture.Snapshot, default);
        await Assert.ThrowsAsync<ConnectionConflictException>(() => store.ApplyAsync(work, observation, null, [], default));
    }

    [Fact]
    public async Task SeriesIdentityIsComparedToItsSeriesWithoutConfusingEpisodeProviderIds() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db);
        var series = Guid.NewGuid(); var season = Guid.NewGuid();
        db.Entities.AddRange(new() { Id = series, KindCode = EntityKind.VideoSeries.ToCode(), Title = "Series" },
            new() { Id = season, ParentEntityId = series, KindCode = EntityKind.VideoSeason.ToCode(), Title = "Season" });
        var episode = await db.Entities.SingleAsync(entity => entity.Id == fixture.EntityId);
        episode.KindCode = EntityKind.VideoEpisode.ToCode(); episode.ParentEntityId = season;
        db.EntityPositions.AddRange(new() { EntityId = season, Code = EntityPositionCodes.Season, Value = 1 },
            new() { EntityId = episode.Id, Code = EntityPositionCodes.Season, Value = 1 },
            new() { EntityId = episode.Id, Code = EntityPositionCodes.Episode, Value = 1 });
        db.EntityExternalIds.AddRange(new() { Id = Guid.NewGuid(), EntityId = series, Provider = ExternalIdProviders.Tvdb, Value = "42" },
            new() { Id = Guid.NewGuid(), EntityId = episode.Id, Provider = ExternalIdProviders.Tvdb, Value = "1000" });
        await db.SaveChangesAsync();
        var item = new ManagedItemInput(EntityKind.VideoSeries, "1", new Dictionary<string, string> { [ExternalIdProviders.Tvdb] = "42" });
        fixture = fixture with {
            Request = fixture.Request with { Item = item, Selections = [new("1000", episode.Id, fixture.SourceId)] },
            Snapshot = fixture.Snapshot with {
                Item = fixture.Snapshot.Item with { EntityKind = EntityKind.VideoSeries, ExternalIds = item.ExpectedExternalIds },
                Files = [fixture.Snapshot.Files[0] with { Targets = [new("1000", EntityKind.VideoEpisode, "Episode", 1, 1)] }]
            }
        };
        var accepted = await AdoptAsync(Store(db), fixture);
        Assert.Equal(ManagedTrackingStatus.Tracking, accepted.Tracking.Status);
        Assert.Equal(episode.Id, Assert.Single(Assert.Single(accepted.Tracking.Bindings).Entities).EntityId);
    }

    [Fact]
    public async Task ProviderIdentityConflictPreventsAdoptionWithoutChangingSource() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db); var store = Store(db);
        db.EntityExternalIds.Add(new() { Id = Guid.NewGuid(), EntityId = fixture.EntityId, Provider = "tmdb", Value = "different" });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => AdoptAsync(store, fixture));
        db.ChangeTracker.Clear();
        Assert.Empty(await db.ManagedSourceBindings.ToArrayAsync());
        Assert.Equal(EntityFileRole.Source, (await db.EntityFiles.SingleAsync()).Role);
    }

    [Fact]
    public async Task QueuePublicationFailureRollsBackHoldingIntentAndItsGraph() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using (var db = database.CreateContext()) {
            var fixture = await SeedAsync(db);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_test_queue() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Simulated queue publication failure'; END $$;
                CREATE TRIGGER reject_test_queue BEFORE INSERT ON job_runs FOR EACH ROW EXECUTE FUNCTION reject_test_queue();
                """);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => Store(db).CreateAsync(fixture.ConnectionId, fixture.Request, "Holding", default));
            Assert.Contains("Simulated queue publication failure", error.ToString(), StringComparison.Ordinal);
        }
        await using var check = database.CreateContext();
        Assert.Empty(await check.ManagedHoldings.ToArrayAsync());
        Assert.Empty(await check.ManagedSourceBindings.ToArrayAsync());
        Assert.Empty(await check.FulfillmentReservations.ToArrayAsync());
        Assert.Empty(await check.JobGraphs.ToArrayAsync());
        Assert.Empty(await check.JobRuns.ToArrayAsync());
    }

    private async Task<ManagedTrackingWork> AdoptAsync(EfManagedTrackingStore store, Fixture fixture) {
        var intent = await store.CreateAsync(fixture.ConnectionId, fixture.Request, "Holding", default);
        var work = (await store.FindAsync(intent.Id, default))!;
        var observation = await store.ObserveAsync(fixture.ConnectionId, fixture.Snapshot, default);
        var plan = ManagedSourceAdoption.Plan(observation.Files, work.Selections, observation.Sources);
        Assert.Null(plan.ReviewReason);
        await store.ApplyAsync(work, observation, plan.Bindings, [], default);
        return (await store.FindAsync(intent.Id, default))!;
    }

    private EfManagedTrackingStore Store(PrismediaDbContext db) => new(db,
        new EfExternalLibraryMountStore(db, new(Path.Combine(workspace, "data"), Path.Combine(workspace, "cache")), new SettingsSnapshotCache()),
        new JobQueueService(db), new LibraryScanPersistenceService(db), new EfEntityLifecycleMutationLease(db, new EfEntityHierarchyReader(db)));

    private async Task<Fixture> SeedAsync(PrismediaDbContext db) {
        var connection = Guid.NewGuid(); var root = Guid.NewGuid(); var entity = Guid.NewGuid(); var file = Guid.NewGuid();
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(workspace, "external")).FullName, "film.mkv");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        db.IntegrationConnections.Add(new() { Id = connection, PluginId = "fixture", Name = "Fixture", BaseUrl = "http://manager.test/", Enabled = true, Status = ConnectionStatus.Ready, Revision = 1 });
        db.LibraryRoots.Add(new() { Id = root, Path = Path.GetDirectoryName(path)!, Label = "Films", Enabled = true, ScanVideos = true });
        db.ExternalLibraryMounts.Add(new() { Id = Guid.NewGuid(), ConnectionId = connection, LibraryRootId = root, RemoteRootId = "1", RemotePath = "/movies", LocalPath = Path.GetDirectoryName(path)! });
        db.Entities.Add(new() { Id = entity, KindCode = EntityKind.Movie.ToCode(), Title = "Retained title", IsOrganized = true });
        db.EntityFiles.Add(new() { Id = file, EntityId = entity, Path = path, SizeBytes = 3 });
        await db.SaveChangesAsync();
        var input = new ManagedItemInput(EntityKind.Movie, "1", new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "1" });
        var item = new ManagedLibraryItem("1", EntityKind.Movie, "Film", 2020, input.ExpectedExternalIds, false, null, 1);
        var snapshot = new ManagedItemSnapshot(item, "/movies/film", [new("11", "/movies/film.mkv", 3, null, [new("1", EntityKind.Movie, "Film")])], DateTimeOffset.UtcNow);
        return new(connection, root, entity, file, path, snapshot, new(Guid.NewGuid(), root, input, [new("1", entity, file)]));
    }

    private sealed record Fixture(Guid ConnectionId, Guid RootId, Guid EntityId, Guid SourceId, string Path,
        ManagedItemSnapshot Snapshot, TrackManagedHoldingRequest Request);
    public void Dispose() => Directory.Delete(workspace, true);
}
