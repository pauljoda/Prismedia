using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Requests;
using Xunit.Sdk;

namespace Prismedia.Infrastructure.Tests;

/// <summary>PostgreSQL row-lock regressions for parent discovery versus child lifecycle cleanup.</summary>
public sealed class EntityLifecycleConcurrencyPostgresTests {
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task EntityMutationLeaseCommitsBeforeACompetingDeletionClaimCanPublish() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var entityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var setup = database.CreateContext()) {
            setup.Entities.Add(new EntityRow {
                Id = entityId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "Serialized book",
                CreatedAt = now,
                UpdatedAt = now
            });
            await setup.SaveChangesAsync();
        }

        var leaseEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var mutationContext = database.CreateContext();
        var mutationLease = new EfEntityLifecycleMutationLease(
            mutationContext,
            new EfEntityHierarchyReader(mutationContext));
        var mutationTask = mutationLease.ExecuteAsync(
            entityId,
            async cancellationToken => {
                leaseEntered.TrySetResult();
                await releaseLease.Task.WaitAsync(cancellationToken);
                var entity = await mutationContext.Entities.SingleAsync(
                    row => row.Id == entityId,
                    cancellationToken);
                entity.Title = "Committed first";
                entity.UpdatedAt = DateTimeOffset.UtcNow;
                await mutationContext.SaveChangesAsync(cancellationToken);
            },
            CancellationToken.None);
        await leaseEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var operationId = Guid.NewGuid();
        await using var claimContext = database.CreateContext();
        var claimTask = claimContext.Entities
            .Where(row => row.Id == entityId && row.LifecycleClaimKind == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.LifecycleClaimKind, EntityLifecycleClaimKind.DeletingFiles)
                .SetProperty(row => row.LifecycleClaimId, operationId)
                .SetProperty(row => row.LifecycleClaimedAt, DateTimeOffset.UtcNow));
        Assert.NotSame(
            claimTask,
            await Task.WhenAny(claimTask, Task.Delay(TimeSpan.FromMilliseconds(200))));

        releaseLease.TrySetResult();
        Assert.True(await mutationTask.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, await claimTask.WaitAsync(TimeSpan.FromSeconds(10)));

        await using var verification = database.CreateContext();
        var row = await verification.Entities.AsNoTracking().SingleAsync();
        Assert.Equal("Committed first", row.Title);
        Assert.Equal(operationId, row.LifecycleClaimId);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ExistingDeletionClaimRejectsEntityMutationLeaseWithoutRunningCallback() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var entityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var setup = database.CreateContext()) {
            setup.Entities.Add(new EntityRow {
                Id = entityId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "Deleting book",
                LifecycleClaimKind = EntityLifecycleClaimKind.DeletingFiles,
                LifecycleClaimId = Guid.NewGuid(),
                LifecycleClaimedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
            await setup.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var callbackRan = false;
        var accepted = await new EfEntityLifecycleMutationLease(
                context,
                new EfEntityHierarchyReader(context))
            .ExecuteAsync(
                entityId,
                _ => {
                    callbackRan = true;
                    return Task.CompletedTask;
                },
                CancellationToken.None);

        Assert.False(accepted);
        Assert.False(callbackRan);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task FailedRecoveryClaimRequiresTheExactSelectedReleaseOnPostgres() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var acquisitionId = Guid.NewGuid();
        var selected = new SelectedRelease("Dune release", "Indexer", "hash-1");
        var now = DateTimeOffset.UtcNow;
        await using (var setup = database.CreateContext()) {
            setup.Acquisitions.Add(new AcquisitionRow {
                Id = acquisitionId,
                Status = AcquisitionStatus.Downloading,
                Title = "Dune",
                SelectedReleaseJson = System.Text.Json.JsonSerializer.Serialize(selected),
                ExternalIdsJson = "{}",
                SourceUrlsJson = "[]",
                CreatedAt = now,
                UpdatedAt = now
            });
            await setup.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var store = AcquisitionTestFactory.Store(context);
        Assert.False(await store.TryClaimFailedRecoveryAsync(
            acquisitionId,
            [AcquisitionStatus.Queued, AcquisitionStatus.Downloading],
            selected with { InfoHash = "different-hash" },
            "Failed.",
            CancellationToken.None));
        Assert.True(await store.TryClaimFailedRecoveryAsync(
            acquisitionId,
            [AcquisitionStatus.Queued, AcquisitionStatus.Downloading],
            selected,
            "Failed.",
            CancellationToken.None));
        Assert.Equal(AcquisitionStatus.Failed, await store.GetStatusAsync(acquisitionId, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task CancellationCommittedFirstPreventsStaleSearchCandidatePublication() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var acquisitionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var setup = database.CreateContext()) {
            setup.Acquisitions.Add(new AcquisitionRow {
                Id = acquisitionId,
                Status = AcquisitionStatus.Searching,
                Title = "Dune",
                ExternalIdsJson = "{}",
                SourceUrlsJson = "[]",
                CreatedAt = now,
                UpdatedAt = now
            });
            setup.ReleaseCandidates.Add(new ReleaseCandidateRow {
                Id = Guid.NewGuid(),
                AcquisitionId = acquisitionId,
                IndexerName = "Old indexer",
                Title = "Old release",
                InfoHash = "old-hash",
                Accepted = true,
                Score = 1,
                Protocol = DownloadProtocol.Torrent,
                RejectionsJson = "[]",
                CreatedAt = now
            });
            await setup.SaveChangesAsync();
        }

        await using var cancellationContext = database.CreateContext();
        await using var cancellationTransaction =
            await cancellationContext.Database.BeginTransactionAsync();
        Assert.Equal(1, await cancellationContext.Acquisitions
            .Where(row => row.Id == acquisitionId && row.Status == AcquisitionStatus.Searching)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, AcquisitionStatus.Cancelled)
                .SetProperty(row => row.StatusMessage, "Cancelled.")));

        await using var searchContext = database.CreateContext();
        var searchTask = AcquisitionTestFactory.Store(searchContext).TryCompleteSearchAsync(
            acquisitionId,
            [Scored("New release", "new-hash")],
            "1 acceptable release.",
            CancellationToken.None);
        Assert.NotSame(
            searchTask,
            await Task.WhenAny(searchTask, Task.Delay(TimeSpan.FromMilliseconds(200))));

        await cancellationTransaction.CommitAsync();
        Assert.False(await searchTask.WaitAsync(TimeSpan.FromSeconds(10)));

        await using var verification = database.CreateContext();
        var acquisition = await verification.Acquisitions.AsNoTracking().SingleAsync();
        Assert.Equal(AcquisitionStatus.Cancelled, acquisition.Status);
        Assert.Equal("Cancelled.", acquisition.StatusMessage);
        var candidate = await verification.ReleaseCandidates.AsNoTracking().SingleAsync();
        Assert.Equal("Old release", candidate.Title);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ParentSyncLeaseFirstThenChildOffLeavesNoProviderPhantom() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var parentMonitorId = Guid.NewGuid();
        var childMonitorId = Guid.NewGuid();
        var childIdentity = new ExternalIdentity("musicbrainz", "release-1");
        await using (var setup = database.CreateContext()) {
            setup.Entities.AddRange(
                new EntityRow {
                    Id = parentId,
                    KindCode = EntityKind.MusicArtist.ToCode(),
                    Title = "Artist",
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new EntityRow {
                    Id = childId,
                    ParentEntityId = parentId,
                    KindCode = EntityKind.AudioLibrary.ToCode(),
                    Title = "Album",
                    CreatedAt = now,
                    UpdatedAt = now
                });
            setup.EntityExternalIds.Add(new EntityExternalIdRow {
                Id = Guid.NewGuid(),
                EntityId = childId,
                Provider = childIdentity.Namespace,
                Value = childIdentity.Value,
                CreatedAt = now,
                UpdatedAt = now
            });
            setup.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = childId,
                Role = EntityFileRole.Source,
                Path = "/media/artist/album/track.flac",
                CreatedAt = now,
                UpdatedAt = now
            });
            setup.Monitors.AddRange(
                new MonitorRow {
                    Id = parentMonitorId,
                    EntityId = parentId,
                    Kind = EntityKind.MusicArtist,
                    Status = MonitorStatus.Active,
                    Title = "Artist",
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new MonitorRow {
                    Id = childMonitorId,
                    EntityId = childId,
                    Kind = EntityKind.AudioLibrary,
                    Status = MonitorStatus.Active,
                    Title = "Album",
                    CreatedAt = now,
                    UpdatedAt = now
                });
            await setup.SaveChangesAsync();
        }

        EntityUnmonitorScope scope;
        await using (var preflight = database.CreateContext()) {
            var persistence = new EfEntityUnmonitorPersistence(
                preflight,
                new EfEntityHierarchyReader(preflight));
            scope = (await persistence.ResolveAsync(childMonitorId, CancellationToken.None))!;
        }

        var leaseEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var phantomId = Guid.NewGuid();
        await using var syncContext = database.CreateContext();
        var syncStore = new EfMonitorStore(syncContext, new EfEntityHierarchyReader(syncContext));
        var syncTask = syncStore.ExecuteIfActiveEntityMutationAsync(
            parentId,
            async cancellationToken => {
                leaseEntered.TrySetResult();
                await releaseLease.Task.WaitAsync(cancellationToken);
                syncContext.Entities.Add(new EntityRow {
                    Id = phantomId,
                    ParentEntityId = childId,
                    KindCode = EntityKind.AudioTrack.ToCode(),
                    Title = "Provider phantom",
                    IsWanted = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                await syncContext.SaveChangesAsync(cancellationToken);
            },
            CancellationToken.None);
        await leaseEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using var unmonitorContext = database.CreateContext();
        var unmonitoring = new EfEntityUnmonitorPersistence(
            unmonitorContext,
            new EfEntityHierarchyReader(unmonitorContext));
        var claimTask = unmonitoring.ClaimAsync(scope, CancellationToken.None);
        var premature = await Task.WhenAny(claimTask, Task.Delay(TimeSpan.FromMilliseconds(200)));
        Assert.NotSame(claimTask, premature);

        releaseLease.TrySetResult();
        Assert.True(await syncTask.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(await claimTask.WaitAsync(TimeSpan.FromSeconds(10)));

        // Claim returned only after the same transaction committed both Stopping and the root suppression.
        await using (var observer = database.CreateContext()) {
            var suppressed = await new EfWantedSuppressionStore(observer)
                .FilterSuppressedAsync([childIdentity], CancellationToken.None);
            Assert.Contains(childIdentity, suppressed);
        }

        await unmonitoring.CompleteAsync(scope, CancellationToken.None);

        await using var verification = database.CreateContext();
        Assert.NotNull(await verification.Entities.FindAsync(parentId));
        Assert.NotNull(await verification.Entities.FindAsync(childId));
        Assert.Null(await verification.Entities.FindAsync(phantomId));
        Assert.NotNull(await verification.Monitors.FindAsync(parentMonitorId));
        Assert.Null(await verification.Monitors.FindAsync(childMonitorId));
        Assert.Single(await verification.WantedSuppressions.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ImportClaimCommittedFirstPreventsAStaleUnmonitorClaim() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var entityId = Guid.NewGuid();
        var acquisitionId = Guid.NewGuid();
        var monitorId = Guid.NewGuid();
        await using (var setup = database.CreateContext()) {
            setup.Entities.Add(new EntityRow {
                Id = entityId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "Downloaded book",
                IsWanted = true,
                CreatedAt = now,
                UpdatedAt = now
            });
            setup.EntityExternalIds.Add(new EntityExternalIdRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Provider = "openlibrary",
                Value = "OL1W",
                CreatedAt = now,
                UpdatedAt = now
            });
            setup.Acquisitions.Add(new AcquisitionRow {
                Id = acquisitionId,
                EntityId = entityId,
                Status = AcquisitionStatus.Downloaded,
                Title = "Downloaded book",
                ExternalIdsJson = "{}",
                SourceUrlsJson = "[]",
                CreatedAt = now,
                UpdatedAt = now
            });
            setup.Monitors.Add(new MonitorRow {
                Id = monitorId,
                EntityId = entityId,
                AcquisitionId = acquisitionId,
                Kind = EntityKind.Book,
                Status = MonitorStatus.Active,
                Title = "Downloaded book",
                CreatedAt = now,
                UpdatedAt = now
            });
            await setup.SaveChangesAsync();
        }

        EntityUnmonitorScope scope;
        await using (var preflight = database.CreateContext()) {
            var persistence = new EfEntityUnmonitorPersistence(
                preflight,
                new EfEntityHierarchyReader(preflight));
            scope = (await persistence.ResolveAsync(monitorId, CancellationToken.None))!;
            Assert.Equal(AcquisitionStatus.Downloaded, scope.AcquisitionStatuses?[acquisitionId]);
        }

        var importClaimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseImport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var importContext = database.CreateContext();
        var importStore = AcquisitionTestFactory.Store(importContext);
        var importLease = new EfEntityLifecycleMutationLease(
            importContext,
            new EfEntityHierarchyReader(importContext));
        var importTask = importLease.ExecuteAsync(
            entityId,
            async cancellationToken => {
                Assert.True(await importStore.TryTransitionStatusAsync(
                    acquisitionId,
                    [AcquisitionStatus.Downloaded],
                    AcquisitionStatus.Importing,
                    "Importing.",
                    cancellationToken));
                importClaimed.TrySetResult();
                await releaseImport.Task.WaitAsync(cancellationToken);
            },
            CancellationToken.None);
        await importClaimed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using var unmonitorContext = database.CreateContext();
        var unmonitoring = new EfEntityUnmonitorPersistence(
            unmonitorContext,
            new EfEntityHierarchyReader(unmonitorContext));
        var eligibilityRechecked = false;
        var claimTask = unmonitoring.ClaimAsync(
            scope,
            CancellationToken.None,
            _ => {
                eligibilityRechecked = true;
                return Task.FromResult(true);
            });
        Assert.NotSame(
            claimTask,
            await Task.WhenAny(claimTask, Task.Delay(TimeSpan.FromMilliseconds(200))));

        releaseImport.TrySetResult();
        Assert.True(await importTask.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(await claimTask.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(eligibilityRechecked);

        await using var verification = database.CreateContext();
        Assert.Equal(
            AcquisitionStatus.Importing,
            (await verification.Acquisitions.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(
            MonitorStatus.Active,
            (await verification.Monitors.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await verification.WantedSuppressions.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task UnmonitorClaimCommittedFirstRejectsPublicEntityBoundAcquisitionCreation() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var entityId = Guid.NewGuid();
        var monitorId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var setup = database.CreateContext()) {
            setup.Entities.Add(new EntityRow {
                Id = entityId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "Wanted book",
                IsWanted = true,
                CreatedAt = now,
                UpdatedAt = now
            });
            setup.Monitors.Add(new MonitorRow {
                Id = monitorId,
                EntityId = entityId,
                Kind = EntityKind.Book,
                Status = MonitorStatus.Active,
                Title = "Wanted book",
                CreatedAt = now,
                UpdatedAt = now
            });
            await setup.SaveChangesAsync();
        }

        EntityUnmonitorScope scope;
        await using (var preflight = database.CreateContext()) {
            scope = (await new EfEntityUnmonitorPersistence(
                preflight,
                new EfEntityHierarchyReader(preflight))
                .ResolveAsync(monitorId, CancellationToken.None))!;
        }

        var suppression = new PausingSuppressionStore();
        await using var unmonitorContext = database.CreateContext();
        var unmonitoring = new EfEntityUnmonitorPersistence(
            unmonitorContext,
            new EfEntityHierarchyReader(unmonitorContext),
            suppressionStore: suppression);
        var claimTask = unmonitoring.ClaimAsync(scope, CancellationToken.None);
        await suppression.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var queue = new PausingJobQueue(pause: false);
        await using var createContext = database.CreateContext();
        var service = CreateAcquisitionService(createContext, queue);
        var createTask = service.CreateAndSearchAsync(
            new AcquisitionCreateRequest(
                "Wanted book", null, null, null, null, null, null,
                Kind: EntityKind.Book,
                EntityId: entityId),
            CancellationToken.None);
        Assert.NotSame(
            createTask,
            await Task.WhenAny(createTask, Task.Delay(TimeSpan.FromMilliseconds(200))));

        suppression.Release.TrySetResult();
        Assert.True(await claimTask.WaitAsync(TimeSpan.FromSeconds(10)));
        await Assert.ThrowsAsync<AcquisitionConfigurationException>(async () =>
            await createTask.WaitAsync(TimeSpan.FromSeconds(10)));

        await using var verification = database.CreateContext();
        Assert.Empty(await verification.Acquisitions.AsNoTracking().ToArrayAsync());
        Assert.Equal(
            MonitorStatus.Stopping,
            (await verification.Monitors.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task PublicEntityBoundAcquisitionCreationCommittedFirstInvalidatesStaleUnmonitorScope() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var entityId = Guid.NewGuid();
        var monitorId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var setup = database.CreateContext()) {
            setup.Entities.Add(new EntityRow {
                Id = entityId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "Wanted book",
                IsWanted = true,
                CreatedAt = now,
                UpdatedAt = now
            });
            setup.Monitors.Add(new MonitorRow {
                Id = monitorId,
                EntityId = entityId,
                Kind = EntityKind.Book,
                Status = MonitorStatus.Active,
                Title = "Wanted book",
                CreatedAt = now,
                UpdatedAt = now
            });
            await setup.SaveChangesAsync();
        }

        EntityUnmonitorScope scope;
        await using (var preflight = database.CreateContext()) {
            scope = (await new EfEntityUnmonitorPersistence(
                preflight,
                new EfEntityHierarchyReader(preflight))
                .ResolveAsync(monitorId, CancellationToken.None))!;
        }

        var queue = new PausingJobQueue(pause: true);
        await using var createContext = database.CreateContext();
        var service = CreateAcquisitionService(createContext, queue);
        var createTask = service.CreateAndSearchAsync(
            new AcquisitionCreateRequest(
                "Wanted book", null, null, null, null, null, null,
                Kind: EntityKind.Book,
                EntityId: entityId),
            CancellationToken.None);
        await queue.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using var unmonitorContext = database.CreateContext();
        var unmonitoring = new EfEntityUnmonitorPersistence(
            unmonitorContext,
            new EfEntityHierarchyReader(unmonitorContext));
        var claimTask = unmonitoring.ClaimAsync(scope, CancellationToken.None);
        Assert.NotSame(
            claimTask,
            await Task.WhenAny(claimTask, Task.Delay(TimeSpan.FromMilliseconds(200))));

        queue.Release.TrySetResult();
        var created = await createTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(AcquisitionStatus.Searching, created.Status);
        Assert.False(await claimTask.WaitAsync(TimeSpan.FromSeconds(10)));

        await using var verification = database.CreateContext();
        var acquisition = await verification.Acquisitions.AsNoTracking().SingleAsync();
        Assert.Equal(entityId, acquisition.EntityId);
        Assert.Equal(AcquisitionStatus.Searching, acquisition.Status);
        Assert.Equal(
            MonitorStatus.Active,
            (await verification.Monitors.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(JobType.AcquisitionSearch, Assert.Single(queue.Enqueued).Type);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task MonitorlessGiveUpClaimFirstRejectsAWaitingExplicitRequest() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var entityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var setup = database.CreateContext()) {
            setup.Entities.Add(new EntityRow {
                Id = entityId,
                KindCode = EntityKind.Book.ToCode(),
                Title = "Wanted book",
                IsWanted = true,
                CreatedAt = now,
                UpdatedAt = now
            });
            setup.EntityExternalIds.Add(new EntityExternalIdRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Provider = "openlibrary",
                Value = "OL1W",
                CreatedAt = now,
                UpdatedAt = now
            });
            await setup.SaveChangesAsync();
        }

        EntityUnmonitorScope scope;
        await using (var preflight = database.CreateContext()) {
            var persistence = new EfEntityUnmonitorPersistence(
                preflight,
                new EfEntityHierarchyReader(preflight));
            scope = (await persistence.ResolveForEntityAsync(entityId, CancellationToken.None))!;
            Assert.True(scope.SyntheticMonitorAnchor);
        }

        var suppression = new PausingSuppressionStore();
        await using var claimContext = database.CreateContext();
        var persistenceUnderClaim = new EfEntityUnmonitorPersistence(
            claimContext,
            new EfEntityHierarchyReader(claimContext),
            suppressionStore: suppression);
        var claimTask = persistenceUnderClaim.ClaimAsync(scope, CancellationToken.None);
        await suppression.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var explicitMutationRan = false;
        await using var explicitContext = database.CreateContext();
        var monitorStore = new EfMonitorStore(
            explicitContext,
            new EfEntityHierarchyReader(explicitContext));
        var explicitTask = monitorStore.ExecuteIfEntityLifecycleMutableAsync(
            entityId,
            _ => {
                explicitMutationRan = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);
        var premature = await Task.WhenAny(explicitTask, Task.Delay(TimeSpan.FromMilliseconds(200)));
        Assert.NotSame(explicitTask, premature);

        suppression.Release.TrySetResult();
        Assert.True(await claimTask.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(await explicitTask.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(explicitMutationRan);

        await using var verification = database.CreateContext();
        var anchor = await verification.Monitors.AsNoTracking().SingleAsync();
        Assert.Equal(scope.MonitorId, anchor.Id);
        Assert.Equal(MonitorStatus.Stopping, anchor.Status);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task TransferAddLeaseCommitsThePointerBeforeTeardownCanClaimTheOwner() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var acquisitionId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var setup = database.CreateContext()) {
            setup.DownloadClientConfigs.Add(new DownloadClientConfigRow {
                Id = clientId,
                Kind = DownloadClientKind.QBittorrent,
                DisplayName = "qBittorrent",
                BaseUrl = "http://download.test",
                Category = "prismedia",
                CreatedAt = now,
                UpdatedAt = now
            });
            setup.Acquisitions.Add(new AcquisitionRow {
                Id = acquisitionId,
                Status = AcquisitionStatus.Queued,
                Title = "Arrival",
                ExternalIdsJson = "{}",
                SourceUrlsJson = "[]",
                CreatedAt = now,
                UpdatedAt = now
            });
            await setup.SaveChangesAsync();
        }

        await using var addContext = database.CreateContext();
        var addStore = AcquisitionTestFactory.Store(addContext);
        Assert.True(await addStore.BeginTransferAddAsync(
            acquisitionId,
            clientId,
            "arrival-release",
            "prismedia",
            seedGoal: null,
            CancellationToken.None));
        await using var lease = await new EfAcquisitionTransferAddCoordinator(addContext)
            .AcquireAsync(acquisitionId, CancellationToken.None);
        Assert.NotNull(lease);

        await using var teardownContext = database.CreateContext();
        var teardownStore = AcquisitionTestFactory.Store(teardownContext);
        var claimTask = teardownStore.TryClaimTeardownAsync(
            acquisitionId,
            AcquisitionStatus.Queued,
            AcquisitionTeardownIntent.Remove,
            "Removing acquisition.",
            CancellationToken.None);
        var premature = await Task.WhenAny(claimTask, Task.Delay(TimeSpan.FromMilliseconds(200)));
        Assert.NotSame(claimTask, premature);

        Assert.True(await addStore.CompleteTransferAddAsync(
            acquisitionId,
            clientId,
            "arrival-release",
            "client-item",
            new SelectedRelease("Arrival release", "Indexer", "arrival-release"),
            "Sent to download client.",
            CancellationToken.None));
        await lease!.CommitAsync(CancellationToken.None);

        Assert.True(await claimTask.WaitAsync(TimeSpan.FromSeconds(10)));
        await using var verification = database.CreateContext();
        var pointer = await verification.DownloadTransfers.AsNoTracking().SingleAsync();
        Assert.Equal((acquisitionId, clientId, "client-item"),
            (pointer.AcquisitionId, pointer.DownloadClientConfigId, pointer.ClientItemId));
        Assert.Null(pointer.State);
        Assert.Equal(
            "Arrival release",
            (await AcquisitionTestFactory.Store(verification)
                .GetSelectedReleaseAsync(acquisitionId, CancellationToken.None))?.Title);
        Assert.Equal(
            AcquisitionStatus.Stopping,
            (await verification.Acquisitions.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ProcessCrashAfterRemoteAddLeavesDurableCorrelationForTeardownRecovery() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var acquisitionId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var setup = database.CreateContext()) {
            setup.DownloadClientConfigs.Add(new DownloadClientConfigRow {
                Id = clientId,
                Kind = DownloadClientKind.QBittorrent,
                DisplayName = "qBittorrent",
                BaseUrl = "http://download.test",
                Category = "prismedia",
                CreatedAt = now,
                UpdatedAt = now
            });
            setup.Acquisitions.Add(new AcquisitionRow {
                Id = acquisitionId,
                Status = AcquisitionStatus.Queued,
                Title = "Dune",
                ExternalIdsJson = "{}",
                SourceUrlsJson = "[]",
                CreatedAt = now,
                UpdatedAt = now
            });
            await setup.SaveChangesAsync();
        }

        await using var addContext = database.CreateContext();
        var addStore = AcquisitionTestFactory.Store(addContext);
        Assert.True(await addStore.BeginTransferAddAsync(
            acquisitionId,
            clientId,
            "Dune Release",
            "prismedia",
            seedGoal: null,
            CancellationToken.None));
        var lease = await new EfAcquisitionTransferAddCoordinator(addContext)
            .AcquireAsync(acquisitionId, CancellationToken.None);
        Assert.NotNull(lease);

        await using var teardownContext = database.CreateContext();
        var claimTask = AcquisitionTestFactory.Store(teardownContext).TryClaimTeardownAsync(
            acquisitionId,
            AcquisitionStatus.Queued,
            AcquisitionTeardownIntent.Remove,
            "Removing acquisition.",
            CancellationToken.None);
        Assert.NotSame(
            claimTask,
            await Task.WhenAny(claimTask, Task.Delay(TimeSpan.FromMilliseconds(200))));

        // Simulate a process dying after the external client accepted the Add but before the native id
        // could replace the correlation: transaction/row lock disappears, durable pre-Add ownership stays.
        await lease!.DisposeAsync();
        Assert.True(await claimTask.WaitAsync(TimeSpan.FromSeconds(10)));

        await using var verification = database.CreateContext();
        var pointer = await verification.DownloadTransfers.AsNoTracking().SingleAsync();
        Assert.Equal((acquisitionId, clientId, "Dune Release", TransferOwnershipState.Adding.ToCode()),
            (pointer.AcquisitionId, pointer.DownloadClientConfigId, pointer.ClientItemId, pointer.State));
        Assert.Equal(
            AcquisitionStatus.Stopping,
            (await verification.Acquisitions.AsNoTracking().SingleAsync()).Status);
    }

    private sealed class PausingSuppressionStore : IWantedSuppressionStore {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SuppressAsync(
            IReadOnlyList<ExternalIdentity> identities,
            EntityKind kind,
            string title,
            CancellationToken cancellationToken) {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }

        public Task<IReadOnlySet<ExternalIdentity>> FilterSuppressedAsync(
            IReadOnlyList<ExternalIdentity> identities,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<ExternalIdentity>>(new HashSet<ExternalIdentity>());

        public Task ClearAsync(
            IReadOnlyList<ExternalIdentity> identities,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static AcquisitionService CreateAcquisitionService(
        PrismediaDbContext db,
        IJobQueueService queue) {
        var hierarchy = new EfEntityHierarchyReader(db);
        var lifecycle = new EfEntityLifecycleMutationLease(db, hierarchy);
        return new AcquisitionService(
            AcquisitionTestFactory.Store(db),
            new ThrowingBlocklistStore(),
            queue,
            new MergedImportTestSupport.ThrowingClientConfigStore(),
            new MergedImportTestSupport.ThrowingClientFactory(),
            new EmptyImportedFilesReader(),
            new EfAcquisitionHistoryStore(db),
            NullLogger<AcquisitionService>.Instance,
            new EfMonitorStore(db, hierarchy, lifecycle),
            new AcquisitionJobCleanup(db),
            lifecycle);
    }

    private sealed class ThrowingBlocklistStore : IAcquisitionBlocklistStore {
        public Task<IReadOnlySet<string>> GetIdentitiesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task AddAsync(BlocklistAddRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<AcquisitionBlocklistEntry>> ListAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyImportedFilesReader : IImportedFilesReader {
        public IReadOnlyList<DownloadItemFile> List(string path) => [];
    }

    private sealed class PausingJobQueue(bool pause) : IJobQueueService {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<EnqueueJobRequest> Enqueued { get; } = [];

        public async Task<JobRunSnapshot> EnqueueAsync(
            EnqueueJobRequest request,
            CancellationToken cancellationToken) {
            if (pause) {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            Enqueued.Add(request);
            var now = DateTimeOffset.UtcNow;
            return new JobRunSnapshot(
                Guid.NewGuid(), request.Type, JobRunStatus.Queued, 0, null,
                request.PayloadJson ?? "{}", request.TargetEntityKind, request.TargetEntityId,
                request.TargetLabel, now, null, null);
        }

        public Task<bool> HasPendingAsync(
            JobType type,
            string? targetEntityId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Enqueued.Any(request =>
                request.Type == type && request.TargetEntityId == targetEntityId));

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken, JobRunLane? lane = null) => throw new NotSupportedException();
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static ScoredRelease Scored(string title, string infoHash) =>
        new(
            new IndexerRelease(
                title,
                1_000,
                10,
                2,
                DownloadProtocol.Torrent,
                "https://indexer.test/download",
                null,
                infoHash,
                null,
                null,
                DateTimeOffset.UtcNow),
            Guid.NewGuid(),
            "Indexer",
            Accepted: true,
            Score: 100,
            Rejections: []);

    private sealed class PostgresTestDatabase(
        string databaseName,
        string adminConnectionString,
        string connectionString) : IAsyncDisposable {
        public PrismediaDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<PrismediaDbContext>()
                .UseNpgsql(connectionString)
                .Options);

        public static async Task<PostgresTestDatabase> CreateAsync() {
            var configured = Environment.GetEnvironmentVariable("PRISMEDIA_TEST_DATABASE_URL")
                ?? "Host=localhost;Port=5432;Database=postgres;Username=prismedia;Password=prismedia";
            var adminBuilder = new NpgsqlConnectionStringBuilder(configured) {
                Database = "postgres",
                Pooling = false
            };
            try {
                await using var probe = new NpgsqlConnection(adminBuilder.ConnectionString);
                await probe.OpenAsync();
            } catch (Exception exception) when (exception is NpgsqlException or TimeoutException) {
                throw SkipException.ForSkip(
                    $"PostgreSQL lifecycle race test requires PRISMEDIA_TEST_DATABASE_URL or the local dev database: {exception.Message}");
            }

            var name = $"prismedia_race_{Guid.NewGuid():N}";
            await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString)) {
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
                await create.ExecuteNonQueryAsync();
            }

            var testBuilder = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString) {
                Database = name,
                Pooling = false
            };
            var database = new PostgresTestDatabase(
                name,
                adminBuilder.ConnectionString,
                testBuilder.ConnectionString);
            try {
                await using var context = database.CreateContext();
                await context.Database.MigrateAsync();
                return database;
            } catch {
                await database.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync() {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(adminConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)",
                admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
