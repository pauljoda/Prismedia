using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Files;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfFilesPersistenceTests {
    private static readonly Guid RootId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task ListHiddenPathsAsyncHidesFoldersWhoseAssociatedSourcesAreAllNsfw() {
        await using var db = CreateContext();
        var rootPath = Path.GetFullPath("/media/safe");
        var folderPath = Path.Combine(rootPath, "Videos", "Friendship (2025)");
        var videoPath = Path.Combine(folderPath, "Friendship (2025) Bluray-1080p.mkv");
        await AddSourceEntityAsync(db, EntityKindRegistry.Video.Code, "Friendship (2025)", videoPath, isNsfw: true);

        var persistence = new EfFilesPersistence(db);

        var hidden = await persistence.ListHiddenPathsAsync(
            rootPath,
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
            rootPath,
            [rootPath, videosPath, nsfwFolderPath, nsfwVideoPath, safeVideoPath],
            CancellationToken.None);

        Assert.DoesNotContain(rootPath, hidden);
        Assert.DoesNotContain(videosPath, hidden);
        Assert.Contains(nsfwFolderPath, hidden);
        Assert.Contains(nsfwVideoPath, hidden);
        Assert.DoesNotContain(safeVideoPath, hidden);
    }

    [Fact]
    public async Task ExclusionsAreScopedToLibraryRootAndCoverDescendants() {
        await using var db = CreateContext();
        var otherRootId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        SeedLibraryRoot(db, RootId, "/media/root");
        SeedLibraryRoot(db, otherRootId, "/media/other");
        var persistence = new EfFilesPersistence(db);

        await persistence.UpsertExclusionAsync(RootId, "Skip", FileEntryKind.Directory, CancellationToken.None);
        await persistence.UpsertExclusionAsync(otherRootId, "Skip", FileEntryKind.Directory, CancellationToken.None);

        var excluded = await persistence.ListExcludedRelativePathsAsync(
            RootId,
            ["Skip", "Skip/nested.mkv", "Keep.mkv"],
            CancellationToken.None);

        Assert.Equal(["Skip", "Skip/nested.mkv"], excluded.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(2, db.MediaFileIgnores.Count());
    }

    [Fact]
    public async Task RemoveExclusionDeletesOnlyTheSelectedRootPath() {
        await using var db = CreateContext();
        var otherRootId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        SeedLibraryRoot(db, RootId, "/media/root");
        SeedLibraryRoot(db, otherRootId, "/media/other");
        db.MediaFileIgnores.Add(new MediaFileIgnoreRow {
            LibraryRootId = RootId,
            Path = "Skip",
            Kind = "directory",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.MediaFileIgnores.Add(new MediaFileIgnoreRow {
            LibraryRootId = otherRootId,
            Path = "Skip",
            Kind = "directory",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var persistence = new EfFilesPersistence(db);

        await persistence.RemoveExclusionAsync(RootId, "Skip", CancellationToken.None);

        var remaining = Assert.Single(db.MediaFileIgnores);
        Assert.Equal(otherRootId, remaining.LibraryRootId);
    }

    [Fact]
    public async Task UnixExclusionsKeepCaseDistinctFilesystemPathsIndependent() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        await using var db = CreateContext();
        SeedLibraryRoot(db, RootId, "/media/root");
        var persistence = new EfFilesPersistence(db);

        await persistence.UpsertExclusionAsync(
            RootId,
            "Skip",
            FileEntryKind.Directory,
            CancellationToken.None);
        await persistence.UpsertExclusionAsync(
            RootId,
            "skip",
            FileEntryKind.Directory,
            CancellationToken.None);

        var excluded = await persistence.ListExcludedRelativePathsAsync(
            RootId,
            ["Skip/one.mkv", "skip/two.mkv", "SKIP/three.mkv"],
            CancellationToken.None);

        Assert.Equal(2, db.MediaFileIgnores.Count());
        Assert.Contains("Skip/one.mkv", excluded);
        Assert.Contains("skip/two.mkv", excluded);
        Assert.DoesNotContain("SKIP/three.mkv", excluded);
    }

    [Fact]
    public async Task WindowsExclusionUpsertReusesCaseVariantPath() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        await using var db = CreateContext();
        SeedLibraryRoot(db, RootId, "C:\\Media\\Root");
        var persistence = new EfFilesPersistence(db);

        await persistence.UpsertExclusionAsync(
            RootId,
            "Skip",
            FileEntryKind.Directory,
            CancellationToken.None);
        await persistence.UpsertExclusionAsync(
            RootId,
            "skip",
            FileEntryKind.Directory,
            CancellationToken.None);

        Assert.Single(db.MediaFileIgnores);
    }

    [Fact]
    public async Task PathRewriteUpdatesEntityFileAndCapabilitySourceTogether() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var now = DateTimeOffset.UtcNow;
        var sourceFolder = Path.Combine(Path.GetTempPath(), "prismedia-files-rewrite", "Incoming");
        var targetFolder = Path.Combine(Path.GetTempPath(), "prismedia-files-rewrite", "Organized");
        var sourceFile = Path.Combine(sourceFolder, "Movie.mkv");
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = EntityKindRegistry.Video.Code,
            Title = "Movie",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Role = EntityFileRole.Source,
            Path = sourceFile,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.EntitySources.Add(new EntitySourceRow {
            EntityId = entityId,
            Code = EntityStorageShape.Folder.ToCode(),
            Value = sourceFolder,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        await new EfFilesPersistence(db).ApplyPathPrefixRewriteAsync(
            sourceFolder,
            targetFolder,
            CancellationToken.None);

        Assert.Equal(Path.Combine(targetFolder, "Movie.mkv"), Assert.Single(db.EntityFiles).Path);
        Assert.Equal(targetFolder, Assert.Single(db.EntitySources).Value);
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

    private static void SeedLibraryRoot(PrismediaDbContext db, Guid id, string path) {
        db.LibraryRoots.Add(new LibraryRootRow {
            Id = id,
            Path = path,
            Label = Path.GetFileName(path),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"files-persistence-{Guid.NewGuid():N}")
            .Options);
}
