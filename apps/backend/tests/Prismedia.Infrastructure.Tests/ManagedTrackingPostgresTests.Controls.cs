using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Queue;

namespace Prismedia.Infrastructure.Tests;

public sealed partial class ManagedTrackingPostgresTests {
    [Fact] public async Task ManagerIntentAndQueueAreAtomicAndReplayKeepsOneActiveAction() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db); var holding = await AdoptAsync(Store(db), fixture);
        var (action, plan) = ControlIntent(holding);
        await using var other = database.CreateContext();
        var accepted = await Task.WhenAll(Controls(db).CreateAsync(action, plan, default), Controls(other).CreateAsync(new(action.State), plan, default));
        Assert.All(accepted, value => Assert.Equal(action.State.OperationId, value.Operation.State.OperationId));
        Assert.Single(await db.ManagedControls.ToArrayAsync());
        Assert.Single(await db.JobRuns.Where(row => row.Type == JobType.ManagedControl).ToArrayAsync());
        var (competing, competingPlan) = ControlIntent(holding);
        await using var third = database.CreateContext();
        await Assert.ThrowsAsync<ManagedControlConflictException>(() => Controls(third).CreateAsync(competing, competingPlan, default));
        Assert.Single(await db.FulfillmentReservations.ToArrayAsync());
    }

    [Fact] public async Task ManagerQueueFailureRollsBackIntentButRetainsOriginalHoldingAndOwner() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using (var db = database.CreateContext()) {
            var fixture = await SeedAsync(db); var holding = await AdoptAsync(Store(db), fixture);
            var (action, plan) = ControlIntent(holding);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_control_queue() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Simulated queue publication failure'; END $$;
                CREATE TRIGGER reject_control_queue BEFORE INSERT ON job_runs FOR EACH ROW EXECUTE FUNCTION reject_control_queue();
                """);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => Controls(db).CreateAsync(action, plan, default));
            Assert.Contains("Simulated queue publication failure", error.ToString(), StringComparison.Ordinal);
        }
        await using var check = database.CreateContext();
        Assert.Empty(await check.ManagedControls.ToArrayAsync()); Assert.Single(await check.ManagedHoldings.ToArrayAsync());
        Assert.Single(await check.FulfillmentReservations.ToArrayAsync());
        Assert.Empty(await check.JobRuns.Where(row => row.Type == JobType.ManagedControl).ToArrayAsync());
    }

    [Fact] public async Task ManagerCancelFencesStaleDispatchAndKeepsFulfillmentOwner() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db); var holding = await AdoptAsync(Store(db), fixture);
        var (action, plan) = ControlIntent(holding); await Controls(db).CreateAsync(action, plan, default);
        await using (var api = database.CreateContext()) {
            var cancellation = new ManagedControlOperation(action.State); cancellation.Cancel();
            await Controls(api).SaveAsync(cancellation, 1, null, false, default);
        }
        action.BeginSearch();
        await Assert.ThrowsAsync<ManagedControlConflictException>(() => Controls(db).SaveAsync(action, 1, null, true, default));
        Assert.Single(await db.FulfillmentReservations.Where(row => row.ReleasedAt == null).ToArrayAsync());
        var (next, nextPlan) = ControlIntent(holding); await Controls(db).CreateAsync(next, nextPlan, default);
        Assert.Equal(2, await db.ManagedControls.CountAsync());
    }

    [Fact] public async Task ManagerCommandTimestampKeepsTicksAndRecoverySurvivesRemovedQueueHistory() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db); var holding = await AdoptAsync(Store(db), fixture);
        var (action, plan) = ControlIntent(holding); var store = Controls(db);
        await store.CreateAsync(action, plan, default);
        action.BeginSearch(); await store.SaveAsync(action, 1, null, true, default);
        var command = new ManagedCommandIdentity("99", DateTimeOffset.Parse("2026-09-16T20:00:00.1234567Z"));
        action.AcceptCommand(command); await store.SaveAsync(action, 2, null, false, default);
        Assert.Equal(command, (await store.FindAsync(action.State.OperationId, default))!.Operation.State.Command);
        await db.JobRuns.Where(row => row.Type == JobType.ManagedControl).ExecuteDeleteAsync();
        await db.ManagedControls.Where(row => row.Id == action.State.OperationId).ExecuteUpdateAsync(set => set.SetProperty(row => row.NextCheckAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await store.QueueDueAsync(default);
        Assert.Single(await db.JobRuns.Where(row => row.Type == JobType.ManagedControl).ToArrayAsync());
        Assert.Equal(ManagedControlPhase.AwaitingCommand, (await store.FindAsync(action.State.OperationId, default))!.Operation.State.Phase);
    }

    [Fact] public async Task ManagerScopeIgnoresUpgradedFilesButRejectsChangedLocalOwnerBeforeDispatch() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db); var holding = await AdoptAsync(Store(db), fixture);
        var (action, plan) = ControlIntent(holding); var store = Controls(db);
        await store.CreateAsync(action, plan, default);
        await db.ManagedSourceBindings.ExecuteUpdateAsync(set => set.SetProperty(row => row.RemoteFileId, "replacement").SetProperty(row => row.LocalPath, "/other/file.mkv"));
        Assert.Equal(plan.Request.ScopeFingerprint, (await store.RequireScopeAsync(fixture.ConnectionId, holding.Tracking.Id, default)).Fingerprint);
        await db.ManagedSourceBindings.ExecuteUpdateAsync(set => set.SetProperty(row => row.RemoteTargetId, "another-target"));
        action.BeginSearch();
        await Assert.ThrowsAsync<ManagedControlConflictException>(() => store.SaveAsync(action, 1, null, true, default));
        Assert.Equal(ManagedControlPhase.PendingSearch, (await store.FindAsync(action.State.OperationId, default))!.Operation.State.Phase);
    }

    private EfManagedControlStore Controls(PrismediaDbContext db) => new(db, Store(db), new JobQueueService(db));
    private static (ManagedControlOperation Action, ManagedControlPlan Plan) ControlIntent(ManagedTrackingWork holding) {
        var action = ManagedControlOperation.Create(Guid.NewGuid(), holding.Tracking.ConnectionId, holding.Tracking.Id, false, true);
        var owned = ManagedControlIdentity.From(holding.Tracking);
        var request = new CreateManagedControlRequest(action.State.OperationId, owned.Fingerprint, "/movies/film", "1",
            owned.Scope.Targets.ToDictionary(target => target.RemoteId, _ => false), new(), true);
        return (action, new(owned.Scope, request, ManagedControlIdentity.RequestFingerprint(request)));
    }
}
