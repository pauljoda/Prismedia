using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Files;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfFilesPersistenceTests {
    [Fact]
    public async Task ListHiddenPathsAsyncHidesFoldersWhoseAssociatedSourcesAreAllNsfw() {
        await using var db = CreateContext();
        var rootPath = Path.GetFullPath("/media/safe");
        var folderPath = Path.Combine(rootPath, "Videos", "Friendship (2025)");
        var videoPath = Path.Combine(folderPath, "Friendship (2025) Bluray-1080p.mkv");
        await AddSourceEntityAsync(db, EntityKindRegistry.Video.Code, "Friendship (2025)", videoPath, isNsfw: true);

        var persistence = new EfFilesPersistence(db);

        var hidden = await persistence.ListHiddenPathsAsync(
            [folderPath, videoPath],
            CancellationToken.None);

        Assert.Contains(folderPath, hidden);
        Assert.Contains(videoPath, hidden);
    }

    [Fact]
    public async Task ListHiddenPathsAsyncKeepsMixedAncestorsVisibleWhenTheyContainSafeSources() {
        await using var db = CreateContext();
        var rootPath = Path.GetFullPath("/media/safe");
        var videosPath = Path.Combine(rootPath, "Videos");
        var nsfwFolderPath = Path.Combine(videosPath, "Friendship (2025)");
        var nsfwVideoPath = Path.Combine(nsfwFolderPath, "Friendship (2025) Bluray-1080p.mkv");
        var safeVideoPath = Path.Combine(videosPath, "bbb_sunflower_2160p_60fps.mp4");
        await AddSourceEntityAsync(db, EntityKindRegistry.Video.Code, "Friendship (2025)", nsfwVideoPath, isNsfw: true);
        await AddSourceEntityAsync(db, EntityKindRegistry.Video.Code, "Big Buck Bunny", safeVideoPath, isNsfw: false);

        var persistence = new EfFilesPersistence(db);

        var hidden = await persistence.ListHiddenPathsAsync(
            [rootPath, videosPath, nsfwFolderPath, nsfwVideoPath, safeVideoPath],
            CancellationToken.None);

        Assert.DoesNotContain(rootPath, hidden);
        Assert.DoesNotContain(videosPath, hidden);
        Assert.Contains(nsfwFolderPath, hidden);
        Assert.Contains(nsfwVideoPath, hidden);
        Assert.DoesNotContain(safeVideoPath, hidden);
    }

    private static async Task AddSourceEntityAsync(
        PrismediaDbContext db,
        string kindCode,
        string title,
        string sourcePath,
        bool isNsfw) {
        var entityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = kindCode,
            Title = title,
            IsNsfw = isNsfw,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Role = EntityFileRole.Source,
            Path = sourcePath,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"files-persistence-{Guid.NewGuid():N}")
            .Options);
}
