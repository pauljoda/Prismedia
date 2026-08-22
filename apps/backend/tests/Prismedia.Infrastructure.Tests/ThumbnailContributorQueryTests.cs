using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities.Thumbnails;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class ThumbnailContributorQueryTests {
    [Fact]
    public void StructuralCountsTranslateToOneGroupedUnionAggregate() {
        using var db = CreateNpgsqlContext();
        var visibleEntities = db.Entities.AsNoTracking()
            .Where(entity => !entity.IsNsfw && !entity.IsWanted);

        var sql = StructuralCountContributor.BuildQuery(
                visibleEntities,
                [Guid.Parse("11111111-1111-1111-1111-111111111111")],
                maxDepth: 3)
            .ToQueryString();

        Assert.Contains("UNION ALL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("count(*)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CollectionMembershipTranslatesToOneGroupedIndexedJoin() {
        using var db = CreateNpgsqlContext();
        var visibleEntities = db.Entities.AsNoTracking()
            .Where(entity => !entity.IsNsfw && !entity.IsWanted);

        var sql = CollectionMembershipCountContributor.BuildQuery(
                db,
                visibleEntities,
                [Guid.Parse("22222222-2222-2222-2222-222222222222")])
            .ToQueryString();

        Assert.Contains("collection_item_details", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INNER JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("count(*)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rule_tree_json", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StructuralContributorNoOpsForLeafOnlyPages() {
        await using var db = new PrismediaDbContext(
            new DbContextOptionsBuilder<PrismediaDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var leaf = new EntityRow {
            Id = Guid.NewGuid(),
            KindCode = EntityKind.Image.ToCode(),
            Title = "Leaf"
        };
        var contributions = new ThumbnailContributions(
            [leaf],
            db.Entities.AsNoTracking().Where(entity => !entity.IsWanted));

        await new StructuralCountContributor(db).ContributeAsync(contributions, CancellationToken.None);

        Assert.Empty(contributions.ExtraMetaFor(leaf.Id));
    }

    [Fact]
    public async Task PersistedPageStatsBecomeThumbnailChipsWithoutCountingDescendants() {
        await using var db = new PrismediaDbContext(
            new DbContextOptionsBuilder<PrismediaDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var installment = new EntityRow {
            Id = Guid.NewGuid(),
            KindCode = EntityKind.ComicInstallment.ToCode(),
            Title = "Chapter 1"
        };
        db.Entities.Add(installment);
        db.EntityStats.Add(new EntityStatRow {
            EntityId = installment.Id,
            Code = EntityStatCodes.Pages,
            Value = 42,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var contributions = new ThumbnailContributions(
            [installment],
            db.Entities.AsNoTracking());

        await new PersistedStatThumbnailContributor(db)
            .ContributeAsync(contributions, CancellationToken.None);

        Assert.Equal(
            [new Prismedia.Contracts.Entities.EntityThumbnailMeta(
                Prismedia.Contracts.Entities.EntityThumbnailMetaIcons.Page,
                "42")],
            contributions.ExtraMetaFor(installment.Id));
    }

    private static PrismediaDbContext CreateNpgsqlContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseNpgsql("Host=localhost;Database=prismedia;Username=prismedia;Password=prismedia")
            .Options);
}
