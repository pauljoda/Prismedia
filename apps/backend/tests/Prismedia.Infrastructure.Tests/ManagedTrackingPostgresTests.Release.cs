using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Queue;

namespace Prismedia.Infrastructure.Tests;

public sealed partial class ManagedTrackingPostgresTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WantedHandoffStopsTheOldRequestAndPreservesItsWantedOrImportedIdentity(bool imported) {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedWantedAsync(db); var requests = Requests(db);
        var accepted = await requests.CreateAsync(fixture.Operation, fixture.Plan, default);
        await requests.AcceptHoldingAsync(accepted, fixture.Fixture.Snapshot with { Files = [] }, default);
        var previous = (await requests.FindAsync(accepted.Operation.State.OperationId, default))!;
        if (imported) await requests.MaterializeAsync(previous, fixture.Fixture.Snapshot, default);
        var holding = (await Store(db).FindAsync(accepted.Operation.State.OperationId, default))!;
        var store = Releases(db);
        await store.BeginAsync(holding.Tracking.ConnectionId, holding.Tracking.Id, ReleaseIntent(holding), default);
        if (!imported) {
            await Assert.ThrowsAsync<ManagedRequestConflictException>(() => requests.MaterializeAsync(previous, fixture.Fixture.Snapshot, default));
            Assert.True((await requests.FindAsync(holding.Tracking.Id, default))!.Operation.State.ReviewRequired);
        }
        var work = (await store.FindAsync(holding.Tracking.Id, default))!;
        await store.CompleteAsync(work, ReleaseEvidence(work), default);
        Assert.Equal(ManagedRequestPhase.OwnershipReleased, (await requests.FindAsync(holding.Tracking.Id, default))!.Operation.State.Phase);
        Assert.Equal(!imported, (await db.Entities.AsNoTracking().SingleAsync()).IsWanted);
        Assert.Equal(imported ? 1 : 0, await db.EntityFiles.CountAsync());
    }

    [Fact]
    public async Task QueueFailureRollsBackTheFreezeAndAReleasedHoldingCanBeLinkedWithNewIntent() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db); var holding = await AdoptAsync(Store(db), fixture);
        await db.JobGraphs.ExecuteDeleteAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_release_queue() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Simulated release queue failure'; END $$;
            CREATE TRIGGER reject_release_queue BEFORE INSERT ON job_runs FOR EACH ROW EXECUTE FUNCTION reject_release_queue();
            """);
        await Assert.ThrowsAnyAsync<Exception>(() => Releases(db).BeginAsync(fixture.ConnectionId, holding.Tracking.Id, ReleaseIntent(holding), default));
        Assert.Equal(ManagedTrackingStatus.Tracking, (await Store(db).FindAsync(holding.Tracking.Id, default))!.Tracking.Status);
        Assert.Null(await Releases(db).FindAsync(holding.Tracking.Id, default));
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_release_queue ON job_runs");
        var intent = ReleaseIntent(holding);
        await Releases(db).BeginAsync(fixture.ConnectionId, holding.Tracking.Id, intent, default);
        var work = (await Releases(db).FindAsync(holding.Tracking.Id, default))!;
        await Releases(db).CompleteAsync(work, ReleaseEvidence(work), default);
        await db.JobGraphs.ExecuteDeleteAsync();
        await Store(db).QueueDueAsync(default);
        Assert.Empty(await db.JobRuns.ToArrayAsync());
        var again = await AdoptAsync(Store(db), fixture with { Request = fixture.Request with { OperationId = Guid.NewGuid() } });
        Assert.NotEqual(holding.Tracking.Id, again.Tracking.Id);
        Assert.Single(await db.FulfillmentReservations.Where(row => row.ReleasedAt == null).ToArrayAsync());
        Assert.Equal(2, await db.ManagedHoldings.CountAsync());
    }
    [Fact]
    public async Task ReleaseFreezesActionsBeforeObservationAndArchivesBindingsWithoutChangingFiles() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db); var holding = await AdoptAsync(Store(db), fixture);
        var store = Releases(db); var intent = ReleaseIntent(holding);
        var pending = await store.BeginAsync(fixture.ConnectionId, holding.Tracking.Id, intent, default);
        Assert.Equal(ManagedTrackingStatus.ReleasePending, pending.Status);
        Assert.Equal(pending.Revision, (await store.BeginAsync(fixture.ConnectionId, holding.Tracking.Id, intent, default)).Revision);
        Assert.Single(await db.FulfillmentReservations.Where(row => row.ReleasedAt == null).ToArrayAsync());
        Assert.Single(await db.ManagedSourceBindings.ToArrayAsync());
        await using (var competing = database.CreateContext()) {
            competing.Acquisitions.Add(new() { Id = Guid.NewGuid(), EntityId = fixture.EntityId, Kind = EntityKind.Movie, Status = AcquisitionStatus.Pending });
            var conflict = await Assert.ThrowsAsync<DbUpdateException>(() => competing.SaveChangesAsync());
            Assert.True(FulfillmentOwnershipViolation.IsConflict(conflict));
        }
        await Assert.ThrowsAsync<ManagedControlConflictException>(() => Controls(db).RequireScopeAsync(fixture.ConnectionId, pending.Id, default));
        var work = (await store.FindAsync(pending.Id, default))!;
        await store.CompleteAsync(work, ReleaseEvidence(work), default);
        await store.CompleteAsync(work, ReleaseEvidence(work), default);
        var released = (await Store(db).FindAsync(pending.Id, default))!.Tracking;
        Assert.Equal(ManagedTrackingStatus.Released, released.Status);
        Assert.NotNull(released.ReleasedAt);
        Assert.Empty(await db.ManagedSourceBindings.ToArrayAsync());
        Assert.Single(await db.FulfillmentReservations.Where(row => row.ReleasedAt != null).ToArrayAsync());
        var saved = await db.EntityFiles.AsNoTracking().SingleAsync();
        Assert.Equal(fixture.SourceId, saved.Id); Assert.Equal(EntityFileRole.Source, saved.Role);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(fixture.Path));
        Assert.True((await db.Entities.AsNoTracking().SingleAsync()).IsOrganized);
        Assert.Contains(fixture.SourceId.ToString(), (await db.ManagedHoldings.AsNoTracking().SingleAsync()).ReleasedBindingsJson);
        db.Acquisitions.Add(new() { Id = Guid.NewGuid(), EntityId = fixture.EntityId, Kind = EntityKind.Movie, Status = AcquisitionStatus.Pending });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ReleaseRejectsPendingAndClosedUnverifiedActionsAndChangedReviewedScope() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db); var holding = await AdoptAsync(Store(db), fixture);
        var store = Releases(db); var request = ReleaseIntent(holding);
        await Assert.ThrowsAsync<ManagedControlConflictException>(() => store.BeginAsync(fixture.ConnectionId, holding.Tracking.Id,
            request with { ScopeFingerprint = new string('0', 64) }, default));
        var (action, plan) = ControlIntent(holding); await Controls(db).CreateAsync(action, plan, default);
        await Assert.ThrowsAsync<ManagedControlConflictException>(() => store.BeginAsync(fixture.ConnectionId, holding.Tracking.Id, request, default));
        action.BeginSearch(); await Controls(db).SaveAsync(action, 1, null, true, default);
        action.RequireReview(); await Controls(db).SaveAsync(action, 2, "Uncertain", false, default);
        action.CloseUnverified(); await Controls(db).SaveAsync(action, 3, "Uncertain", false, default);
        await Assert.ThrowsAsync<ManagedControlConflictException>(() => store.BeginAsync(fixture.ConnectionId, holding.Tracking.Id, request, default));
        Assert.Single(await db.FulfillmentReservations.Where(row => row.ReleasedAt == null).ToArrayAsync());
    }

    [Fact]
    public async Task UnchangedScopeRemainsReviewableAfterRoutineFileReconciliation() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db); var holding = await AdoptAsync(Store(db), fixture);
        var intent = ReleaseIntent(holding);
        var observation = await Store(db).ObserveAsync(fixture.ConnectionId, fixture.Snapshot, default);
        await Store(db).ApplyAsync(holding, observation, null, [], default);
        var accepted = await Releases(db).BeginAsync(fixture.ConnectionId, holding.Tracking.Id, intent, default);
        Assert.Equal(ManagedTrackingStatus.ReleasePending, accepted.Status);
        Assert.True(accepted.Revision > intent.ExpectedRevision);
    }

    [Fact]
    public async Task ChangedOrBusyEvidenceAndStaleWorkerCannotReleaseOrRebindTheScope() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db); var holding = await AdoptAsync(Store(db), fixture);
        var observation = await Store(db).ObserveAsync(fixture.ConnectionId, fixture.Snapshot, default);
        var store = Releases(db);
        await store.BeginAsync(fixture.ConnectionId, holding.Tracking.Id, ReleaseIntent(holding), default);
        var work = (await store.FindAsync(holding.Tracking.Id, default))!; var evidence = ReleaseEvidence(work);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CompleteAsync(work, evidence with { QueueEmpty = false }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.CompleteAsync(work, evidence with { State = evidence.State with { Path = "/different" } }, default));
        await using (var worker = database.CreateContext())
            await Assert.ThrowsAsync<ConnectionConflictException>(() => Store(worker).ApplyAsync(holding, observation, null, [], default));
        await store.RecordProblemAsync(work, "Remote app unavailable", default);
        await Assert.ThrowsAsync<ManagedControlConflictException>(() => store.CompleteAsync(work, evidence, default));
        Assert.Single(await db.FulfillmentReservations.Where(row => row.ReleasedAt == null).ToArrayAsync());
    }

    private EfManagedReleaseStore Releases(PrismediaDbContext db) => new(db, Store(db), Controls(db),
        new EfEntityLifecycleMutationLease(db, new EfEntityHierarchyReader(db)));
    private static ReleaseManagedHoldingRequest ReleaseIntent(ManagedTrackingWork holding) => new(Guid.NewGuid(), holding.Tracking.Revision,
        ManagedControlIdentity.From(holding.Tracking).Fingerprint, "/movies/film");
    private static ManagedReleaseObservation ReleaseEvidence(ManagedReleaseWork work) {
        var scope = ManagedControlIdentity.From(work.Holding).Scope;
        return new(new(new(scope.Item.RemoteId, scope.Item.EntityKind, "Film", 2024, scope.Item.ExpectedExternalIds, false, "1", 1),
            work.Request.ExpectedPath, scope.Targets.Select(target => new ManagedTargetMonitoring(target, false)).ToArray(), new(true, true, true)), true, true);
    }
}
