using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class MetadataFieldServiceTests {
    [Fact]
    public async Task LockingExistingUntrackedValueDoesNotInventProvenanceAndRejectsStaleWrites() {
        await using var db = CreateContext();
        var id = await SeedAsync(db);
        var service = new EfMetadataFieldService(db, new Lease());
        var before = await service.ReadAsync(id, default);
        Assert.All(before!, field => Assert.Equal(MetadataValueOrigin.Unknown, field.Origin));
        var locked = await service.SetLockAsync(id, MetadataPatchField.Title, new(0, true), default);
        Assert.True(locked!.IsLocked);
        Assert.Equal(1, locked.Revision);
        Assert.Null(locked.ObservedAt);
        await Assert.ThrowsAsync<MetadataFieldConflictException>(() => service.SetLockAsync(id, MetadataPatchField.Title, new(0, false), default));
        Assert.Equal("Existing book", (await db.Entities.FindAsync(id))!.Title);
    }

    [Fact]
    public async Task UnlockingPreservesProviderEvidenceAndRepeatedRequestIsANoop() {
        await using var db = CreateContext();
        var id = await SeedAsync(db);
        var evidence = MetadataFieldEvidence.Unknown.WrittenByProvider("book-provider", 0.8m, DateTimeOffset.UtcNow).WithLock(true);
        var row = new EntityMetadataFieldRow { EntityId = id, Field = MetadataPatchField.Description };
        row.Apply(evidence);
        db.EntityMetadataFields.Add(row);
        await db.SaveChangesAsync();
        var service = new EfMetadataFieldService(db, new Lease());
        var unlocked = await service.SetLockAsync(id, row.Field, new(evidence.Revision, false), default);
        Assert.Equal(evidence.ProviderId, unlocked!.ProviderId);
        Assert.Equal(evidence.ObservedAt, unlocked.ObservedAt);
        Assert.Equal(evidence.Confidence, unlocked.Confidence);
        Assert.Equal(unlocked, await service.SetLockAsync(id, row.Field, new(unlocked.Revision, false), default));
    }

    [Fact]
    public async Task DestructiveOwnershipPreventsProtectionWritesAndMissingEntityReturnsNull() {
        await using var db = CreateContext();
        var id = await SeedAsync(db);
        var service = new EfMetadataFieldService(db, new Lease(false));
        Assert.Null(await service.SetLockAsync(id, MetadataPatchField.Title, new(0, true), default));
        Assert.Empty(db.EntityMetadataFields);
        Assert.Null(await service.ReadAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task UnsupportedCollectionLockIsRejectedAndUnknownUnlockCreatesNoRow() {
        await using var db = CreateContext();
        var id = await SeedAsync(db);
        var service = new EfMetadataFieldService(db, new Lease());
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetLockAsync(id, MetadataPatchField.Tags, new(0, true), default));
        var unchanged = await service.SetLockAsync(id, MetadataPatchField.Title, new(0, false), default);
        Assert.Equal(0, unchanged!.Revision);
        Assert.Empty(db.EntityMetadataFields);
    }

    private static PrismediaDbContext CreateContext() => new(new DbContextOptionsBuilder<PrismediaDbContext>()
        .UseInMemoryDatabase($"field-evidence-{Guid.NewGuid():N}").Options);
    private static async Task<Guid> SeedAsync(PrismediaDbContext db) {
        var id = Guid.NewGuid();
        db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKind.Book.ToCode(), Title = "Existing book", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return id;
    }
    private sealed class Lease(bool accepted = true) : IEntityLifecycleMutationLease {
        public async Task<bool> ExecuteAsync(Guid entityId, Func<CancellationToken, Task> mutation, CancellationToken cancellationToken) {
            if (accepted) await mutation(cancellationToken);
            return accepted;
        }
    }
}
