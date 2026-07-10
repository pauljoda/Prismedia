using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Proves the shared Entity hierarchy walk is kind-agnostic, unbounded, and cycle-safe.</summary>
public sealed class EfEntityHierarchyReaderTests {
    [Fact]
    public async Task ListsAnArbitrarilyDeepSubtreeWithoutMediaKindRules() {
        await using var db = CreateContext();
        var ids = Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToArray();
        for (var index = 0; index < ids.Length; index++) {
            db.Entities.Add(NewEntity(
                ids[index],
                index % 2 == 0 ? EntityKindRegistry.MusicArtist.Code : EntityKindRegistry.Book.Code,
                index == 0 ? null : ids[index - 1]));
        }
        await db.SaveChangesAsync();

        var actual = await new EfEntityHierarchyReader(db)
            .ListSubtreeIdsAsync(ids[0], CancellationToken.None);

        Assert.Equal(ids, actual);
    }

    [Fact]
    public async Task AncestorAndSubtreeWalksStopWhenCorruptDataCycles() {
        await using var db = CreateContext();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        db.Entities.AddRange(
            NewEntity(first, EntityKindRegistry.VideoSeries.Code, second),
            NewEntity(second, EntityKindRegistry.AudioLibrary.Code, first));
        await db.SaveChangesAsync();
        var reader = new EfEntityHierarchyReader(db);

        Assert.Equal([first, second], await reader.ListSubtreeIdsAsync(first, CancellationToken.None));
        Assert.Equal([second], await reader.ListAncestorIdsAsync(first, CancellationToken.None));
    }

    private static EntityRow NewEntity(Guid id, string kind, Guid? parentId) => new() {
        Id = id,
        KindCode = kind,
        Title = id.ToString(),
        ParentEntityId = parentId,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
