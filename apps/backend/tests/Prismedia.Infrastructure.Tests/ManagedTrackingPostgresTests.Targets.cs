using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Domain.Integrations;

namespace Prismedia.Infrastructure.Tests;

public sealed partial class ManagedTrackingPostgresTests {
    [Fact]
    public async Task TargetMigrationBackfillsExactAssociationsWithoutChangingControlIdentity() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db);
        var accepted = await AdoptAsync(Store(db), fixture);
        var target = Assert.Single(accepted.Tracking.Targets);
        var bound = Assert.Single(Assert.Single(accepted.Tracking.Bindings).Entities);
        Assert.Equal(new ManagedTargetBinding(bound.Target, bound.EntityId), target);
        var fingerprint = ManagedControlIdentity.From(accepted.Tracking).Fingerprint;
        await database.MigrateAsync("20260916230957_AddManagedControls");
        await database.MigrateAsync("20260917000917_RetainManagedTargets");
        // Current persistence projections require all current columns after verifying the historical upgrade path.
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var migrated = (await Store(db).FindAsync(accepted.Tracking.Id, default))!.Tracking;
        Assert.Equal(target, Assert.Single(migrated.Targets));
        Assert.Equal(fingerprint, ManagedControlIdentity.From(migrated).Fingerprint);
        Assert.Equal(accepted.Tracking.Revision, migrated.Revision);
    }

    [Fact]
    public async Task MissingBytesRetainTargetsAndTheirControlFingerprint() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db);
        var store = Store(db); var accepted = await AdoptAsync(store, fixture);
        var fingerprint = ManagedControlIdentity.From(accepted.Tracking).Fingerprint;
        File.Delete(fixture.Path);
        var observed = await store.ObserveAsync(fixture.ConnectionId, fixture.Snapshot, default);
        var plan = ManagedSourceReconciliation.Plan(accepted.Tracking.Bindings, observed.Files);
        Assert.Null(plan.ReviewReason);
        await store.ApplyAsync(accepted, observed, null, plan.Changes, default);
        var saved = (await store.FindAsync(accepted.Tracking.Id, default))!.Tracking;
        Assert.Equal(accepted.Tracking.Targets, saved.Targets);
        Assert.False(Assert.Single(saved.Bindings).IsAvailable);
        Assert.Equal(fingerprint, (await Controls(db).RequireScopeAsync(fixture.ConnectionId, saved.Id, default)).Fingerprint);
    }

    [Fact]
    public async Task PendingAdoptionSurvivesTargetMigrationWithoutInventingTargets() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db);
        await Store(db).CreateAsync(fixture.ConnectionId, fixture.Request, "Holding", default);
        await database.MigrateAsync("20260916230957_AddManagedControls");
        await database.MigrateAsync("20260917000917_RetainManagedTargets");
        // Current persistence projections require all current columns after verifying the historical upgrade path.
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var pending = (await Store(db).FindAsync(fixture.Request.OperationId, default))!.Tracking;
        Assert.Empty(pending.Targets);
        Assert.Empty(pending.Bindings);
    }
}
