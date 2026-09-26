using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Domain.Integrations;

namespace Prismedia.Infrastructure.Tests;

public sealed partial class ManagedTrackingPostgresTests {
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
}
