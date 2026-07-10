using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Files;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfEntitySourcePathOwnerReaderTests {
    [Fact]
    public async Task FolderLookupIncludesDescendantsAndArchiveLookupUsesPhysicalOwner() {
        await using var db = CreateContext();
        var folderOwnerId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var fileOwnerId = Guid.Parse("30000000-0000-0000-0000-000000000002");
        var archiveMemberOwnerId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var folderPath = Path.Combine(Path.GetTempPath(), "prismedia-source-owner", "Series");
        var archivePath = Path.Combine(Path.GetTempPath(), "prismedia-source-owner", "Book.cbz");
        SeedSource(db, folderOwnerId, folderPath);
        SeedSource(db, fileOwnerId, Path.Combine(folderPath, "Episode.mkv"));
        SeedSource(db, archiveMemberOwnerId, EntitySourcePath.ArchiveMember(archivePath, "001.jpg"));
        await db.SaveChangesAsync();
        var reader = new EfEntitySourcePathOwnerReader(db);

        var folderOwners = await reader.ListDirectOwnerIdsAsync(folderPath, CancellationToken.None);
        var archiveOwners = await reader.ListDirectOwnerIdsAsync(archivePath, CancellationToken.None);

        Assert.Equal([folderOwnerId, fileOwnerId], folderOwners.Order().ToArray());
        Assert.Equal([archiveMemberOwnerId], archiveOwners);
    }

    [Fact]
    public async Task FolderLookupKeepsCaseDistinctSiblingSeparateOnUnix() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        await using var db = CreateContext();
        var upperOwnerId = Guid.Parse("40000000-0000-0000-0000-000000000001");
        var lowerOwnerId = Guid.Parse("40000000-0000-0000-0000-000000000002");
        var root = Path.Combine(Path.GetTempPath(), "prismedia-source-owner-case");
        SeedSource(db, upperOwnerId, Path.Combine(root, "Album", "track.mp3"));
        SeedSource(db, lowerOwnerId, Path.Combine(root, "album", "track.mp3"));
        await db.SaveChangesAsync();

        var ownerIds = await new EfEntitySourcePathOwnerReader(db).ListDirectOwnerIdsAsync(
            Path.Combine(root, "Album"),
            CancellationToken.None);

        Assert.Equal([upperOwnerId], ownerIds);
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"entity-source-path-owner-{Guid.NewGuid():N}")
            .Options;
        return new PrismediaDbContext(options);
    }

    private static void SeedSource(PrismediaDbContext db, Guid entityId, string path) {
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = EntityKindRegistry.Video.Code,
            Title = entityId.ToString("N"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Role = EntityFileRole.Source,
            Path = path,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }
}
