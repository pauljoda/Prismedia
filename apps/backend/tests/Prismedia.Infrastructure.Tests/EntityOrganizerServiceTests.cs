using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Organization;
using Prismedia.Contracts.Organize;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Organization;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EntityOrganizerServiceTests {
    [Fact]
    public async Task PlanUsesGenericStorageShapesAndStructuralParents() {
        await using var db = CreateContext();
        var rootPath = Path.Combine(Path.GetTempPath(), "prismedia-organize-plan");
        var seriesId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var seasonId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var videoId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var now = DateTimeOffset.UtcNow;

        SeedRoot(db, rootPath);
        SeedEntity(db, seriesId, EntityKindRegistry.VideoSeries.Code, "The Chair Company", null, null, now);
        SeedEntity(db, seasonId, EntityKindRegistry.VideoSeason.Code, "Season 1", seriesId, 1, now);
        SeedEntity(db, videoId, EntityKindRegistry.Video.Code, "Episode One", seasonId, 1, now);
        SeedSource(db, seriesId, Path.Combine(rootPath, "chair"), now);
        SeedSource(db, seasonId, Path.Combine(rootPath, "chair", "s1"), now);
        SeedSource(db, videoId, Path.Combine(rootPath, "chair", "s1", "bad-name.mkv"), now);
        await db.SaveChangesAsync();

        var service = new OrganizeService(new EfOrganizePersistence(db));
        var plan = await service.PlanAsync(new OrganizePlanRequest(null, null), CancellationToken.None);

        var series = Assert.Single(plan.Items, item => item.EntityId == seriesId);
        Assert.Equal("folder", series.StorageShape);
        Assert.Equal(Path.Combine(rootPath, "The Chair Company"), series.TargetPath);

        var season = Assert.Single(plan.Items, item => item.EntityId == seasonId);
        Assert.Equal(Path.Combine(rootPath, "The Chair Company", "Season 1"), season.TargetPath);

        var video = Assert.Single(plan.Items, item => item.EntityId == videoId);
        Assert.Equal("file", video.StorageShape);
        Assert.Equal(Path.Combine(rootPath, "The Chair Company", "Season 1", "Episode One.mkv"), video.TargetPath);
    }

    [Fact]
    public async Task PlanSkipsArchiveEntriesBecauseTheyAreNotMovedIndependently() {
        await using var db = CreateContext();
        var rootPath = Path.Combine(Path.GetTempPath(), "prismedia-organize-archive");
        var pageId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var now = DateTimeOffset.UtcNow;

        SeedRoot(db, rootPath);
        SeedEntity(db, pageId, EntityKindRegistry.BookPage.Code, "Page 1", null, null, now);
        SeedSource(db, pageId, Path.Combine(rootPath, "Book.cbz#page-1.jpg"), now);
        await db.SaveChangesAsync();

        var service = new OrganizeService(new EfOrganizePersistence(db));
        var plan = await service.PlanAsync(new OrganizePlanRequest(pageId, null), CancellationToken.None);

        var item = Assert.Single(plan.Items);
        Assert.Equal("archive-entry", item.StorageShape);
        Assert.Equal("skipped", item.Status);
        Assert.Equal("Archive entries are not moved independently.", item.Reason);
    }

    [Fact]
    public async Task ApplyMovesReadyFileAndUpdatesSourcePath() {
        await using var db = CreateContext();
        var tempRoot = Directory.CreateTempSubdirectory("prismedia-organize-apply-");
        try {
            var videoId = Guid.Parse("30000000-0000-0000-0000-000000000001");
            var now = DateTimeOffset.UtcNow;
            var sourcePath = Path.Combine(tempRoot.FullName, "bad-name.mkv");
            await File.WriteAllTextAsync(sourcePath, "video");

            SeedRoot(db, tempRoot.FullName);
            SeedEntity(db, videoId, EntityKindRegistry.Video.Code, "Good Name", null, null, now);
            SeedSource(db, videoId, sourcePath, now);
            await db.SaveChangesAsync();

            var service = new OrganizeService(new EfOrganizePersistence(db));
            var result = await service.ApplyAsync(new OrganizePlanRequest(videoId, null), CancellationToken.None);

            var item = Assert.Single(result.Items);
            Assert.Equal("applied", item.Status);
            Assert.Equal(1, result.Applied);
            Assert.False(File.Exists(sourcePath));
            Assert.True(File.Exists(Path.Combine(tempRoot.FullName, "Good Name.mkv")));
            Assert.Equal(
                Path.Combine(tempRoot.FullName, "Good Name.mkv"),
                Assert.Single(db.EntityFiles.Where(file => file.EntityId == videoId)).Path);
        } finally {
            tempRoot.Delete(recursive: true);
        }
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"entity-organizer-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }

    private static void SeedRoot(PrismediaDbContext db, string path) {
        var now = DateTimeOffset.UtcNow;
        db.LibraryRoots.Add(new LibraryRootRow {
            Id = Guid.NewGuid(),
            Path = path,
            Label = "Root",
            Enabled = true,
            Recursive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static void SeedEntity(
        PrismediaDbContext db,
        Guid id,
        string kindCode,
        string title,
        Guid? parentEntityId,
        int? sortOrder,
        DateTimeOffset now) {
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kindCode,
            Title = title,
            ParentEntityId = parentEntityId,
            SortOrder = sortOrder,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static void SeedSource(
        PrismediaDbContext db,
        Guid entityId,
        string path,
        DateTimeOffset now) {
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Role = EntityFileRole.Source,
            Path = path,
            CreatedAt = now,
            UpdatedAt = now
        });
    }
}
