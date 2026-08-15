using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EntityAvailabilityProjectionPostgresTests {
    [Fact]
    public async Task TriggersMaintainHierarchyAvailabilityAndReconcilerRepairsDrift() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var oldRootId = Guid.NewGuid();
        var newRootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        db.Entities.AddRange(
            Entity(oldRootId, EntityKind.Book, "Old root", null, now),
            Entity(newRootId, EntityKind.Book, "New root", null, now),
            Entity(childId, EntityKind.BookChapter, "Chapter", oldRootId, now));
        await db.SaveChangesAsync();

        var sourceId = Guid.NewGuid();
        db.EntityFiles.Add(new EntityFileRow {
            Id = sourceId,
            EntityId = childId,
            Role = EntityFileRole.Source,
            Path = "/media/book/chapter.cbz",
            CreatedAt = now,
            UpdatedAt = now,
        });
        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            EntityId = childId,
            Kind = EntityKind.Book,
            Status = AcquisitionStatus.Downloading,
            Title = "Chapter",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        Assert.True((await SnapshotAsync(db, oldRootId)).HasSourceMedia);
        Assert.Contains(AcquisitionStatus.Downloading.ToCode(),
            (await SnapshotAsync(db, oldRootId)).AcquisitionStatusCodes);

        var upgradeId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = upgradeId,
            UpgradeOfAcquisitionId = acquisitionId,
            Kind = EntityKind.Book,
            Status = AcquisitionStatus.Importing,
            Title = "Chapter upgrade",
            CreatedAt = now.AddSeconds(1),
            UpdatedAt = now.AddSeconds(1),
        });
        await db.SaveChangesAsync();
        Assert.Equal(
            [AcquisitionStatus.Downloading.ToCode(), AcquisitionStatus.Importing.ToCode()],
            (await SnapshotAsync(db, oldRootId)).AcquisitionStatusCodes.Order());

        await db.Acquisitions
            .Where(row => row.Id == upgradeId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, AcquisitionStatus.Imported)
                .SetProperty(row => row.UpdatedAt, now.AddMinutes(1)));
        Assert.Equal(
            [AcquisitionStatus.Downloading.ToCode(), AcquisitionStatus.Imported.ToCode()],
            (await SnapshotAsync(db, oldRootId)).AcquisitionStatusCodes.Order());

        await db.Acquisitions.Where(row => row.Id == upgradeId).ExecuteDeleteAsync();

        await db.Acquisitions
            .Where(row => row.Id == acquisitionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, AcquisitionStatus.Imported)
                .SetProperty(row => row.UpdatedAt, now.AddMinutes(1)));
        Assert.Equal(
            [AcquisitionStatus.Imported.ToCode()],
            (await SnapshotAsync(db, oldRootId)).AcquisitionStatusCodes);

        await db.Entities
            .Where(row => row.Id == childId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ParentEntityId, newRootId));
        Assert.Empty((await SnapshotAsync(db, oldRootId)).AcquisitionStatusCodes);
        Assert.Contains(
            AcquisitionStatus.Imported.ToCode(),
            (await SnapshotAsync(db, newRootId)).AcquisitionStatusCodes);

        await db.EntityFiles.Where(row => row.Id == sourceId).ExecuteDeleteAsync();
        Assert.False((await SnapshotAsync(db, newRootId)).HasSourceMedia);

        await db.EntityAvailability
            .Where(row => row.EntityId == newRootId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.HasSourceMedia, true)
                .SetProperty(row => row.AcquisitionStatusCodes, new[] { AcquisitionStatus.Failed.ToCode() }));
        var reconciler = new EfEntityAvailabilityReconciler(db);
        Assert.True(await reconciler.ReconcileAsync(CancellationToken.None) > 0);
        var repaired = await SnapshotAsync(db, newRootId);
        Assert.False(repaired.HasSourceMedia);
        Assert.Equal([AcquisitionStatus.Imported.ToCode()], repaired.AcquisitionStatusCodes);
    }

    private static EntityRow Entity(
        Guid id,
        EntityKind kind,
        string title,
        Guid? parentId,
        DateTimeOffset now) =>
        new() {
            Id = id,
            KindCode = kind.ToCode(),
            Title = title,
            ParentEntityId = parentId,
            CreatedAt = now,
            UpdatedAt = now,
        };

    private static async Task<EntityAvailabilityRow> SnapshotAsync(
        Prismedia.Infrastructure.Persistence.PrismediaDbContext db,
        Guid id) {
        db.ChangeTracker.Clear();
        return await db.EntityAvailability.AsNoTracking().SingleAsync(row => row.EntityId == id);
    }
}
