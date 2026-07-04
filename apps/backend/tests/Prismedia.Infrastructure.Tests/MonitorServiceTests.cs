using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Exercises MonitorService over the real EF stores: start (idempotent, 404), and pause/resume/stop guards.</summary>
public sealed class MonitorServiceTests {
    [Fact]
    public async Task StartReturnsNullWhenAcquisitionDoesNotExist() {
        await using var db = CreateContext();
        var service = new MonitorService(new EfMonitorStore(db), AcquisitionTestFactory.Store(db), new Prismedia.Infrastructure.Requests.WantedEntityWriter(db, new Prismedia.Infrastructure.Plugins.EntityMetadataApplyService(db, new Prismedia.Infrastructure.Plugins.PluginArtworkServiceOptions(Path.GetTempPath()))));

        Assert.Null(await service.StartAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task StartIsIdempotentAndDenormalizesTitleAuthorFromTheAcquisition() {
        await using var db = CreateContext();
        var acquisitionId = SeedAcquisition(db, "The Anxious Generation", "Jonathan Haidt");
        await db.SaveChangesAsync();
        var service = new MonitorService(new EfMonitorStore(db), AcquisitionTestFactory.Store(db), new Prismedia.Infrastructure.Requests.WantedEntityWriter(db, new Prismedia.Infrastructure.Plugins.EntityMetadataApplyService(db, new Prismedia.Infrastructure.Plugins.PluginArtworkServiceOptions(Path.GetTempPath()))));

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
        var service = new MonitorService(new EfMonitorStore(db), AcquisitionTestFactory.Store(db), new Prismedia.Infrastructure.Requests.WantedEntityWriter(db, new Prismedia.Infrastructure.Plugins.EntityMetadataApplyService(db, new Prismedia.Infrastructure.Plugins.PluginArtworkServiceOptions(Path.GetTempPath()))));
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
        var service = new MonitorService(new EfMonitorStore(db), AcquisitionTestFactory.Store(db), new Prismedia.Infrastructure.Requests.WantedEntityWriter(db, new Prismedia.Infrastructure.Plugins.EntityMetadataApplyService(db, new Prismedia.Infrastructure.Plugins.PluginArtworkServiceOptions(Path.GetTempPath()))));
        var monitor = await service.StartAsync(acquisitionId, CancellationToken.None);

        Assert.True(await service.PauseAsync(monitor!.Id, CancellationToken.None));
        Assert.Equal(MonitorStatus.Paused, (await service.ListAsync(CancellationToken.None))[0].Status);
        Assert.True(await service.ResumeAsync(monitor.Id, CancellationToken.None));
        Assert.Equal(MonitorStatus.Active, (await service.ListAsync(CancellationToken.None))[0].Status);
        Assert.True(await service.StopAsync(monitor.Id, CancellationToken.None));
        Assert.Empty(await service.ListAsync(CancellationToken.None));
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
