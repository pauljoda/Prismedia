using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Persistence;
using System.Text.Json;

namespace Prismedia.Infrastructure.Tests;

public sealed partial class ImportedEntityMaterializationTests {
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task GroupedImportMaterializesOneStableGalleryAndExactOrderedImageOwners(int count) {
        await using var db = CreateContext();
        var root = new RootPersistence(_workRoot, scanImages: true);
        AddLibraryRoot(db, root.Root); await db.SaveChangesAsync();
        var folder = Directory.CreateDirectory(Path.Combine(_workRoot, "Selected gallery")).FullName;
        var paths = Enumerable.Range(1, count).Select(index => Path.Combine(folder, $"{index:00000}-image.png")).ToArray();
        foreach (var path in paths) await File.WriteAllTextAsync(path, "image bytes");
        var persistence = new LibraryScanPersistenceService(db);
        var scan = new ScanGalleryJobHandler(NullLogger<ScanGalleryJobHandler>.Instance, Discovery(), root, persistence, persistence);
        var materializer = Materializer(db, new ImportedImageMaterializationPolicy(scan), persistence);
        var operation = Guid.NewGuid(); var queue = new MergedImportTestSupport.RecordingJobQueue();
        var request = new ImportedEntityMaterializationRequest(operation, null, root.Root, paths.Reverse().ToArray());
        var result = await materializer.MaterializeAsync(EntityKind.Gallery, JobContext(operation, queue), request, default);
        Assert.Equal(count, result.Entities.Count);
        Assert.All(result.Entities, image => Assert.Equal(EntityKind.Image, image.Kind));
        var gallery = Assert.Single(result.ProcessingRoots);
        Assert.Equal(EntityKind.Gallery, gallery.Kind);
        Assert.True((await db.GalleryDetails.FindAsync(gallery.Id))!.PreserveContainer);
        var images = await db.Entities.Where(entity => entity.ParentEntityId == gallery.Id).OrderBy(entity => entity.SortOrder).ToArrayAsync();
        Assert.Equal(paths.Select(Path.GetFileNameWithoutExtension), images.Select(entity => entity.Title));
        var replay = await materializer.MaterializeAsync(EntityKind.Gallery, JobContext(operation, queue), request, default);
        Assert.Equal(result.Entities.Select(e => e.Id).Order(), replay.Entities.Select(e => e.Id).Order());
        Assert.Equal(gallery, Assert.Single(replay.ProcessingRoots));
    }

    [Fact]
    public async Task ExplicitSingleImageGalleryKeepsItsContainerThroughRescans() {
        await using var db = CreateContext();
        var root = new RootPersistence(_workRoot, scanImages: true);
        AddLibraryRoot(db, root.Root); await db.SaveChangesAsync();
        var folder = Directory.CreateDirectory(Path.Combine(_workRoot, "Album")).FullName;
        var path = Path.Combine(folder, "001.png");
        await File.WriteAllTextAsync(path, "image bytes");
        var persistence = new LibraryScanPersistenceService(db);
        var id = Assert.Single(await persistence.UpsertGalleriesBatchAsync([
            new GalleryUpsertItem(folder, "Album", root.Root.Id, null, 0, false, PreserveContainer: true)], default));
        var scan = new ScanGalleryJobHandler(NullLogger<ScanGalleryJobHandler>.Instance, Discovery(), root, persistence, persistence);
        var queue = new MergedImportTestSupport.RecordingJobQueue();
        for (var attempt = 0; attempt < 2; attempt++) {
            var job = new JobRunSnapshot(Guid.NewGuid(), JobType.ScanGallery, JobRunStatus.Running, 0, null,
                JsonSerializer.Serialize(new { libraryRootId = root.Root.Id }), null, null, null, DateTimeOffset.UtcNow, null, null);
            await scan.HandleAsync(new(job, queue), default);
            Assert.True(await db.Entities.AnyAsync(entity => entity.Id == id));
            var image = await db.Entities.SingleAsync(entity => entity.KindCode == EntityKind.Image.ToCode());
            Assert.Equal(id, image.ParentEntityId);
            Assert.True((await db.GalleryDetails.FindAsync(id))!.PreserveContainer);
        }
    }
}
