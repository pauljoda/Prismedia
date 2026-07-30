using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EntityManagementServiceTests {
    [Fact]
    public async Task CreateAsyncCreatesTaxonomyEntity() {
        await using var db = CreateContext();
        var service = new EntityManagementService(db);

        var result = await service.CreateAsync(
            EntityKind.Tag.ToCode(), new EntityCreateRequest("  Sci-Fi  "), CancellationToken.None);

        Assert.Equal(EntityCommandStatus.Created, result.Status);
        Assert.NotNull(result.Id);
        var row = await db.Entities.SingleAsync(e => e.Id == result.Id);
        Assert.Equal(EntityKind.Tag.ToCode(), row.KindCode);
        Assert.Equal("Sci-Fi", row.Title);
    }

    [Fact]
    public async Task CreateAsyncRejectsBlankTitle() {
        await using var db = CreateContext();
        var service = new EntityManagementService(db);

        var result = await service.CreateAsync(
            EntityKind.Person.ToCode(), new EntityCreateRequest("   "), CancellationToken.None);

        Assert.Equal(EntityCommandStatus.Invalid, result.Status);
        Assert.Empty(db.Entities);
    }

    [Fact]
    public async Task CreateAsyncRejectsNonManageableKind() {
        await using var db = CreateContext();
        var service = new EntityManagementService(db);

        var result = await service.CreateAsync(
            EntityKind.Video.ToCode(), new EntityCreateRequest("Nope"), CancellationToken.None);

        Assert.Equal(EntityCommandStatus.KindNotManageable, result.Status);
        Assert.Empty(db.Entities);
    }

    [Fact]
    public async Task DeleteAsyncHardDeletesEntityAndLeavesReferencingMedia() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var tagId = Guid.NewGuid();
        var videoId = Guid.NewGuid();
        db.Entities.AddRange(
            new EntityRow { Id = tagId, KindCode = EntityKind.Tag.ToCode(), Title = "Noir", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = videoId, KindCode = EntityKind.Video.ToCode(), Title = "Film", CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        var service = new EntityManagementService(db);
        var result = await service.DeleteAsync(tagId, EntityKind.Tag.ToCode(), CancellationToken.None);

        Assert.Equal(EntityCommandStatus.Deleted, result.Status);
        // The tag row is gone; the previously referencing media survives. The relationship-link
        // rows are removed by the FK cascade on entity_id/target_entity_id (exercised against the
        // real database, not the in-memory provider used here).
        Assert.False(await db.Entities.AnyAsync(e => e.Id == tagId));
        Assert.True(await db.Entities.AnyAsync(e => e.Id == videoId));
    }

    [Fact]
    public async Task DeleteAsyncReturnsNotFoundForMissingEntity() {
        await using var db = CreateContext();
        var service = new EntityManagementService(db);

        var result = await service.DeleteAsync(Guid.NewGuid(), EntityKind.Tag.ToCode(), CancellationToken.None);

        Assert.Equal(EntityCommandStatus.NotFound, result.Status);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
