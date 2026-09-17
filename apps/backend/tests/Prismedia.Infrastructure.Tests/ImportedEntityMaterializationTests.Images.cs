using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Media.Persistence;
using System.Text.Json;

namespace Prismedia.Infrastructure.Tests;

public sealed partial class ImportedEntityMaterializationTests {
    [Fact]
    public async Task ImageImportOwnsOnlyItsExactFileAndRescanPreservesTitleAndIdentity() {
        using var fixture = await ImportedTitleFixture.CreateAsync(EntityKind.Image);
        await using var db = CreateContext();
        var root = new RootPersistence(fixture.Directory, scanImages: true);
        fixture.Work = fixture.Work with { Plan = fixture.Work.Plan with { LibraryRootId = root.Root.Id } };
        AddLibraryRoot(db, root.Root); await db.SaveChangesAsync();
        var persistence = new LibraryScanPersistenceService(db);
        var unrelated = Path.Combine(fixture.Directory, "unrelated.png");
        await File.WriteAllTextAsync(unrelated, "existing file");
        var existing = Assert.Single(await persistence.UpsertImagesBatchAsync([
            new ImageUpsertItem(unrelated, "Existing image", root.Root.Id, null, 13, 0, false)], default));
        var scan = new ScanGalleryJobHandler(NullLogger<ScanGalleryJobHandler>.Instance, Discovery(), root,
            persistence, persistence, importedTitles: new ImportedPublicationTitleResolver(fixture));
        var materializer = Materializer(db, new ImportedImageMaterializationPolicy(scan), persistence);
        var queue = new MergedImportTestSupport.RecordingJobQueue();
        var operationId = fixture.Work.Transfer.State.OperationId;
        var result = await materializer.MaterializeAsync(EntityKind.Image, JobContext(operationId, queue),
            new(operationId, null, root.Root, [fixture.Path]), default);
        var owner = Assert.Single(result.Entities);
        Assert.Equal(EntityKind.Image, owner.Kind);
        Assert.NotEqual(existing, owner.Id);
        Assert.Equal(2, await db.Entities.CountAsync());
        Assert.Equal(fixture.Work.Plan.Title, (await db.Entities.SingleAsync(e => e.Id == owner.Id)).Title);
        var fileIds = await db.EntityFiles.Select(f => f.Id).ToArrayAsync();
        var replay = await materializer.MaterializeAsync(EntityKind.Image, JobContext(operationId, queue),
            new(operationId, null, root.Root, [fixture.Path]), default);
        Assert.Equal(owner, Assert.Single(replay.Entities));
        var job = new JobRunSnapshot(Guid.NewGuid(), JobType.ScanGallery, JobRunStatus.Running, 0, null,
            JsonSerializer.Serialize(new { libraryRootId = root.Root.Id }), null, null, null, DateTimeOffset.UtcNow, null, null);
        await scan.HandleAsync(new(job, queue), default);
        Assert.Equal(fileIds.Order(), (await db.EntityFiles.Select(f => f.Id).ToArrayAsync()).Order());
        Assert.Equal(fixture.Work.Plan.Title, (await db.Entities.AsNoTracking().SingleAsync(e => e.Id == owner.Id)).Title);
        Assert.True(await db.Entities.AnyAsync(e => e.Id == existing));
    }
}
