using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Application.Organization;
using Prismedia.Contracts.Organize;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Organization;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Files;

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

        var service = CreateService(db);
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

        var service = CreateService(db);
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

            var service = CreateService(db);
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

    [Fact]
    public async Task PlanTreatsCaseVariantTargetAsDistinctOnUnix() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        await using var db = CreateContext();
        var tempRoot = Directory.CreateTempSubdirectory("prismedia-organize-case-");
        try {
            var videoId = Guid.Parse("40000000-0000-0000-0000-000000000001");
            var now = DateTimeOffset.UtcNow;
            var sourcePath = Path.Combine(tempRoot.FullName, "good name.mkv");
            var targetPath = Path.Combine(tempRoot.FullName, "Good Name.mkv");
            await File.WriteAllTextAsync(sourcePath, "video");

            SeedRoot(db, tempRoot.FullName);
            SeedEntity(db, videoId, EntityKindRegistry.Video.Code, "Good Name", null, null, now);
            SeedSource(db, videoId, sourcePath, now);
            await db.SaveChangesAsync();

            var result = await CreateService(db).PlanAsync(
                new OrganizePlanRequest(videoId, null),
                CancellationToken.None);

            var item = Assert.Single(result.Items);
            Assert.Equal("ready", item.Status);
            Assert.Equal(targetPath, item.TargetPath);
            Assert.True(File.Exists(sourcePath));
        } finally {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PathRewriteKeepsCaseDistinctSiblingUntouchedOnUnix() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        await using var db = CreateContext();
        var rootPath = Path.Combine(Path.GetTempPath(), $"prismedia-organize-rewrite-{Guid.NewGuid():N}");
        var upperEntityId = Guid.Parse("50000000-0000-0000-0000-000000000001");
        var lowerEntityId = Guid.Parse("50000000-0000-0000-0000-000000000002");
        var now = DateTimeOffset.UtcNow;
        var upperSource = Path.Combine(rootPath, "Album", "track.mp3");
        var lowerSource = Path.Combine(rootPath, "album", "track.mp3");
        var target = Path.Combine(rootPath, "Renamed");
        SeedEntity(db, upperEntityId, EntityKindRegistry.AudioTrack.Code, "Upper", null, null, now);
        SeedEntity(db, lowerEntityId, EntityKindRegistry.AudioTrack.Code, "Lower", null, null, now);
        SeedSource(db, upperEntityId, upperSource, now);
        SeedSource(db, lowerEntityId, lowerSource, now);
        await db.SaveChangesAsync();

        await new EfOrganizePersistence(db).ApplyPathPrefixRewriteAsync(
            Path.Combine(rootPath, "Album"),
            target,
            CancellationToken.None);

        var paths = await db.EntityFiles.AsNoTracking()
            .ToDictionaryAsync(row => row.EntityId, row => row.Path);
        Assert.Equal(Path.Combine(target, "track.mp3"), paths[upperEntityId]);
        Assert.Equal(lowerSource, paths[lowerEntityId]);
    }

    [Fact]
    public async Task ApplyConflictDoesNotMoveSourceOwnedByDeletingEntity() {
        await using var db = CreateContext();
        var tempRoot = Directory.CreateTempSubdirectory("prismedia-organize-conflict-");
        try {
            var videoId = Guid.Parse("60000000-0000-0000-0000-000000000001");
            var now = DateTimeOffset.UtcNow;
            var sourcePath = Path.Combine(tempRoot.FullName, "bad-name.mkv");
            await File.WriteAllTextAsync(sourcePath, "video");
            SeedRoot(db, tempRoot.FullName);
            SeedEntity(db, videoId, EntityKindRegistry.Video.Code, "Good Name", null, null, now);
            SeedSource(db, videoId, sourcePath, now);
            await db.SaveChangesAsync();
            var ownerReader = new EfEntitySourcePathOwnerReader(db);
            var coordinator = new EntitySourcePathMutationCoordinator(
                ownerReader,
                new RejectingLifecycleMutationLease());
            var service = new OrganizeService(new EfOrganizePersistence(db), coordinator);

            var result = await service.ApplyAsync(
                new OrganizePlanRequest(videoId, null),
                CancellationToken.None);

            var item = Assert.Single(result.Items);
            Assert.Equal("failed", item.Status);
            Assert.True(File.Exists(sourcePath));
            Assert.False(File.Exists(Path.Combine(tempRoot.FullName, "Good Name.mkv")));
            Assert.Equal(0, result.Applied);
        } finally {
            tempRoot.Delete(recursive: true);
        }
    }

    private static OrganizeService CreateService(PrismediaDbContext db) {
        var hierarchy = new EfEntityHierarchyReader(db);
        var lifecycle = new EfEntityLifecycleMutationLease(db, hierarchy);
        return new OrganizeService(
            new EfOrganizePersistence(db),
            new EntitySourcePathMutationCoordinator(
                new EfEntitySourcePathOwnerReader(db),
                lifecycle));
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

    private sealed class RejectingLifecycleMutationLease : IEntityLifecycleMutationLease {
        public Task<bool> ExecuteAsync(
            Guid entityId,
            Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
