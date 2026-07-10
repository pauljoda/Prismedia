using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfEntityProviderIdentityStoreTests {
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-07-09T10:00:00Z");
    private static readonly DateTimeOffset ReplacedAt =
        DateTimeOffset.Parse("2026-07-09T11:00:00Z");

    [Fact]
    public async Task SetAsyncRejectsIdentityNotOwnedByEntity() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, "Unidentified");
        await db.SaveChangesAsync();
        var store = Store(db, CreatedAt);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetAsync(
            entityId,
            "tmdb",
            new ExternalIdentity("tmdb", "82728"),
            CancellationToken.None));

        Assert.Contains("already owned", exception.Message, StringComparison.Ordinal);
        Assert.Empty(db.EntityProviderIdentities);
    }

    [Fact]
    public async Task SetAsyncRoundTripsNormalizedPluginAndOpaqueIdentity() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, "Provider identity");
        AddIdentity(db, entityId, "tmdbseason", "Series-A:02");
        await db.SaveChangesAsync();
        var store = Store(db, CreatedAt);

        await store.SetAsync(
            entityId,
            " TMDB ",
            new ExternalIdentity(" TMDBSeason ", " Series-A:02 "),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var binding = await store.GetAsync(entityId, CancellationToken.None);
        Assert.NotNull(binding);
        Assert.Equal(entityId, binding.EntityId);
        Assert.Equal("tmdb", binding.PluginId);
        Assert.Equal(new ExternalIdentity("tmdbseason", "Series-A:02"), binding.Identity);
        var row = await db.EntityProviderIdentities.SingleAsync();
        Assert.Equal("tmdbseason", row.IdentityNamespace);
        Assert.Equal("Series-A:02", row.IdentityValue);
        Assert.Equal(CreatedAt, row.CreatedAt);
        Assert.Equal(CreatedAt, row.UpdatedAt);
    }

    [Fact]
    public async Task SetAsyncReplacesExistingBindingWithoutCreatingASecondRow() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, "Replacement");
        AddIdentity(db, entityId, "tmdb", "82728");
        AddIdentity(db, entityId, "tvdb", "364140");
        await db.SaveChangesAsync();

        await Store(db, CreatedAt).SetAsync(
            entityId,
            "tmdb",
            new ExternalIdentity("tmdb", "82728"),
            CancellationToken.None);
        await db.SaveChangesAsync();
        await Store(db, ReplacedAt).SetAsync(
            entityId,
            " television-metadata ",
            new ExternalIdentity("tvdb", "364140"),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var row = await db.EntityProviderIdentities.SingleAsync();
        Assert.Equal("television-metadata", row.PluginId);
        Assert.Equal("tvdb", row.IdentityNamespace);
        Assert.Equal("364140", row.IdentityValue);
        Assert.Equal(CreatedAt, row.CreatedAt);
        Assert.Equal(ReplacedAt, row.UpdatedAt);
        var binding = await Store(db, ReplacedAt).GetAsync(entityId, CancellationToken.None);
        Assert.Equal(new ExternalIdentity("tvdb", "364140"), binding?.Identity);
    }

    [Fact]
    public async Task GetAsyncDoesNotSilentlyRetargetBindingWhenRawIdentityValueChanges() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, "Exact value");
        AddIdentity(db, entityId, "tmdb", "82728");
        await db.SaveChangesAsync();
        var store = Store(db, CreatedAt);
        await store.SetAsync(
            entityId,
            "tmdb",
            new ExternalIdentity("tmdb", "82728"),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var rawIdentity = await db.EntityExternalIds.SingleAsync(value => value.EntityId == entityId);
        rawIdentity.Value = "99999";
        await db.SaveChangesAsync();

        var binding = await store.GetAsync(entityId, CancellationToken.None);
        Assert.Equal(new ExternalIdentity("tmdb", "82728"), binding?.Identity);
        var persistedRoute = await db.EntityProviderIdentities.SingleAsync();
        Assert.Equal("82728", persistedRoute.IdentityValue);
    }

    [Fact]
    public async Task DeletingEntityCascadesProviderBinding() {
        await using var db = CreateContext();
        var entityId = AddEntity(db, "Cascade");
        AddIdentity(db, entityId, "tmdb", "82728");
        await db.SaveChangesAsync();
        await Store(db, CreatedAt).SetAsync(
            entityId,
            "tmdb",
            new ExternalIdentity("tmdb", "82728"),
            CancellationToken.None);
        await db.SaveChangesAsync();

        db.Entities.Remove(await db.Entities.SingleAsync(entity => entity.Id == entityId));
        await db.SaveChangesAsync();

        Assert.Empty(db.EntityProviderIdentities);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"provider-identity-{Guid.NewGuid():N}")
            .Options);

    private static Guid AddEntity(PrismediaDbContext db, string title) {
        var id = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = EntityKindRegistry.VideoSeries.Code,
            Title = title,
            CreatedAt = CreatedAt,
            UpdatedAt = CreatedAt
        });
        return id;
    }

    private static void AddIdentity(
        PrismediaDbContext db,
        Guid entityId,
        string identityNamespace,
        string value) =>
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Provider = identityNamespace,
            Value = value,
            CreatedAt = CreatedAt,
            UpdatedAt = CreatedAt
        });

    private static EfEntityProviderIdentityStore Store(
        PrismediaDbContext db,
        DateTimeOffset now) =>
        new(db, new FixedTimeProvider(now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
