using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Navigation;
using Prismedia.Contracts.Navigation;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Tests;

public sealed class NavLayoutServiceTests {
    [Fact]
    public async Task GetReturnsNullWhenNothingStored() {
        await using var db = CreateContext();
        var service = new NavLayoutService(new EfSettingsPersistence(db));

        Assert.Null(await service.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveThenGetRoundTripsTheLayout() {
        await using var db = CreateContext();
        var service = new NavLayoutService(new EfSettingsPersistence(db));

        var layout = new NavLayoutDocument(
            Version: 1,
            Sections: [new NavLayoutSection("books", "Books", ["/books", "/comics", "/ebooks"], false)],
            Hidden: ["/images"],
            MobileFavorites: ["/files", "/videos"]);

        await service.SaveAsync(layout, CancellationToken.None);
        var loaded = await service.GetAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.Version);
        var section = Assert.Single(loaded.Sections);
        Assert.Equal("books", section.Id);
        Assert.Equal(["/books", "/comics", "/ebooks"], section.Items);
        Assert.Equal(["/images"], loaded.Hidden);
        Assert.Equal(["/files", "/videos"], loaded.MobileFavorites);
    }

    [Fact]
    public async Task GetReturnsNullWhenStoredJsonIsInvalid() {
        await using var db = CreateContext();
        db.AppSettings.Add(new AppSettingRow {
            Key = NavLayoutService.LayoutKey,
            ValueJson = "{ not json",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = new NavLayoutService(new EfSettingsPersistence(db));
        Assert.Null(await service.GetAsync(CancellationToken.None));
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"nav-layout-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }
}
