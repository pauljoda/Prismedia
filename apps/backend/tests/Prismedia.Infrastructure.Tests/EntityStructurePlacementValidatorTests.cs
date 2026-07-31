using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EntityStructurePlacementValidatorTests {
    [Fact]
    public void ValidatePlacementHonorsRootRequiredAndOptionalPolicies() {
        EntityStructurePlacementValidator.ValidatePlacement(EntityKind.Movie, parentKind: null);
        EntityStructurePlacementValidator.ValidatePlacement(EntityKind.Image, parentKind: null);
        EntityStructurePlacementValidator.ValidatePlacement(EntityKind.Image, EntityKind.Gallery);

        Assert.Throws<InvalidOperationException>(() =>
            EntityStructurePlacementValidator.ValidatePlacement(EntityKind.VideoSeason, parentKind: null));
        Assert.Throws<InvalidOperationException>(() =>
            EntityStructurePlacementValidator.ValidatePlacement(EntityKind.Movie, EntityKind.Gallery));
        Assert.Throws<InvalidOperationException>(() =>
            EntityStructurePlacementValidator.ValidatePlacement(EntityKind.Image, EntityKind.Book));
    }

    [Fact]
    public async Task ValidateAsyncUsesTrackedAncestorInsteadOfStaleCachedSnapshot() {
        await using var db = CreateContext();
        var rootId = Guid.NewGuid();
        var ancestorId = Guid.NewGuid();
        var descendantId = Guid.NewGuid();
        Add(db, rootId, EntityKind.Gallery, null);
        Add(db, ancestorId, EntityKind.Gallery, rootId);
        Add(db, descendantId, EntityKind.Gallery, rootId);
        await db.SaveChangesAsync();

        var validator = new EntityStructurePlacementValidator(db);
        await validator.RequireParentKindAsync(ancestorId, CancellationToken.None);

        var ancestor = await db.Entities.SingleAsync(row => row.Id == ancestorId);
        ancestor.ParentEntityId = descendantId;

        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(
            EntityKind.Gallery,
            descendantId,
            ancestorId,
            rootId,
            knownParentKind: null,
            CancellationToken.None));
    }

    [Fact]
    public async Task ResetDropsSnapshotsAfterTheChangeTrackerIsCleared() {
        await using var db = CreateContext();
        var rootId = Guid.NewGuid();
        var ancestorId = Guid.NewGuid();
        var descendantId = Guid.NewGuid();
        Add(db, rootId, EntityKind.Gallery, null);
        Add(db, ancestorId, EntityKind.Gallery, rootId);
        Add(db, descendantId, EntityKind.Gallery, rootId);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var validator = new EntityStructurePlacementValidator(db);
        await validator.RequireParentKindAsync(ancestorId, CancellationToken.None);

        var ancestor = await db.Entities.SingleAsync(row => row.Id == ancestorId);
        ancestor.ParentEntityId = descendantId;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        validator.Reset();

        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(
            EntityKind.Gallery,
            descendantId,
            ancestorId,
            rootId,
            knownParentKind: null,
            CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsyncRejectsAnUnchangedSelfParentAssignment() {
        var entityId = Guid.NewGuid();
        await using var db = CreateContext();
        var validator = new EntityStructurePlacementValidator(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(
            EntityKind.Gallery,
            entityId,
            entityId,
            entityId,
            EntityKind.Gallery,
            CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsyncRejectsAParentMarkedForDeletion() {
        await using var db = CreateContext();
        var galleryId = Guid.NewGuid();
        Add(db, galleryId, EntityKind.Gallery, null);
        await db.SaveChangesAsync();

        db.Remove(await db.Entities.SingleAsync(row => row.Id == galleryId));
        var validator = new EntityStructurePlacementValidator(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(
            EntityKind.Image,
            Guid.NewGuid(),
            galleryId,
            currentParentId: null,
            knownParentKind: null,
            CancellationToken.None));
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static void Add(PrismediaDbContext db, Guid id, EntityKind kind, Guid? parentId) =>
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind.ToCode(),
            Title = kind.ToCode(),
            ParentEntityId = parentId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
}
