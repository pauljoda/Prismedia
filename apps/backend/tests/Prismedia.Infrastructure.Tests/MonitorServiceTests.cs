using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Plugins;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Exercises MonitorService over the real EF stores: start (idempotent, 404), pause/resume/stop guards,
/// and the plugin-trackability gate on entity monitoring/eligibility.
/// </summary>
public sealed class MonitorServiceTests {
    [Fact]
    public async Task StartReturnsNullWhenAcquisitionDoesNotExist() {
        await using var db = CreateContext();
        var service = Service(db);

        Assert.Null(await service.StartAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task StartIsIdempotentAndDenormalizesTitleAuthorFromTheAcquisition() {
        await using var db = CreateContext();
        var acquisitionId = SeedAcquisition(db, "The Anxious Generation", "Jonathan Haidt");
        await db.SaveChangesAsync();
        var service = Service(db);

        var first = await service.StartAsync(acquisitionId, CancellationToken.None);
        var second = await service.StartAsync(acquisitionId, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(MonitorStatus.Active, second.Status);
        Assert.Equal("The Anxious Generation", second.Title);
        Assert.Equal("Jonathan Haidt", second.Author);
        Assert.Single(await service.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PauseResumeStopReturnFalseForUnknownMonitor() {
        await using var db = CreateContext();
        var service = Service(db);
        var unknown = Guid.NewGuid();

        Assert.False(await service.PauseAsync(unknown, CancellationToken.None));
        Assert.False(await service.ResumeAsync(unknown, CancellationToken.None));
        Assert.False((await service.StopAsync(unknown, CancellationToken.None)).Found);
    }

    [Fact]
    public async Task PauseThenResumeTogglesStatus() {
        await using var db = CreateContext();
        var acquisitionId = SeedAcquisition(db, "Book", null);
        await db.SaveChangesAsync();
        var service = Service(db);
        var monitor = await service.StartAsync(acquisitionId, CancellationToken.None);

        Assert.True(await service.PauseAsync(monitor!.Id, CancellationToken.None));
        Assert.Equal(MonitorStatus.Paused, (await service.ListAsync(CancellationToken.None))[0].Status);
        Assert.True(await service.ResumeAsync(monitor.Id, CancellationToken.None));
        Assert.Equal(MonitorStatus.Active, (await service.ListAsync(CancellationToken.None))[0].Status);
        Assert.True((await service.StopAsync(monitor.Id, CancellationToken.None)).Stopped);
        Assert.Empty(await service.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StoppingMonitorCannotBeResumedOrReactivated() {
        await using var db = CreateContext();
        var acquisitionId = SeedAcquisition(db, "Book", null);
        var entityId = Guid.NewGuid();
        db.Acquisitions.Local.Single(row => row.Id == acquisitionId).EntityId = entityId;
        var monitorId = Guid.NewGuid();
        db.Monitors.Add(new MonitorRow {
            Id = monitorId,
            AcquisitionId = acquisitionId,
            Kind = EntityKind.Book,
            Status = MonitorStatus.Stopping,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);

        Assert.False(await store.SetStatusAsync(monitorId, MonitorStatus.Active, CancellationToken.None));
        Assert.False(await store.SetStatusAsync(monitorId, MonitorStatus.Paused, CancellationToken.None));
        await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            store.StartAsync(acquisitionId, EntityKind.Book, "Changed title", null, CancellationToken.None));
        await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            store.StartForEntityAsync(entityId, EntityKind.Book, "Changed title", null, null, CancellationToken.None));

        var stopping = (await db.Monitors.FindAsync(monitorId))!;
        Assert.Equal(MonitorStatus.Stopping, stopping.Status);
        Assert.Null(stopping.EntityId);
        Assert.Equal(acquisitionId, stopping.AcquisitionId);
    }

    [Fact]
    public async Task EligibilityRequiresATrackableProvider() {
        await using var db = CreateContext();
        var trackedId = SeedContainerEntity(db, "Brandon Sanderson", provider: " OpenLibrary ");
        var orphanId = SeedContainerEntity(db, "Unknown Author", provider: "dead-provider");
        await db.SaveChangesAsync();
        var service = Service(db, trackableProviders: ["openlibrary"]);

        var tracked = await service.GetEligibilityAsync(trackedId, CancellationToken.None);
        Assert.True(tracked.CanMonitor);
        Assert.True(tracked.DiscoversChildren);
        Assert.True(tracked.CanSearchMissingChildren);
        Assert.Equal(EntityKind.Book, tracked.MissingChildEntityKind);
        Assert.Equal(["openlibrary"], tracked.TrackableProviders);

        var orphan = await service.GetEligibilityAsync(orphanId, CancellationToken.None);
        Assert.False(orphan.CanMonitor);
        Assert.True(orphan.DiscoversChildren);
        Assert.True(orphan.CanSearchMissingChildren);
        Assert.Equal(EntityKind.Book, orphan.MissingChildEntityKind);
        Assert.Empty(orphan.TrackableProviders);
    }

    [Fact]
    public async Task EligibilitySurfacesTheAuthoritativePluginId() {
        await using var db = CreateContext();
        var entityId = SeedContainerEntity(db, "Bluey", provider: "tmdb");
        var now = DateTimeOffset.UtcNow;
        db.EntityProviderIdentities.Add(new EntityProviderIdentityRow {
            EntityId = entityId,
            PluginId = "cinema-metadata",
            IdentityNamespace = "tmdb",
            IdentityValue = "id-1",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var service = Service(db, trackableProviders: ["cinema-metadata"]);

        var eligibility = await service.GetEligibilityAsync(entityId, CancellationToken.None);

        Assert.True(eligibility.CanMonitor);
        Assert.Equal(["cinema-metadata"], eligibility.TrackableProviders);
    }

    [Fact]
    public async Task EligibilityDoesNotTreatAStaleBindingAsAnUnboundLegacyEntity() {
        await using var db = CreateContext();
        var entityId = SeedContainerEntity(db, "Bluey", provider: "tmdb");
        var now = DateTimeOffset.UtcNow;
        db.EntityProviderIdentities.Add(new EntityProviderIdentityRow {
            EntityId = entityId,
            PluginId = "tmdb",
            IdentityNamespace = "tmdb",
            IdentityValue = "id-1",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        (await db.EntityExternalIds.SingleAsync(row => row.EntityId == entityId)).Value = "id-2";
        await db.SaveChangesAsync();
        var service = Service(db, trackableProviders: ["tmdb"]);

        var eligibility = await service.GetEligibilityAsync(entityId, CancellationToken.None);

        Assert.False(eligibility.CanMonitor);
        Assert.Empty(eligibility.TrackableProviders);
    }

    [Fact]
    public async Task EligibilitySeparatesBookChildSearchFromAnAlbumWithNoRequestableChildKind() {
        await using var db = CreateContext();
        var bookId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.AddRange(
            new EntityRow { Id = bookId, KindCode = EntityKindRegistry.Book.Code, Title = "A Book", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = albumId, KindCode = EntityKindRegistry.AudioLibrary.Code, Title = "An Album", CreatedAt = now, UpdatedAt = now });
        db.EntityExternalIds.AddRange(
            new EntityExternalIdRow { Id = Guid.NewGuid(), EntityId = bookId, Provider = "openlibrary", Value = "OL1", CreatedAt = now },
            new EntityExternalIdRow { Id = Guid.NewGuid(), EntityId = albumId, Provider = "musicbrainz", Value = "album-1", CreatedAt = now });
        await db.SaveChangesAsync();
        var service = Service(db, trackableProviders: ["openlibrary", "musicbrainz"]);

        var book = await service.GetEligibilityAsync(bookId, CancellationToken.None);
        var album = await service.GetEligibilityAsync(albumId, CancellationToken.None);

        Assert.True(book.CanMonitor);
        Assert.False(book.DiscoversChildren);
        Assert.True(book.CanSearchMissingChildren);
        Assert.Equal(EntityKind.Book, book.MissingChildEntityKind);
        Assert.True(album.CanMonitor);
        Assert.False(album.DiscoversChildren);
        Assert.False(album.CanSearchMissingChildren);
        Assert.Null(album.MissingChildEntityKind);
        Assert.False((await service.GetEligibilityAsync(Guid.NewGuid(), CancellationToken.None)).CanMonitor);
    }

    [Fact]
    public async Task BatchStatePreservesRequestedEntitiesAndCombinesEligibilityMonitorAndLatestAcquisition() {
        await using var db = CreateContext();
        var authorId = SeedContainerEntity(db, "Author", provider: "openlibrary");
        var bookId = SeedContainerEntity(db, "Book", provider: "openlibrary", kind: EntityKind.Book);
        var seasonId = SeedContainerEntity(db, "Season 1", provider: "unavailable-tv", kind: EntityKind.VideoSeason);
        db.Entities.Local.Single(row => row.Id == bookId).IsWanted = true;
        db.Entities.Local.Single(row => row.Id == seasonId).IsWanted = true;
        var missingId = Guid.NewGuid();
        var acquisitionId = SeedAcquisition(db, "Book", "Author");
        db.Acquisitions.Local.Single(row => row.Id == acquisitionId).EntityId = bookId;
        db.Monitors.AddRange(
            new MonitorRow {
                Id = Guid.NewGuid(), EntityId = authorId, Kind = EntityKind.BookAuthor,
                Status = MonitorStatus.Active, Title = "Author",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            },
            new MonitorRow {
                Id = Guid.NewGuid(), EntityId = bookId, AcquisitionId = acquisitionId, Kind = EntityKind.Book,
                Status = MonitorStatus.Active, Title = "Book",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();
        var service = Service(db, trackableProviders: ["openlibrary"]);

        var states = await service.GetStatesAsync(
            [bookId, authorId, seasonId, missingId, bookId],
            CancellationToken.None);

        Assert.Equal([bookId, authorId, seasonId, missingId], states.Select(state => state.EntityId));
        var book = states[0];
        Assert.True(book.CanMonitor);
        Assert.True(book.CanRequest);
        Assert.False(book.DiscoversChildren);
        Assert.True(book.CanSearchMissingChildren);
        Assert.Equal(EntityKind.Book, book.MissingChildEntityKind);
        Assert.NotNull(book.Monitor);
        Assert.Equal(acquisitionId, book.LatestAcquisition?.Id);
        var author = states[1];
        Assert.True(author.CanMonitor);
        Assert.False(author.CanRequest);
        Assert.True(author.DiscoversChildren);
        Assert.True(author.CanSearchMissingChildren);
        Assert.Equal(EntityKind.Book, author.MissingChildEntityKind);
        Assert.NotNull(author.Monitor);
        Assert.Null(author.LatestAcquisition);
        var season = states[2];
        Assert.False(season.CanMonitor);
        Assert.True(season.CanRequest);
        Assert.False(season.DiscoversChildren);
        Assert.True(season.CanSearchMissingChildren);
        Assert.Equal(EntityKind.Video, season.MissingChildEntityKind);
        var missing = states[3];
        Assert.False(missing.CanMonitor);
        Assert.False(missing.CanRequest);
        Assert.False(missing.CanSearchMissingChildren);
        Assert.Null(missing.MissingChildEntityKind);
        Assert.Empty(missing.TrackableProviders);
        Assert.Null(missing.Monitor);
        Assert.Null(missing.LatestAcquisition);
    }

    [Fact]
    public async Task StartForEntityRefusesAnUntrackableContainer() {
        await using var db = CreateContext();
        var entityId = SeedContainerEntity(db, "Unknown Author", provider: "dead-provider");
        await db.SaveChangesAsync();
        var service = Service(db, trackableProviders: ["openlibrary"]);

        Assert.Null(await service.StartForEntityAsync(entityId, preset: null, CancellationToken.None));
    }

    [Fact]
    public async Task EligibilityAndStartRejectAnAmbiguousLegacyProviderRoute() {
        await using var db = CreateContext();
        var entityId = SeedContainerEntity(db, "Ambiguous Series", provider: "tmdb", kind: EntityKind.VideoSeries);
        await db.SaveChangesAsync();
        var identity = new ExternalIdentity("tmdb", "id-1");
        var router = new FixedIdentityRouter([
            new PluginIdentityRoute("alpha-provider", identity),
            new PluginIdentityRoute("zeta-provider", identity),
        ]);
        var service = Service(
            db,
            identityRouter: router,
            trackingCatalog: new Prismedia.Infrastructure.Requests.PluginProviderTrackingCatalog(router));

        var eligibility = await service.GetEligibilityAsync(entityId, CancellationToken.None);
        var started = await service.StartForEntityAsync(entityId, preset: null, CancellationToken.None);

        Assert.False(eligibility.CanMonitor);
        Assert.Empty(eligibility.TrackableProviders);
        Assert.Null(started);
        Assert.Empty(await db.Monitors.ToArrayAsync());
    }

    [Fact]
    public async Task StartForEntityMonitorsATrackableContainer() {
        await using var db = CreateContext();
        var entityId = SeedContainerEntity(db, "Brandon Sanderson", provider: "openlibrary");
        db.WantedSuppressions.Add(new WantedSuppressionRow {
            Id = Guid.NewGuid(),
            Provider = "openlibrary",
            ItemId = "id-1",
            Kind = EntityKind.BookAuthor,
            Title = "Brandon Sanderson",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = Service(db, trackableProviders: ["openlibrary"]);

        var monitor = await service.StartForEntityAsync(entityId, preset: null, CancellationToken.None);

        Assert.NotNull(monitor);
        Assert.Equal(entityId, monitor!.EntityId);
        Assert.Equal(MonitorStatus.Active, monitor.Status);
        Assert.Empty(await db.WantedSuppressions.ToArrayAsync());
    }

    [Fact]
    public async Task MonitoringAnOnDiskSeasonCreatesOnlyStableEntityIntent() {
        await using var db = CreateContext();
        var entityId = SeedContainerEntity(db, "Season 1", provider: "tmdbseason", kind: EntityKind.VideoSeason);
        await db.SaveChangesAsync();
        var service = Service(db, trackableProviders: ["tmdbseason"]);

        var monitor = await service.StartForEntityAsync(entityId, preset: null, CancellationToken.None);

        Assert.NotNull(monitor);
        Assert.Equal(entityId, monitor!.EntityId);
        Assert.Empty(await db.Acquisitions.ToArrayAsync());
    }

    private static MonitorService Service(
        PrismediaDbContext db,
        string[]? trackableProviders = null,
        IPluginIdentityRouter? identityRouter = null,
        IProviderTrackingCatalog? trackingCatalog = null) {
        var trackable = trackableProviders ?? [];
        var router = identityRouter ?? new TrackingIdentityRouter(trackable);
        var suppressions = new Prismedia.Infrastructure.Requests.EfWantedSuppressionStore(db);
        var acquisitionRequests = new MonitorTestAcquisitionRequests(db);
        return new(
            new EfMonitorStore(db),
            AcquisitionTestFactory.Store(db),
            new Prismedia.Infrastructure.Requests.WantedEntityWriter(
                db,
                new Prismedia.Infrastructure.Plugins.EntityMetadataApplyService(
                    db,
                    new Prismedia.Infrastructure.Plugins.PluginArtworkServiceOptions(Path.GetTempPath())),
                new EfEntityExternalIdentityStore(db, TimeProvider.System),
                new EfEntityProviderIdentityStore(db, TimeProvider.System),
                router,
                new EfEntityHierarchyReader(db)),
            trackingCatalog ?? new FakeTrackingCatalog(trackable),
            new EntityUnmonitorService(
                new EfEntityUnmonitorPersistence(db, new EfEntityHierarchyReader(db)),
                acquisitionRequests),
            suppressions);
    }

    private sealed class FixedIdentityRouter(IReadOnlyList<PluginIdentityRoute> routes) : IPluginIdentityRouter {
        public Task<IReadOnlyList<PluginIdentityRoute>> ResolveAsync(
            string entityKindCode,
            IdentifyAction action,
            IReadOnlyList<ExternalIdentity> identities,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PluginIdentityRoute>>(routes
                .Where(route => identities.Contains(route.Identity))
                .ToArray());
    }

    private sealed class MonitorTestAcquisitionRequests(PrismediaDbContext db) : IAcquisitionRequestService {
        public Task<AcquisitionRemovalEligibility> GetRemovalEligibilityAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(new AcquisitionRemovalEligibility(true));

        public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken, bool preserveWantedLoop = false) {
            var row = await db.Acquisitions.FindAsync([id], cancellationToken);
            if (row is null) {
                return false;
            }

            db.Acquisitions.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        public Task<bool> DeleteForUnmonitorAsync(Guid id, CancellationToken cancellationToken) =>
            DeleteAsync(id, cancellationToken);

        public Task<AcquisitionSummary> CreateAndSearchAsync(AcquisitionCreateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> AnyOpenForEntityAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AcquisitionReacquireEligibility> GetReacquireEligibilityAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ClaimTeardownAsync(Guid id, AcquisitionTeardownIntent intent, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task ConfirmTransferRemovedAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> CompleteTeardownAsync(Guid id, AcquisitionTeardownIntent intent, CancellationToken cancellationToken) => DeleteAsync(id, cancellationToken);
        public Task<Guid?> ReacquireAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TrackingIdentityRouter(string[] trackable) : IPluginIdentityRouter {
        public Task<IReadOnlyList<PluginIdentityRoute>> ResolveAsync(
            string entityKindCode,
            IdentifyAction action,
            IReadOnlyList<ExternalIdentity> identities,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PluginIdentityRoute>>(identities
                .Where(identity => trackable.Contains(identity.Namespace, StringComparer.OrdinalIgnoreCase))
                .Select(identity => new PluginIdentityRoute(identity.Namespace, identity))
                .ToArray());
    }

    /// <summary>Stands in for the plugin catalog: only the given provider ids count as trackable.</summary>
    private sealed class FakeTrackingCatalog(string[] trackable) : IProviderTrackingCatalog {
        public Task<IReadOnlyList<string>> TrackableProvidersAsync(
            string pluginKindCode,
            IReadOnlyList<ExternalIdentity> identities,
            PluginIdentityRoute? providerIdentity,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(providerIdentity is not null
                ? identities.Contains(providerIdentity.Identity)
                    && trackable.Contains(providerIdentity.PluginId, StringComparer.OrdinalIgnoreCase)
                    ? [providerIdentity.PluginId]
                    : []
                : identities
                .Select(identity => identity.Namespace)
                .Distinct(StringComparer.Ordinal)
                .Where(identityNamespace => trackable.Contains(identityNamespace, StringComparer.OrdinalIgnoreCase))
                .ToArray());
    }

    private static Guid SeedContainerEntity(
        PrismediaDbContext db,
        string title,
        string provider,
        EntityKind kind = EntityKind.BookAuthor) {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = id, KindCode = kind.ToCode(), Title = title, CreatedAt = now, UpdatedAt = now
        });
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(), EntityId = id, Provider = provider, Value = "id-1", Url = null, CreatedAt = now
        });
        return id;
    }

    private static Guid SeedAcquisition(PrismediaDbContext db, string title, string? author) {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = id, Status = AcquisitionStatus.Failed, Title = title, Author = author,
            ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now
        });
        return id;
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
