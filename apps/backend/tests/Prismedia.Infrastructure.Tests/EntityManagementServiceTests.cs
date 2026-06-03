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
            EntityKindRegistry.Tag.Code, new EntityCreateRequest("  Sci-Fi  "), CancellationToken.None);

        Assert.Equal(EntityCommandStatus.Created, result.Status);
        Assert.NotNull(result.Id);
        var row = await db.Entities.SingleAsync(e => e.Id == result.Id);
        Assert.Equal(EntityKindRegistry.Tag.Code, row.KindCode);
        Assert.Equal("Sci-Fi", row.Title);
        Assert.Null(row.DeletedAt);
    }

    [Fact]
    public async Task CreateAsyncRejectsBlankTitle() {
        await using var db = CreateContext();
        var service = new EntityManagementService(db);

        var result = await service.CreateAsync(
            EntityKindRegistry.Person.Code, new EntityCreateRequest("   "), CancellationToken.None);

        Assert.Equal(EntityCommandStatus.Invalid, result.Status);
        Assert.Empty(db.Entities);
    }

    [Fact]
    public async Task CreateAsyncRejectsNonManageableKind() {
        await using var db = CreateContext();
        var service = new EntityManagementService(db);

        var result = await service.CreateAsync(
            EntityKindRegistry.Video.Code, new EntityCreateRequest("Nope"), CancellationToken.None);

        Assert.Equal(EntityCommandStatus.KindNotManageable, result.Status);
        Assert.Empty(db.Entities);
    }

    [Fact]
    public async Task DeleteAsyncSoftDeletesEntityAndDetachesReferencingMedia() {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var tagId = Guid.NewGuid();
        var videoId = Guid.NewGuid();
        db.Entities.AddRange(
            new EntityRow { Id = tagId, KindCode = EntityKindRegistry.Tag.Code, Title = "Noir", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = videoId, KindCode = EntityKindRegistry.Video.Code, Title = "Film", CreatedAt = now, UpdatedAt = now });
        db.EntityRelationshipLinks.Add(new EntityRelationshipLinkRow {
            EntityId = videoId,
            RelationshipCode = "tags",
            Label = "Tags",
            TargetEntityId = tagId,
            TargetKindCode = EntityKindRegistry.Tag.Code,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();

        var service = new EntityManagementService(db);
        var result = await service.DeleteAsync(tagId, EntityKindRegistry.Tag.Code, CancellationToken.None);

        Assert.Equal(EntityCommandStatus.Deleted, result.Status);
        var tag = await db.Entities.SingleAsync(e => e.Id == tagId);
        Assert.NotNull(tag.DeletedAt);
        Assert.False(await db.EntityRelationshipLinks.AnyAsync(link => link.TargetEntityId == tagId));
    }

    [Fact]
    public async Task DeleteAsyncReturnsNotFoundForMissingEntity() {
        await using var db = CreateContext();
        var service = new EntityManagementService(db);

        var result = await service.DeleteAsync(Guid.NewGuid(), EntityKindRegistry.Tag.Code, CancellationToken.None);

        Assert.Equal(EntityCommandStatus.NotFound, result.Status);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
