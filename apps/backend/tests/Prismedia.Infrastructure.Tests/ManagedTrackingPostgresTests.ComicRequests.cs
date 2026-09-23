using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed partial class ManagedTrackingPostgresTests {
    [Fact]
    public async Task WantedIssueCanJoinAPreviouslyLinkedComicRunWithoutLosingExistingOwnership() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db);
        var seriesId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var root = await db.LibraryRoots.SingleAsync(row => row.Id == fixture.RootId);
        root.ScanBooks = true;
        db.Entities.Add(new EntityRow { Id = seriesId, KindCode = EntityKind.ComicSeries.ToCode(), Title = "Fixture Run" });
        var existing = await db.Entities.SingleAsync(row => row.Id == fixture.EntityId);
        existing.KindCode = EntityKind.ComicInstallment.ToCode();
        existing.ParentEntityId = seriesId;
        db.EntityPositions.Add(new EntityPositionRow { EntityId = existing.Id,
            Code = EntityPositionCodes.Chapter, Value = 12, Label = "12.5" });
        db.EntityExternalIds.Add(new EntityExternalIdRow { Id = Guid.NewGuid(), EntityId = seriesId,
            Provider = ExternalIdProviders.ComicVine, Value = "4050-42" });
        db.Entities.Add(new EntityRow { Id = missingId, KindCode = EntityKind.ComicInstallment.ToCode(),
            ParentEntityId = seriesId, Title = "Half issue", IsWanted = true });
        db.EntityExternalIds.Add(new EntityExternalIdRow { Id = Guid.NewGuid(), EntityId = missingId,
            Provider = ExternalIdProviders.ComicVine, Value = "4000-7" });
        db.EntityPositions.Add(new EntityPositionRow { EntityId = missingId,
            Code = EntityPositionCodes.Chapter, Value = 1, Label = "½" });
        await db.SaveChangesAsync();

        var item = new ManagedItemInput(EntityKind.ComicSeries, "7",
            new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4050-42" });
        fixture = fixture with {
            Request = fixture.Request with { Item = item, Selections = [new("19", fixture.EntityId, fixture.SourceId)] },
            Snapshot = fixture.Snapshot with {
                Item = new ManagedLibraryItem("7", EntityKind.ComicSeries, "Fixture Run", 2024,
                    item.ExpectedExternalIds, true, null, 1),
                Files = [fixture.Snapshot.Files[0] with {
                    Targets = [new("19", EntityKind.ComicInstallment, "Issue 12.5", IssueLabel: "12.5")]
                }]
            }
        };
        var linked = await AdoptAsync(Store(db), fixture);
        Assert.Equal(FulfillmentOwnerKind.ConnectedLibrary,
            Assert.Single(await db.FulfillmentReservations.AsNoTracking().ToArrayAsync()).OwnerKind);

        var store = Requests(db);
        var target = await store.RequireTargetAsync(fixture.ConnectionId, seriesId, fixture.RootId, [missingId], default);
        var operation = ManagedRequestOperation.Create(Guid.NewGuid(), fixture.ConnectionId, seriesId, fixture.RootId);
        var request = new CreateManagedRequestInput(operation.State.OperationId, seriesId, fixture.RootId,
            target.Work, null, Monitored: true, Search: true, TargetEntityIds: [missingId]);
        var mount = await db.ExternalLibraryMounts.SingleAsync(row => row.ConnectionId == fixture.ConnectionId);
        var plan = new ManagedRequestPlan(request,
            new(operation.State.OperationId, target.Work, null, mount.RemoteRootId, mount.RemotePath),
            target.Title, ManagedRequestIdentity.Fingerprint(request), ExistingHoldingId: linked.Tracking.Id);
        var accepted = await store.CreateAsync(operation, plan, default);
        var observed = fixture.Snapshot with { ComicIssues = [
            new("19", "12.5", "Issue 12.5", true,
                new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4000-6" }),
            new("20", "½", "Half issue", false,
                new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4000-7" })
        ] };
        await store.AcceptHoldingAsync(accepted, observed,
            [new("20", EntityKind.ComicInstallment,
                new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4000-7" }, IssueLabel: "½")], default);

        var scope = await Controls(db).RequireScopeAsync(fixture.ConnectionId, linked.Tracking.Id, [missingId], default);
        Assert.Equal("½", Assert.Single(scope.Scope.Targets).IssueLabel);
        var owners = await db.FulfillmentReservations.AsNoTracking().OrderBy(row => row.EntityId).ToArrayAsync();
        Assert.Equal(2, owners.Length);
        Assert.Contains(owners, row => row.EntityId == fixture.EntityId && row.OwnerKind == FulfillmentOwnerKind.ConnectedLibrary);
        Assert.Contains(owners, row => row.EntityId == missingId && row.OwnerKind == FulfillmentOwnerKind.ExternalManager);
    }

    [Fact]
    public async Task ExactWantedComicIssueWaitsForItsArchiveAndPreservesItsLabel() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var connectionId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        var issueId = Guid.NewGuid();
        var directory = Directory.CreateDirectory(Path.Combine(workspace, "comic-request"));
        db.IntegrationConnections.Add(new() { Id = connectionId, PluginId = "fixture", Name = "Fixture",
            BaseUrl = "http://manager.test/", Enabled = true, Status = ConnectionStatus.Ready, Revision = 1 });
        db.LibraryRoots.Add(new() { Id = rootId, Path = directory.FullName, Label = "Comics", Enabled = true, ScanBooks = true });
        db.ExternalLibraryMounts.Add(new() { Id = Guid.NewGuid(), ConnectionId = connectionId, LibraryRootId = rootId,
            RemoteRootId = "comics", RemotePath = "/comics", LocalPath = directory.FullName });
        db.Entities.AddRange(
            new EntityRow { Id = seriesId, KindCode = EntityKind.ComicSeries.ToCode(), Title = "Fixture Run" },
            new EntityRow { Id = issueId, KindCode = EntityKind.ComicInstallment.ToCode(), ParentEntityId = seriesId,
                Title = "Half issue", IsWanted = true });
        db.EntityExternalIds.AddRange(
            new EntityExternalIdRow { Id = Guid.NewGuid(), EntityId = seriesId,
                Provider = ExternalIdProviders.ComicVine, Value = "4050-1" },
            new EntityExternalIdRow { Id = Guid.NewGuid(), EntityId = issueId,
                Provider = ExternalIdProviders.ComicVine, Value = "4000-2" });
        db.EntityPositions.Add(new EntityPositionRow { EntityId = issueId,
            Code = EntityPositionCodes.Chapter, Value = 1, Label = "½" });
        await db.SaveChangesAsync();

        var store = Requests(db);
        var target = await store.RequireTargetAsync(connectionId, seriesId, rootId, [issueId], default);
        Assert.Equal("½", Assert.Single(target.Work.Targets!).IssueLabel);
        var operation = ManagedRequestOperation.Create(Guid.NewGuid(), connectionId, seriesId, rootId);
        var request = new CreateManagedRequestInput(operation.State.OperationId, seriesId, rootId,
            target.Work, ProfileId: null, Monitored: true, Search: true, TargetEntityIds: [issueId]);
        ManagedRequestService.Validate(request);
        var plan = new ManagedRequestPlan(request,
            new(operation.State.OperationId, target.Work, null, "comics", "/comics"),
            target.Title, ManagedRequestIdentity.Fingerprint(request));
        var accepted = await store.CreateAsync(operation, plan, default);
        Assert.Equal(issueId, Assert.Single(await db.FulfillmentReservations.AsNoTracking().ToArrayAsync()).EntityId);

        var ids = new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4050-1" };
        var issueIds = new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4000-2" };
        var item = new ManagedLibraryItem("run", EntityKind.ComicSeries, "Fixture Run", 2024, ids, false, null, 0);
        var empty = new ManagedItemSnapshot(item, "/comics/Fixture Run", [], DateTimeOffset.UtcNow,
            [new("remote-issue", "½", "Half issue", false, issueIds)]);
        var resolved = new ManagedResolvedTarget("remote-issue", EntityKind.ComicInstallment, issueIds, IssueLabel: "½");
        await store.AcceptHoldingAsync(accepted, empty, [resolved], default);
        var waiting = (await store.FindAsync(operation.State.OperationId, default))!;
        Assert.False((await store.MaterializeAsync(waiting, empty, default)).Imported);

        var path = Path.Combine(directory.FullName, "half.cbz");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        var available = empty with { Files = [new("file", "/comics/half.cbz", 4, null,
            [new("remote-issue", EntityKind.ComicInstallment, "Half issue", IssueLabel: "½")])] };
        Assert.True((await store.MaterializeAsync(waiting, available, default)).Imported);
        Assert.Equal("½", Assert.Single(await db.ManagedSourceBindings.AsNoTracking().ToArrayAsync()).IssueLabel);
        Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == issueId)).IsWanted);
        Assert.Equal(ManagedRequestPhase.Completed,
            (await store.FindAsync(operation.State.OperationId, default))!.Operation.State.Phase);

        var siblingId = Guid.NewGuid();
        db.Entities.Add(new EntityRow { Id = siblingId, KindCode = EntityKind.ComicInstallment.ToCode(),
            ParentEntityId = seriesId, Title = "Interlude", IsWanted = true });
        db.EntityExternalIds.Add(new EntityExternalIdRow { Id = Guid.NewGuid(), EntityId = siblingId,
            Provider = ExternalIdProviders.ComicVine, Value = "4000-3" });
        db.EntityPositions.Add(new EntityPositionRow { EntityId = siblingId,
            Code = EntityPositionCodes.Chapter, Value = 12, Label = "12.5" });
        await db.SaveChangesAsync();
        var siblingTarget = await store.RequireTargetAsync(connectionId, seriesId, rootId, [siblingId], default);
        var siblingOperation = ManagedRequestOperation.Create(Guid.NewGuid(), connectionId, seriesId, rootId);
        var siblingRequest = new CreateManagedRequestInput(siblingOperation.State.OperationId, seriesId, rootId,
            siblingTarget.Work, null, Monitored: true, Search: true, TargetEntityIds: [siblingId]);
        var siblingPlan = new ManagedRequestPlan(siblingRequest,
            new(siblingOperation.State.OperationId, siblingTarget.Work, null, "comics", "/comics"),
            siblingTarget.Title, ManagedRequestIdentity.Fingerprint(siblingRequest),
            ExistingHoldingId: operation.State.OperationId);
        var siblingAccepted = await store.CreateAsync(siblingOperation, siblingPlan, default);
        var expanded = empty with { ComicIssues = [
            new("remote-issue", "½", "Half issue", true, issueIds),
            new("remote-sibling", "12.5", "Interlude", false,
                new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4000-3" })
        ] };
        await store.AcceptHoldingAsync(siblingAccepted, expanded,
            [new("remote-sibling", EntityKind.ComicInstallment,
                new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4000-3" }, IssueLabel: "12.5")], default);
        var siblingWaiting = (await store.FindAsync(siblingOperation.State.OperationId, default))!;
        Assert.Equal(2, (await Store(db).FindAsync(operation.State.OperationId, default))!.Tracking.Targets.Count);
        var scoped = await Controls(db).RequireScopeAsync(connectionId, operation.State.OperationId, [siblingId], default);
        Assert.Equal("12.5", Assert.Single(scoped.Scope.Targets).IssueLabel);
        var siblingPath = Path.Combine(directory.FullName, "interlude.cbz");
        await File.WriteAllBytesAsync(siblingPath, [5, 6, 7, 8]);
        var siblingAvailable = expanded with { Files = [new("second-file", "/comics/interlude.cbz", 4, null,
            [new("remote-sibling", EntityKind.ComicInstallment, "Interlude", IssueLabel: "12.5")])] };
        Assert.True((await store.MaterializeAsync(siblingWaiting, siblingAvailable, default)).Imported);
        var bindings = await db.ManagedSourceBindings.AsNoTracking().OrderBy(row => row.RemoteTargetId).ToArrayAsync();
        Assert.Equal(["½", "12.5"], bindings.Select(row => row.IssueLabel));
        Assert.Equal(ManagedRequestPhase.Completed,
            (await store.FindAsync(siblingOperation.State.OperationId, default))!.Operation.State.Phase);
    }
}
