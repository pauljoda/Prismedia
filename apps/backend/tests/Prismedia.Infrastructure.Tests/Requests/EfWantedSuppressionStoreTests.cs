using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Requests;

namespace Prismedia.Infrastructure.Tests.Requests;

public sealed class EfWantedSuppressionStoreTests {
    [Fact]
    public async Task SuppressionUsesCanonicalTypedIdentitiesWithoutDelimiterCollisions() {
        await using var db = CreateContext();
        var store = new EfWantedSuppressionStore(db);
        var identity = new ExternalIdentity(" TMDB ", " work:603 ");

        await store.SuppressAsync([identity, identity], EntityKind.Movie, "The Matrix", CancellationToken.None);

        var row = Assert.Single(await db.WantedSuppressions.AsNoTracking().ToArrayAsync());
        Assert.Equal("tmdb", row.Provider);
        Assert.Equal("work:603", row.ItemId);
        var filtered = await store.FilterSuppressedAsync(
            [new ExternalIdentity("tmdb", "work:603")],
            CancellationToken.None);
        Assert.Equal(identity, Assert.Single(filtered));

        await store.ClearAsync([identity], CancellationToken.None);

        Assert.Empty(await db.WantedSuppressions.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task LegacyNonCanonicalRowsResolveWithoutCreatingDuplicateSuppressions() {
        await using var db = CreateContext();
        db.WantedSuppressions.Add(new WantedSuppressionRow {
            Id = Guid.NewGuid(),
            Provider = " TMDB ",
            ItemId = " 603 ",
            Kind = EntityKind.Movie,
            Title = "The Matrix",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var store = new EfWantedSuppressionStore(db);
        var identity = new ExternalIdentity("tmdb", "603");

        await store.SuppressAsync([identity], EntityKind.Movie, "The Matrix", CancellationToken.None);

        Assert.Single(await db.WantedSuppressions.AsNoTracking().ToArrayAsync());
        Assert.Equal(identity, Assert.Single(await store.FilterSuppressedAsync([identity], CancellationToken.None)));
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
