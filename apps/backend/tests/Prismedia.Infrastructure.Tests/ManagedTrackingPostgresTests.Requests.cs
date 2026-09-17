using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Queue;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Tests;

public sealed partial class ManagedTrackingPostgresTests {
    [Fact]
    public async Task WantedRequestReservesOwnershipAndRootBeforeRemoteDispatch() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedWantedAsync(db);
        var accepted = await Requests(db).CreateAsync(fixture.Operation, fixture.Plan, default);
        Assert.Equal(ManagedRequestPhase.PendingCreation, accepted.Operation.State.Phase);
        var owner = Assert.Single(await db.FulfillmentReservations.ToArrayAsync());
        Assert.Equal(FulfillmentOwnerKind.ExternalManager, owner.OwnerKind);
        Assert.Equal(fixture.Fixture.EntityId, owner.EntityId);
        Assert.Single(await db.JobRuns.ToArrayAsync());
        Assert.Empty(await db.ManagedHoldings.ToArrayAsync());
        Assert.Equal(accepted.Operation.State.OperationId, Assert.Single(await new LibraryScanPersistenceService(db).ListManagedHoldingsForRootAsync(fixture.Fixture.RootId, default)));
        db.Acquisitions.Add(new() { Id = Guid.NewGuid(), EntityId = owner.EntityId, Kind = EntityKind.Movie, Status = AcquisitionStatus.Pending });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ReplayedWantedIntentHasOneOwnerAndQueueRunAndRejectsChangedSettings() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var first = database.CreateContext();
        var fixture = await SeedWantedAsync(first);
        await using var second = database.CreateContext();
        var results = await Task.WhenAll(Requests(first).CreateAsync(fixture.Operation, fixture.Plan, default),
            Requests(second).CreateAsync(new(fixture.Operation.State), fixture.Plan, default));
        Assert.All(results, result => Assert.Equal(fixture.Operation.State.OperationId, result.Operation.State.OperationId));
        Assert.Single(await first.ManagedRequests.ToArrayAsync()); Assert.Single(await first.FulfillmentReservations.ToArrayAsync()); Assert.Single(await first.JobRuns.ToArrayAsync());
        var request = fixture.Plan.Request with { Monitored = true };
        await Assert.ThrowsAsync<ManagedRequestConflictException>(() => Requests(first).CreateAsync(fixture.Operation,
            fixture.Plan with { Request = request, Fingerprint = ManagedRequestIdentity.Fingerprint(request) }, default));
    }

    [Fact]
    public async Task WantedQueueFailureRollsBackIntentAndFulfillmentOwner() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using (var db = database.CreateContext()) {
            var fixture = await SeedWantedAsync(db);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_request_queue() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Simulated request publication failure'; END $$;
                CREATE TRIGGER reject_request_queue BEFORE INSERT ON job_runs FOR EACH ROW EXECUTE FUNCTION reject_request_queue();
                """);
            await Assert.ThrowsAnyAsync<Exception>(() => Requests(db).CreateAsync(fixture.Operation, fixture.Plan, default));
        }
        await using var check = database.CreateContext();
        Assert.Empty(await check.ManagedRequests.ToArrayAsync()); Assert.Empty(await check.FulfillmentReservations.ToArrayAsync()); Assert.Empty(await check.JobRuns.ToArrayAsync());
    }

    [Fact]
    public async Task CancellationWinningDispatchRaceReleasesOwnerAndBlocksLateCreationFence() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedWantedAsync(db); var store = Requests(db);
        await store.CreateAsync(fixture.Operation, fixture.Plan, default);
        var cancelled = new ManagedRequestOperation(fixture.Operation.State); cancelled.Cancel();
        await using (var other = database.CreateContext()) await Requests(other).SaveAsync(cancelled, 1, null, false, default);
        fixture.Operation.BeginCreation();
        await Assert.ThrowsAsync<ManagedRequestConflictException>(() => store.SaveAsync(fixture.Operation, 1, null, true, default));
        db.ChangeTracker.Clear();
        Assert.NotNull((await db.FulfillmentReservations.SingleAsync()).ReleasedAt);
        Assert.Empty(await new LibraryScanPersistenceService(db).ListManagedHoldingsForRootAsync(fixture.Fixture.RootId, default));
        db.Acquisitions.Add(new() { Id = Guid.NewGuid(), EntityId = fixture.Fixture.EntityId, Kind = EntityKind.Movie, Status = AcquisitionStatus.Pending });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AcceptedHoldingCanControlOwnedWantedTargetsWithoutInventingSourceFiles() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedWantedAsync(db); var store = Requests(db);
        var accepted = await store.CreateAsync(fixture.Operation, fixture.Plan, default);
        await store.AcceptHoldingAsync(accepted, fixture.Fixture.Snapshot with { Files = [] }, default);
        var holding = (await Store(db).FindAsync(accepted.Operation.State.OperationId, default))!.Tracking;
        Assert.Equal(ManagedTrackingStatus.WaitingForFiles, holding.Status);
        Assert.Empty(holding.Bindings); Assert.Empty(await db.EntityFiles.ToArrayAsync());
        Assert.Equal(fixture.Fixture.EntityId, Assert.Single(holding.Targets).EntityId);
        var scope = await Controls(db).RequireScopeAsync(holding.ConnectionId, holding.Id, default);
        Assert.Equal("1", Assert.Single(scope.Scope.Targets).RemoteId);
        Assert.True((await db.Entities.AsNoTracking().SingleAsync()).IsWanted);
    }

    [Fact]
    public async Task FileArrivalCompletesSameWantedIdentityAndPreservesControlScopeForLaterUpgrades() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedWantedAsync(db); var store = Requests(db);
        var accepted = await store.CreateAsync(fixture.Operation, fixture.Plan, default);
        await store.AcceptHoldingAsync(accepted, fixture.Fixture.Snapshot with { Files = [] }, default);
        var pending = (await store.FindAsync(accepted.Operation.State.OperationId, default))!;
        var tracking = (await Store(db).FindAsync(accepted.Operation.State.OperationId, default))!;
        var (action, plan) = ControlIntent(tracking);
        await Controls(db).CreateAsync(action, plan, default);
        Assert.True((await store.MaterializeAsync(pending, fixture.Fixture.Snapshot, default)).Imported);
        var entity = await db.Entities.AsNoTracking().SingleAsync();
        Assert.Equal(fixture.Fixture.EntityId, entity.Id); Assert.False(entity.IsWanted); Assert.True(entity.IsOrganized); Assert.Equal("Retained title", entity.Title);
        Assert.Equal(fixture.Fixture.EntityId, (await db.EntityFiles.AsNoTracking().SingleAsync()).EntityId);
        Assert.Equal(ManagedRequestPhase.Completed, (await store.FindAsync(accepted.Operation.State.OperationId, default))!.Operation.State.Phase);
        Assert.Equal(plan.Request.ScopeFingerprint, (await Controls(db).RequireScopeAsync(tracking.Tracking.ConnectionId, tracking.Tracking.Id, default)).Fingerprint);
        action.BeginSearch(); await Controls(db).SaveAsync(action, 1, null, true, default);
        Assert.Contains(await db.JobRuns.ToArrayAsync(), job => job.Type == JobType.RefreshEntity && job.TargetEntityId == entity.Id.ToString());
        Assert.Null((await db.FulfillmentReservations.AsNoTracking().SingleAsync()).ReleasedAt);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(fixture.Fixture.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrSizeMismatchedBytesKeepTheOriginalEntityWanted(bool missing) {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedWantedAsync(db); var store = Requests(db);
        var accepted = await store.CreateAsync(fixture.Operation, fixture.Plan, default);
        await store.AcceptHoldingAsync(accepted, fixture.Fixture.Snapshot with { Files = [] }, default);
        if (missing) File.Delete(fixture.Fixture.Path); else await File.WriteAllBytesAsync(fixture.Fixture.Path, [1]);
        var pending = (await store.FindAsync(accepted.Operation.State.OperationId, default))!;
        var result = await store.MaterializeAsync(pending, fixture.Fixture.Snapshot, default);
        Assert.False(result.Imported); Assert.NotNull(result.WaitingReason);
        Assert.Empty(await db.EntityFiles.ToArrayAsync()); Assert.True((await db.Entities.AsNoTracking().SingleAsync()).IsWanted);
    }

    [Fact]
    public async Task HoldingOutsideMappedRootCannotBeAcceptedOrMoved() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedWantedAsync(db); var store = Requests(db);
        var accepted = await store.CreateAsync(fixture.Operation, fixture.Plan, default);
        await Assert.ThrowsAsync<ArgumentException>(() => store.AcceptHoldingAsync(accepted, fixture.Fixture.Snapshot with { Path = "/other/movie" }, default));
        Assert.Empty(await db.ManagedHoldings.ToArrayAsync());
        Assert.Equal(ManagedRequestPhase.PendingCreation, (await store.FindAsync(accepted.Operation.State.OperationId, default))!.Operation.State.Phase);
    }

    [Fact]
    public async Task AcceptedHoldingIsRevalidatedBeforeAutomaticControls() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedWantedAsync(db); var store = Requests(db);
        var accepted = await store.CreateAsync(fixture.Operation, fixture.Plan, default);
        await store.AcceptHoldingAsync(accepted, fixture.Fixture.Snapshot with { Files = [] }, default);
        var waiting = (await store.FindAsync(accepted.Operation.State.OperationId, default))!;
        await store.ValidateHoldingAsync(waiting, fixture.Fixture.Snapshot, default);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ValidateHoldingAsync(waiting,
            fixture.Fixture.Snapshot with { Path = "/other/movie" }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ValidateHoldingAsync(waiting,
            fixture.Fixture.Snapshot with { Item = fixture.Fixture.Snapshot.Item with { RemoteId = "replacement" }, Files = [] }, default));
        await db.FulfillmentReservations.ExecuteUpdateAsync(set => set.SetProperty(row => row.ReleasedAt, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<ManagedRequestConflictException>(() => store.ValidateHoldingAsync(waiting, fixture.Fixture.Snapshot, default));
        Assert.Empty(await db.ManagedControls.ToArrayAsync()); Assert.Empty(await db.EntityFiles.ToArrayAsync());
    }

    [Fact]
    public async Task RemovedQueueHistoryCanRecoverRequestWithoutResettingCreationUncertainty() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedWantedAsync(db); var store = Requests(db);
        await store.CreateAsync(fixture.Operation, fixture.Plan, default);
        fixture.Operation.BeginCreation(); await store.SaveAsync(fixture.Operation, 1, null, true, default);
        await db.JobGraphs.ExecuteDeleteAsync();
        await db.ManagedRequests.ExecuteUpdateAsync(set => set.SetProperty(row => row.NextCheckAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await store.QueueDueAsync(default);
        Assert.Single(await db.JobRuns.ToArrayAsync());
        Assert.Equal(ManagedRequestPhase.CreationUncertain, (await store.FindAsync(fixture.Operation.State.OperationId, default))!.Operation.State.Phase);
    }

    private EfManagedRequestStore Requests(PrismediaDbContext db) => new(db,
        new EfExternalLibraryMountStore(db, new(Path.Combine(workspace, "data"), Path.Combine(workspace, "cache")), new SettingsSnapshotCache()),
        new JobQueueService(db), new EfEntityLifecycleMutationLease(db, new EfEntityHierarchyReader(db)));

    private async Task<WantedFixture> SeedWantedAsync(PrismediaDbContext db) {
        var fixture = await SeedAsync(db);
        db.EntityFiles.RemoveRange(await db.EntityFiles.ToArrayAsync());
        (await db.Entities.SingleAsync()).IsWanted = true;
        db.EntityExternalIds.Add(new() { Id = Guid.NewGuid(), EntityId = fixture.EntityId, Provider = ExternalIdProviders.Tmdb, Value = "1" });
        await db.SaveChangesAsync();
        fixture = fixture with { Snapshot = fixture.Snapshot with { Item = fixture.Snapshot.Item with { ProfileId = "1" } } };
        var operation = ManagedRequestOperation.Create(Guid.NewGuid(), fixture.ConnectionId, fixture.EntityId, fixture.RootId);
        var request = new CreateManagedRequestInput(operation.State.OperationId, fixture.EntityId, fixture.RootId,
            new(EntityKind.Movie, fixture.Snapshot.Item.ExternalIds), "1", false, true);
        var plan = new ManagedRequestPlan(request, new(operation.State.OperationId, request.ReviewedWork, "1", "1", "/movies"),
            "Retained title", ManagedRequestIdentity.Fingerprint(request));
        return new(fixture, operation, plan);
    }
    private sealed record WantedFixture(Fixture Fixture, ManagedRequestOperation Operation, ManagedRequestPlan Plan);
}
