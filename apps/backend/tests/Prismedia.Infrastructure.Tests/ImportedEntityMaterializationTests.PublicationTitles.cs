using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Media.Persistence;

namespace Prismedia.Infrastructure.Tests;

public sealed partial class ImportedEntityMaterializationTests {
    [Theory]
    [InlineData(null)]
    [InlineData("Embedded edition title")]
    public async Task AcceptedPublicationTitleBeatsASeriesFallbackButPreservesAnEmbeddedTitle(string? embeddedTitle) {
        using var fixture = await ImportedTitleFixture.CreateAsync();
        await using var db = CreateContext();
        var root = new RootPersistence(fixture.Directory, scanBooks: true);
        fixture.Work = fixture.Work with { Plan = fixture.Work.Plan with { LibraryRootId = root.Root.Id } };
        AddLibraryRoot(db, root.Root); await db.SaveChangesAsync();
        var persistence = new LibraryScanPersistenceService(db);
        var scan = new ScanBookJobHandler(NullLogger<ScanBookJobHandler>.Instance, Discovery(), root, persistence, persistence,
            bookFileMetadata: new TitleMetadataReader(embeddedTitle), importedTitles: new ImportedPublicationTitleResolver(fixture));
        await scan.MaterializeImportedPathsAsync(JobContext(Guid.NewGuid(), new MergedImportTestSupport.RecordingJobQueue()),
            fixture.Work.Transfer.State.OperationId, root.Root, [fixture.Path], default);
        var entity = await db.Entities.AsNoTracking().SingleAsync(row => row.KindCode == EntityKind.Book.ToCode());
        Assert.Equal(embeddedTitle ?? fixture.Work.Plan.Title, entity.Title);
        Assert.Equal(embeddedTitle is null ? 1 : 0, fixture.Reads);
    }
    [Theory]
    [InlineData(EntityKind.Book)]
    [InlineData(EntityKind.ComicInstallment)]
    public async Task PublicationMaterializationAndFullRescanKeepAcceptedTitleAndEntityIdentity(EntityKind kind) {
        byte[]? bytes = null;
        if (kind == EntityKind.ComicInstallment) {
            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true)) {
                using var page = archive.CreateEntry("001.jpg").Open();
                page.Write([0xff, 0xd8, 0xff, 0xd9]);
            }
            bytes = buffer.ToArray();
        }
        using var fixture = await ImportedTitleFixture.CreateAsync(kind, bytes);
        await using var db = CreateContext();
        var root = new RootPersistence(fixture.Directory, scanBooks: true);
        fixture.Work = fixture.Work with { Plan = fixture.Work.Plan with { LibraryRootId = root.Root.Id } };
        AddLibraryRoot(db, root.Root); await db.SaveChangesAsync();
        var persistence = new LibraryScanPersistenceService(db);
        var titles = new ImportedPublicationTitleResolver(fixture);
        var book = new ScanBookJobHandler(NullLogger<ScanBookJobHandler>.Instance, Discovery(), root, persistence, persistence, importedTitles: titles);
        var comic = new ScanComicJobHandler(NullLogger<ScanComicJobHandler>.Instance, Discovery(), root, persistence, new RecordingPageManifestStore(), persistence, importedTitles: titles);
        IImportedEntityMaterializationPolicy policy = kind == EntityKind.Book ? new ImportedBookMaterializationPolicy(book) : new ImportedComicMaterializationPolicy(comic);
        var materializer = Materializer(db, policy, persistence);
        var queue = new MergedImportTestSupport.RecordingJobQueue();
        var operationId = fixture.Work.Transfer.State.OperationId;
        var result = await materializer.MaterializeAsync(kind, JobContext(operationId, queue), new(operationId, null, root.Root, [fixture.Path]), default);
        var owner = Assert.Single(result.Entities);
        var before = await db.Entities.AsNoTracking().SingleAsync(entity => entity.Id == owner.Id);
        Assert.Equal(fixture.Work.Plan.Title, before.Title);
        var fileIds = await db.EntityFiles.Select(file => file.Id).ToArrayAsync();
        var job = new JobRunSnapshot(Guid.NewGuid(), policy.ScanJobType, JobRunStatus.Running, 0, null,
            JsonSerializer.Serialize(new { libraryRootId = root.Root.Id }), null, null, null, DateTimeOffset.UtcNow, null, null);
        if (kind == EntityKind.Book) await book.HandleAsync(new(job, queue), default);
        else await comic.HandleAsync(new(job, queue), default);
        var after = await db.Entities.AsNoTracking().SingleAsync(entity => entity.Id == owner.Id);
        Assert.Equal(fixture.Work.Plan.Title, after.Title);
        Assert.Equal(before.ParentEntityId, after.ParentEntityId);
        Assert.Equal(fileIds.Order(), (await db.EntityFiles.Select(file => file.Id).ToArrayAsync()).Order());
        Assert.DoesNotContain(await db.Entities.Select(entity => entity.Title).ToArrayAsync(), title => title?.Contains(operationId.ToString("N")) == true);
    }

    private sealed class TitleMetadataReader(string? title) : IBookFileMetadataReader {
        public Task<BookFileMetadata?> ReadAsync(string sourcePath, BookFormat format, CancellationToken cancellationToken) =>
            Task.FromResult<BookFileMetadata?>(new() { Title = title, Series = "A broader series" });
    }
}
