using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed partial class ManagedTrackingPostgresTests {
    [Fact]
    public async Task Comic_issue_label_survives_adoption_and_reconciliation() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db);
        var seriesId = Guid.NewGuid();
        db.Entities.Add(new EntityRow { Id = seriesId, KindCode = EntityKind.ComicSeries.ToCode(), Title = "Run" });
        var installment = await db.Entities.SingleAsync(entity => entity.Id == fixture.EntityId);
        installment.KindCode = EntityKind.ComicInstallment.ToCode();
        installment.ParentEntityId = seriesId;
        db.EntityPositions.Add(new EntityPositionRow { EntityId = fixture.EntityId,
            Code = EntityPositionCodes.Chapter, Value = 12, Label = "12.5" });
        db.EntityExternalIds.Add(new EntityExternalIdRow { Id = Guid.NewGuid(), EntityId = seriesId,
            Provider = ExternalIdProviders.ComicVine, Value = "4050-42" });
        await db.SaveChangesAsync();

        var item = new ManagedItemInput(EntityKind.ComicSeries, "7",
            new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4050-42" });
        fixture = fixture with {
            Request = fixture.Request with { Item = item, Selections = [new("19", fixture.EntityId, fixture.SourceId)] },
            Snapshot = fixture.Snapshot with {
                Item = new ManagedLibraryItem("7", EntityKind.ComicSeries, "Run", 2024, item.ExpectedExternalIds, true, null, 1),
                Files = [fixture.Snapshot.Files[0] with {
                    Targets = [new("19", EntityKind.ComicInstallment, "Issue 12.5", IssueLabel: "12.5")]
                }]
            }
        };

        var store = Store(db);
        var work = await AdoptAsync(store, fixture);
        var scanner = new LibraryScanPersistenceService(db);
        Assert.Empty(await scanner.ListManagedHoldingsForRootAsync(fixture.RootId, default));
        Assert.Equal([work.Tracking.Id], await scanner.ListManagedComicHoldingsForRootAsync(fixture.RootId, default));
        Assert.Equal("12.5", Assert.Single(await db.ManagedSourceBindings.AsNoTracking().ToArrayAsync()).IssueLabel);
        Assert.Equal("12.5", Assert.Single(Assert.Single(work.Tracking.Bindings).Entities).Target.IssueLabel);
        Assert.Equal("12.5", Assert.Single(ManagedControlIdentity.From(work.Tracking).Scope.Targets).IssueLabel);
        var observed = await store.ObserveAsync(fixture.ConnectionId, fixture.Snapshot, default);
        var plan = ManagedSourceReconciliation.Plan(work.Tracking.Bindings, observed.Files);
        Assert.Null(plan.ReviewReason);
        Assert.Empty(plan.Changes);

        var replacement = Path.Combine(Path.GetDirectoryName(fixture.Path)!, "renamed.cbz");
        File.Move(fixture.Path, replacement);
        var replaced = fixture.Snapshot with { Files = [fixture.Snapshot.Files[0] with {
            RemoteId = "22", Path = "/movies/renamed.cbz"
        }] };
        observed = await store.ObserveAsync(fixture.ConnectionId, replaced, default);
        plan = ManagedSourceReconciliation.Plan(work.Tracking.Bindings, observed.Files);
        Assert.Null(plan.ReviewReason);
        await store.ApplyAsync(work, observed, null, plan.Changes, default);
        var source = await db.EntityFiles.AsNoTracking().SingleAsync(file => file.Id == fixture.SourceId);
        Assert.Equal(replacement, source.Path);
        Assert.Equal(fixture.EntityId, source.EntityId);
        var restored = (await store.FindAsync(work.Tracking.Id, default))!.Tracking;
        Assert.Equal("12.5", Assert.Single(Assert.Single(restored.Bindings).Entities).Target.IssueLabel);
        Assert.Equal("22", Assert.Single(restored.Bindings).RemoteFileId);
    }
}
