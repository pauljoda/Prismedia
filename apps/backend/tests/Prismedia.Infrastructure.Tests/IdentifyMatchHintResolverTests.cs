using Microsoft.EntityFrameworkCore;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class IdentifyMatchHintResolverTests {
    [Fact]
    public async Task ResolvePrefersStoredProviderIdOverProviderUrls() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        SeedEntity(db, entityId, "video", "Stored Match");
        db.EntityUrls.Add(new EntityUrlRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Url = "https://www.themoviedb.org/movie/999",
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Provider = "tmdb",
            Value = "123",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var resolver = new IdentifyMatchHintResolver(db);
        var hints = await resolver.ResolveAsync(entityId, "tmdb", CancellationToken.None);

        Assert.Equal("123", hints.ExternalIds["tmdb"]);
        Assert.Equal("Stored Match", hints.Title);
        Assert.Contains("https://www.themoviedb.org/movie/999", hints.Urls);
    }

    [Fact]
    public async Task ResolveParsesProviderUrlWhenStoredIdIsMissing() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        SeedEntity(db, entityId, "video-series", "URL Match");
        db.EntityUrls.Add(new EntityUrlRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Url = "https://www.themoviedb.org/tv/456-url-match",
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var resolver = new IdentifyMatchHintResolver(db);
        var hints = await resolver.ResolveAsync(entityId, "tmdb", CancellationToken.None);

        Assert.Equal("456", hints.ExternalIds["tmdb"]);
        Assert.Equal("URL Match", hints.Title);
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"identify-hints-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }

    private static void SeedEntity(PrismediaDbContext db, Guid id, string kind, string title) {
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind,
            Title = title,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }
}
