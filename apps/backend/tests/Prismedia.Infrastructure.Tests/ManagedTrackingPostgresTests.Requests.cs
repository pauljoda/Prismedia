using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
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
    public async Task FiniteSeriesRequiresASonarrLookupIdentityBeforeManagerPreview() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedWantedSeriesAsync(db);
        var identity = await db.EntityExternalIds.SingleAsync(row =>
            row.EntityId == fixture.Operation.State.EntityId
            && row.Provider == ExternalIdProviders.Tvdb);
        identity.Provider = ExternalIdProviders.Imdb;
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Requests(db).RequireTargetAsync(
            fixture.ConnectionId,
            fixture.Operation.State.EntityId,
            fixture.Operation.State.LibraryRootId,
            fixture.EpisodeIds,
            default));

        Assert.Contains("TVDB or TMDB", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FiniteSeriesRequestReservesEpisodesAndMaterializesPartialSharedFilesBeforeCompletion() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedWantedSeriesAsync(db);
        var store = Requests(db);

        var accepted = await store.CreateAsync(fixture.Operation, fixture.Plan, default);
        var owners = await db.FulfillmentReservations.AsNoTracking()
            .OrderBy(row => row.EntityId)
            .ToArrayAsync();
        Assert.Equal(fixture.EpisodeIds.Order().ToArray(), owners.Select(owner => owner.EntityId).ToArray());
        Assert.All(owners, owner => Assert.Equal(FulfillmentOwnerKind.ExternalManager, owner.OwnerKind));
        var rootedIds = await db.EntityLibraryRoots.AsNoTracking()
            .Select(root => root.EntityId)
            .OrderBy(id => id)
            .ToArrayAsync();
        Assert.Equal(
            fixture.EpisodeIds.Append(fixture.Operation.State.EntityId).Order().ToArray(),
            rootedIds);

        await store.AcceptHoldingAsync(
            accepted,
            fixture.EmptySnapshot,
            fixture.ResolvedTargets,
            default);
        var waiting = (await store.FindAsync(accepted.Operation.State.OperationId, default))!;
        var partial = await store.MaterializeAsync(waiting, fixture.PartialSnapshot, default);

        Assert.False(partial.Imported);
        Assert.Contains("remaining", partial.WaitingReason, StringComparison.OrdinalIgnoreCase);
        var firstBindings = await db.ManagedSourceBindings.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, firstBindings.Length);
        Assert.Single(firstBindings.Select(binding => binding.LocalPath).Distinct(FileSystemPathComparison.Comparer));
        Assert.Equal(2, await db.EntityFiles.AsNoTracking().CountAsync());
        var holding = (await Store(db).FindAsync(accepted.Operation.State.OperationId, default))!.Tracking;
        Assert.Equal(ManagedTrackingStatus.WaitingForFiles, holding.Status);
        Assert.Equal(3, (await Controls(db).RequireScopeAsync(fixture.ConnectionId, holding.Id, default)).Scope.Targets.Count);

        var completed = await store.MaterializeAsync(waiting, fixture.CompleteSnapshot, default);

        Assert.True(completed.Imported);
        Assert.Equal(ManagedRequestPhase.Completed,
            (await store.FindAsync(accepted.Operation.State.OperationId, default))!.Operation.State.Phase);
        Assert.Equal(3, await db.ManagedSourceBindings.AsNoTracking().CountAsync());
        Assert.Equal(3, await db.EntityFiles.AsNoTracking().CountAsync());
        Assert.All(await db.Entities.AsNoTracking().Where(row => fixture.EpisodeIds.Contains(row.Id)).ToArrayAsync(),
            episode => Assert.False(episode.IsWanted));
        holding = (await Store(db).FindAsync(accepted.Operation.State.OperationId, default))!.Tracking;
        Assert.Equal(ManagedTrackingStatus.Tracking, holding.Status);
        Assert.Equal(2, Assert.Single(holding.Bindings, binding => binding.RemoteFileId == "shared").Entities.Count);
    }

    [Fact]
    public async Task FiniteSeriesRejectsMixedSelectedAndUnselectedSharedFileWithoutImporting() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedWantedSeriesAsync(db);
        var store = Requests(db);
        var accepted = await store.CreateAsync(fixture.Operation, fixture.Plan, default);
        await store.AcceptHoldingAsync(accepted, fixture.EmptySnapshot, fixture.ResolvedTargets, default);
        var waiting = (await store.FindAsync(accepted.Operation.State.OperationId, default))!;
        var mixed = fixture.PartialSnapshot with {
            Files = [fixture.PartialSnapshot.Files[0] with {
                Targets = [.. fixture.PartialSnapshot.Files[0].Targets,
                    new("unselected", EntityKind.VideoEpisode, "Other", 1, 9)]
            }]
        };

        await Assert.ThrowsAsync<ArgumentException>(() => store.MaterializeAsync(waiting, mixed, default));

        Assert.Empty(await db.ManagedSourceBindings.ToArrayAsync());
        Assert.Empty(await db.EntityFiles.ToArrayAsync());
        Assert.All(await db.Entities.AsNoTracking().Where(row => fixture.EpisodeIds.Contains(row.Id)).ToArrayAsync(),
            episode => Assert.True(episode.IsWanted));
    }

    [Fact]
    public async Task AdditionalEpisodeRequestTruthfullyRejectsUntilHoldingExpansionHasItsOwnJournal() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedWantedSeriesAsync(db);
        var store = Requests(db);
        var accepted = await store.CreateAsync(fixture.Operation, fixture.Plan, default);
        await store.AcceptHoldingAsync(accepted, fixture.EmptySnapshot, fixture.ResolvedTargets, default);
        var seriesId = fixture.Operation.State.EntityId;
        var seasonId = (await db.Entities.AsNoTracking()
            .SingleAsync(row => row.Id == fixture.EpisodeIds[1])).ParentEntityId!.Value;
        var episodeId = Guid.NewGuid();
        db.Entities.Add(new() {
            Id = episodeId,
            ParentEntityId = seasonId,
            KindCode = EntityKind.VideoEpisode.ToCode(),
            Title = "Later",
            IsWanted = true
        });
        db.EntityPositions.AddRange(
            new() { EntityId = episodeId, Code = EntityPositionCodes.Season, Value = 1 },
            new() { EntityId = episodeId, Code = EntityPositionCodes.Episode, Value = 3 });
        db.EntityExternalIds.Add(new() {
            Id = Guid.NewGuid(),
            EntityId = episodeId,
            Provider = ExternalIdProviders.Tvdb,
            Value = "103"
        });
        await db.SaveChangesAsync();
        var target = await store.RequireTargetAsync(
            fixture.ConnectionId,
            seriesId,
            fixture.Operation.State.LibraryRootId,
            [episodeId],
            default);
        var operation = ManagedRequestOperation.Create(
            Guid.NewGuid(),
            fixture.ConnectionId,
            seriesId,
            fixture.Operation.State.LibraryRootId);
        var request = new CreateManagedRequestInput(
            operation.State.OperationId,
            seriesId,
            operation.State.LibraryRootId,
            target.Work,
            "profile",
            Monitored: true,
            Search: true,
            TargetEntityIds: [episodeId]);
        var plan = new ManagedRequestPlan(
            request,
            new(operation.State.OperationId, target.Work, "profile", "tv", "/series"),
            target.Title,
            ManagedRequestIdentity.Fingerprint(request));

        var error = await Assert.ThrowsAsync<ManagedRequestConflictException>(() =>
            store.CreateAsync(operation, plan, default));

        Assert.Contains("reviewed expansion", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await db.ManagedRequests.AsNoTracking().ToArrayAsync());
        Assert.DoesNotContain(await db.FulfillmentReservations.AsNoTracking().ToArrayAsync(),
            owner => owner.EntityId == episodeId);
    }

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
        var rooted = Assert.Single(await db.EntityLibraryRoots.ToArrayAsync());
        Assert.Equal(fixture.Fixture.EntityId, rooted.EntityId);
        Assert.Equal(fixture.Fixture.RootId, rooted.LibraryRootId);
        var rollup = await db.EntityRollups.AsNoTracking()
            .SingleAsync(row => row.EntityId == fixture.Fixture.EntityId);
        Assert.Equal(fixture.Fixture.RootId, rollup.EffectiveLibraryRootId);
        var visibility = new EfEntityLibraryVisibilityFilter(db, TestUserContext.Member());
        Assert.True(await visibility.RequiresCurrentUserVisibilityAsync(default));
        Assert.Empty(await visibility.ApplyCurrentUserVisibility(db.Entities.AsNoTracking())
            .Where(entity => entity.Id == fixture.Fixture.EntityId)
            .ToArrayAsync());
        Assert.Single(await db.JobRuns.ToArrayAsync());
        Assert.Empty(await db.ManagedHoldings.ToArrayAsync());
        Assert.Equal(accepted.Operation.State.OperationId, Assert.Single(await new LibraryScanPersistenceService(db).ListManagedHoldingsForRootAsync(fixture.Fixture.RootId, default)));
        db.Acquisitions.Add(new() { Id = Guid.NewGuid(), EntityId = owner.EntityId, Kind = EntityKind.Movie, Status = AcquisitionStatus.Pending });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ConfirmedRemovalArchivesFilelessWantedEntityAndRetainsIdentityAndOwnerFence() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedWantedAsync(db);
        var requests = Requests(db);
        var accepted = await requests.CreateAsync(fixture.Operation, fixture.Plan, default);
        await requests.AcceptHoldingAsync(accepted, fixture.Fixture.Snapshot, default);
        var tracking = Store(db);
        var work = (await tracking.FindAsync(accepted.Operation.State.OperationId, default))!;

        await tracking.ConfirmRemovalAsync(work, "Confirmed removed", default);

        var request = (await requests.FindAsync(accepted.Operation.State.OperationId, default))!;
        Assert.Equal(ManagedRequestPhase.RemoteRemoved, request.Operation.State.Phase);
        Assert.Equal("1", request.Operation.State.RemoteId);
        var holding = (await tracking.FindAsync(accepted.Operation.State.OperationId, default))!.Tracking;
        Assert.Equal(ManagedTrackingStatus.Removed, holding.Status);
        Assert.Equal("Confirmed removed", holding.Problem);
        var entity = await db.Entities.AsNoTracking().SingleAsync(row => row.Id == fixture.Fixture.EntityId);
        Assert.False(entity.IsWanted);
        Assert.True(entity.IsLibraryArchived);
        Assert.Empty(await db.EntityFiles.AsNoTracking().ToArrayAsync());
        Assert.Null(Assert.Single(await db.FulfillmentReservations.AsNoTracking().ToArrayAsync()).ReleasedAt);

        var removed = (await tracking.FindAsync(accepted.Operation.State.OperationId, default))!;
        Assert.Equal(ManagedTrackingStatus.Removed, removed.Tracking.Status);
        Assert.Equal(ManagedTrackingStatus.ReleasePending,
            (await Releases(db).BeginAsync(removed.Tracking.ConnectionId, removed.Tracking.Id, ReleaseIntent(removed), default)).Status);
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
    public async Task AtomicReviewedRequestQueueFailureRollsBackIntentOwnershipAndRootBinding() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using (var db = database.CreateContext()) {
            var fixture = await SeedWantedAsync(db);
            var connectionRevision = await db.IntegrationConnections
                .Where(connection => connection.Id == fixture.Fixture.ConnectionId)
                .Select(connection => connection.Revision)
                .SingleAsync();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_request_queue() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Simulated request publication failure'; END $$;
                CREATE TRIGGER reject_request_queue BEFORE INSERT ON job_runs FOR EACH ROW EXECUTE FUNCTION reject_request_queue();
                """);
            var scope = new EfReviewedManagedRequestCommitScope(db);
            await Assert.ThrowsAnyAsync<Exception>(() => scope.ExecuteAsync(
                fixture.Fixture.ConnectionId,
                connectionRevision,
                token => Requests(db).CreateAsync(fixture.Operation, fixture.Plan, token),
                default));
        }
        await using var check = database.CreateContext();
        Assert.Empty(await check.ManagedRequests.ToArrayAsync()); Assert.Empty(await check.FulfillmentReservations.ToArrayAsync()); Assert.Empty(await check.JobRuns.ToArrayAsync());
        Assert.Empty(await check.EntityLibraryRoots.ToArrayAsync());
    }

    [Fact]
    public async Task ReviewedCommitScopeRevisionFenceRunsBeforeLocalMutation() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedWantedAsync(db);
        var invoked = false;

        await Assert.ThrowsAsync<ConnectionConflictException>(() =>
            new EfReviewedManagedRequestCommitScope(db).ExecuteAsync(
                fixture.Fixture.ConnectionId,
                expectedConnectionRevision: long.MaxValue,
                _ => {
                    invoked = true;
                    return Task.FromResult(true);
                },
                default));

        Assert.False(invoked);
        Assert.Empty(await db.ManagedRequests.AsNoTracking().ToArrayAsync());
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

    [Fact]
    public async Task ReviewedAwaitingFilesRequestIsReobservedWithoutRetryingUncertainCreation() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedWantedAsync(db);
        var store = Requests(db);
        var accepted = await store.CreateAsync(fixture.Operation, fixture.Plan, default);
        await store.AcceptHoldingAsync(accepted, fixture.Fixture.Snapshot, default);
        var waiting = (await store.FindAsync(accepted.Operation.State.OperationId, default))!;
        var revision = waiting.Operation.State.Revision;
        waiting.Operation.RequireReview();
        await store.SaveAsync(waiting.Operation, revision, "Legacy untyped manager error", false, default);
        await db.JobGraphs.ExecuteDeleteAsync();
        await db.ManagedRequests.Where(row => row.Id == accepted.Operation.State.OperationId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(row => row.NextCheckAt, (DateTimeOffset?)null)
                .SetProperty(row => row.UpdatedAt, DateTimeOffset.UtcNow.AddMinutes(-2)));

        await store.QueueDueAsync(default);

        Assert.Single(await db.JobRuns.ToArrayAsync());
        Assert.Equal(ManagedRequestPhase.AwaitingFiles,
            (await store.FindAsync(accepted.Operation.State.OperationId, default))!.Operation.State.Phase);
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

    private async Task<WantedSeriesFixture> SeedWantedSeriesAsync(PrismediaDbContext db) {
        var connectionId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        var specialsId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var specialId = Guid.NewGuid();
        var pilotId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var directory = Directory.CreateDirectory(Path.Combine(workspace, "series"));
        var sharedPath = Path.Combine(directory.FullName, "shared.mkv");
        var secondPath = Path.Combine(directory.FullName, "second.mkv");
        await File.WriteAllBytesAsync(sharedPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(secondPath, [4, 5, 6, 7]);
        db.IntegrationConnections.Add(new() {
            Id = connectionId,
            PluginId = "fixture",
            Name = "Fixture",
            BaseUrl = "http://manager.test/",
            Enabled = true,
            Status = ConnectionStatus.Ready,
            Revision = 1
        });
        db.LibraryRoots.Add(new() {
            Id = rootId,
            Path = directory.FullName,
            Label = "Series",
            Enabled = true,
            ScanVideos = true
        });
        db.ExternalLibraryMounts.Add(new() {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            LibraryRootId = rootId,
            RemoteRootId = "tv",
            RemotePath = "/series",
            LocalPath = directory.FullName
        });
        db.Entities.AddRange(
            new() { Id = seriesId, KindCode = EntityKind.VideoSeries.ToCode(), Title = "Fixture Series" },
            new() { Id = specialsId, ParentEntityId = seriesId, KindCode = EntityKind.VideoSeason.ToCode(), Title = "Specials", IsWanted = true },
            new() { Id = seasonId, ParentEntityId = seriesId, KindCode = EntityKind.VideoSeason.ToCode(), Title = "Season 1", IsWanted = true },
            new() { Id = specialId, ParentEntityId = specialsId, KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Special", IsWanted = true },
            new() { Id = pilotId, ParentEntityId = seasonId, KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Pilot", IsWanted = true },
            new() { Id = secondId, ParentEntityId = seasonId, KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Second", IsWanted = true });
        db.EntityPositions.AddRange(
            new() { EntityId = specialsId, Code = EntityPositionCodes.Season, Value = 0 },
            new() { EntityId = seasonId, Code = EntityPositionCodes.Season, Value = 1 },
            new() { EntityId = specialId, Code = EntityPositionCodes.Season, Value = 0 },
            new() { EntityId = specialId, Code = EntityPositionCodes.Episode, Value = 1 },
            new() { EntityId = specialId, Code = EntityPositionCodes.AbsoluteEpisode, Value = 100 },
            new() { EntityId = pilotId, Code = EntityPositionCodes.Season, Value = 1 },
            new() { EntityId = pilotId, Code = EntityPositionCodes.Episode, Value = 1 },
            new() { EntityId = secondId, Code = EntityPositionCodes.Season, Value = 1 },
            new() { EntityId = secondId, Code = EntityPositionCodes.Episode, Value = 2 });
        db.EntityExternalIds.AddRange(
            new() { Id = Guid.NewGuid(), EntityId = seriesId, Provider = ExternalIdProviders.Tvdb, Value = "42" },
            new() { Id = Guid.NewGuid(), EntityId = specialId, Provider = ExternalIdProviders.Tvdb, Value = "900" },
            new() { Id = Guid.NewGuid(), EntityId = pilotId, Provider = ExternalIdProviders.Tvdb, Value = "101" },
            new() { Id = Guid.NewGuid(), EntityId = secondId, Provider = ExternalIdProviders.Tvdb, Value = "102" });
        await db.SaveChangesAsync();

        var targetIds = new[] { specialId, pilotId, secondId };
        var target = await Requests(db).RequireTargetAsync(connectionId, seriesId, rootId, targetIds, default);
        var operation = ManagedRequestOperation.Create(Guid.NewGuid(), connectionId, seriesId, rootId);
        var request = new CreateManagedRequestInput(
            operation.State.OperationId,
            seriesId,
            rootId,
            target.Work,
            "profile",
            Monitored: true,
            Search: true,
            TargetEntityIds: targetIds);
        var plan = new ManagedRequestPlan(
            request,
            new(operation.State.OperationId, target.Work, "profile", "tv", "/series"),
            target.Title,
            ManagedRequestIdentity.Fingerprint(request));
        var resolved = new[] {
            new ManagedResolvedTarget("900", EntityKind.VideoEpisode,
                new Dictionary<string, string> { [ExternalIdProviders.Tvdb] = "900" }, 0, 1),
            new ManagedResolvedTarget("101", EntityKind.VideoEpisode,
                new Dictionary<string, string> { [ExternalIdProviders.Tvdb] = "101" }, 1, 1),
            new ManagedResolvedTarget("102", EntityKind.VideoEpisode,
                new Dictionary<string, string> { [ExternalIdProviders.Tvdb] = "102" }, 1, 2)
        };
        var item = new ManagedLibraryItem(
            "series-remote",
            EntityKind.VideoSeries,
            target.Title,
            2026,
            target.Work.ExternalIds,
            false,
            "profile",
            0);
        var empty = new ManagedItemSnapshot(item, "/series/Fixture Series", [], DateTimeOffset.UtcNow);
        var shared = new ManagedLibraryFile(
            "shared",
            "/series/shared.mkv",
            3,
            null,
            [
                new("900", EntityKind.VideoEpisode, "Special", 0, 1),
                new("101", EntityKind.VideoEpisode, "Pilot", 1, 1)
            ]);
        var second = new ManagedLibraryFile(
            "second",
            "/series/second.mkv",
            4,
            null,
            [new("102", EntityKind.VideoEpisode, "Second", 1, 2)]);
        return new(
            connectionId,
            operation,
            plan,
            targetIds,
            resolved,
            empty,
            empty with { Files = [shared] },
            empty with { Files = [shared, second] });
    }

    private sealed record WantedSeriesFixture(
        Guid ConnectionId,
        ManagedRequestOperation Operation,
        ManagedRequestPlan Plan,
        IReadOnlyList<Guid> EpisodeIds,
        IReadOnlyList<ManagedResolvedTarget> ResolvedTargets,
        ManagedItemSnapshot EmptySnapshot,
        ManagedItemSnapshot PartialSnapshot,
        ManagedItemSnapshot CompleteSnapshot);
}
