using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
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
        Assert.False(await service.StopAsync(unknown, CancellationToken.None));
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
        Assert.True(await service.StopAsync(monitor.Id, CancellationToken.None));
        Assert.Empty(await service.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EligibilityRequiresATrackableProvider() {
        await using var db = CreateContext();
        var trackedId = SeedContainerEntity(db, "Brandon Sanderson", provider: "openlibrary");
        var orphanId = SeedContainerEntity(db, "Unknown Author", provider: "dead-provider");
        await db.SaveChangesAsync();
        var service = Service(db, trackableProviders: ["openlibrary"]);

        var tracked = await service.GetEligibilityAsync(trackedId, CancellationToken.None);
        Assert.True(tracked.CanMonitor);
        Assert.Equal(["openlibrary"], tracked.TrackableProviders);

        var orphan = await service.GetEligibilityAsync(orphanId, CancellationToken.None);
        Assert.False(orphan.CanMonitor);
        Assert.Empty(orphan.TrackableProviders);
    }

    [Fact]
    public async Task EligibilityIsFalseForNonContainerKindsAndMissingEntities() {
        await using var db = CreateContext();
        var bookId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow { Id = bookId, KindCode = EntityKindRegistry.Book.Code, Title = "A Book", CreatedAt = now, UpdatedAt = now });
        db.EntityExternalIds.Add(new EntityExternalIdRow { Id = Guid.NewGuid(), EntityId = bookId, Provider = "openlibrary", Value = "OL1", CreatedAt = now });
        await db.SaveChangesAsync();
        var service = Service(db, trackableProviders: ["openlibrary"]);

        Assert.False((await service.GetEligibilityAsync(bookId, CancellationToken.None)).CanMonitor);
        Assert.False((await service.GetEligibilityAsync(Guid.NewGuid(), CancellationToken.None)).CanMonitor);
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
    public async Task StartForEntityMonitorsATrackableContainer() {
        await using var db = CreateContext();
        var entityId = SeedContainerEntity(db, "Brandon Sanderson", provider: "openlibrary");
        await db.SaveChangesAsync();
        var service = Service(db, trackableProviders: ["openlibrary"]);

        var monitor = await service.StartForEntityAsync(entityId, preset: null, CancellationToken.None);

        Assert.NotNull(monitor);
        Assert.Equal(entityId, monitor!.EntityId);
        Assert.Equal(MonitorStatus.Active, monitor.Status);
    }

    private static MonitorService Service(PrismediaDbContext db, string[]? trackableProviders = null) =>
        new(
            new EfMonitorStore(db),
            AcquisitionTestFactory.Store(db),
            new Prismedia.Infrastructure.Requests.WantedEntityWriter(db, new Prismedia.Infrastructure.Plugins.EntityMetadataApplyService(db, new Prismedia.Infrastructure.Plugins.PluginArtworkServiceOptions(Path.GetTempPath()))),
            new FakeTrackingCatalog(trackableProviders ?? []));

    /// <summary>Stands in for the plugin catalog: only the given provider ids count as trackable.</summary>
    private sealed class FakeTrackingCatalog(string[] trackable) : IProviderTrackingCatalog {
        public Task<IReadOnlyList<string>> TrackableProvidersAsync(string pluginKindCode, IReadOnlyList<ProviderRef> providerIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(providerIds
                .Select(reference => reference.Provider)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(provider => trackable.Contains(provider, StringComparer.OrdinalIgnoreCase))
                .ToArray());
    }

    private static Guid SeedContainerEntity(PrismediaDbContext db, string title, string provider) {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = id, KindCode = EntityKindRegistry.BookAuthor.Code, Title = title, CreatedAt = now, UpdatedAt = now
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
