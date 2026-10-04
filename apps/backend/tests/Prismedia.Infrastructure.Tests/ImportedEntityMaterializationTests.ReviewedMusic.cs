using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

public sealed partial class ImportedEntityMaterializationTests {
    [Fact]
    public async Task ReviewedAlbumKeepsRequestedTitlesAndOrderWhenFilenamesDisagree() {
        await using var db = CreateContext();
        var rootPath = Directory.CreateDirectory(Path.Combine(_workRoot, "reviewed-library")).FullName;
        var payloadPath = Directory.CreateDirectory(Path.Combine(_workRoot, "reviewed-download")).FullName;
        await File.WriteAllTextAsync(Path.Combine(payloadPath, "a.flac"), "second-song");
        await File.WriteAllTextAsync(Path.Combine(payloadPath, "z.flac"), "first-song");
        var root = new RootPersistence(rootPath, scanAudio: true);
        AddLibraryRoot(db, root.Root);
        var albumId = AddWantedEntity(db, EntityKind.AudioLibrary, "Album");
        var firstId = AddWantedEntity(db, EntityKind.AudioTrack, "First song", albumId, sortOrder: 0);
        var secondId = AddWantedEntity(db, EntityKind.AudioTrack, "Second song", albumId, sortOrder: 1);
        var acquisitionId = await AddAcquisitionAsync(db, EntityKind.AudioLibrary, albumId, "Album");
        var store = AcquisitionTestFactory.Store(db);
        var engine = new MusicAcquisitionImportEngine(store, new EfBookAcquisitionProfileStore(db), root,
            new DownloadPayloadReader(), new ImportFileMover(new TestFileMutationGuard()), Torrents(store), new EfImportTargetIndex(db),
            new EfAcquisitionBlocklistStore(db), new EfAcquisitionHistoryStore(db), AlbumMaterializer(db, root),
            NullLogger<MusicAcquisitionImportEngine>.Instance);
        var import = new AcquisitionImportContext(acquisitionId, "Album", "Artist", null, null, null, null,
            null, payloadPath, null, null, Kind: EntityKind.AudioLibrary, EntityId: albumId,
            TargetLibraryRootId: root.Root.Id) {
            ManualFileMappings = [new("z.flac", firstId, 0, 0), new("a.flac", secondId, 0, 0)]
        };

        await engine.ImportAsync(JobContext(db, acquisitionId, new MergedImportTestSupport.RecordingJobQueue()), import, default);

        var tracks = await db.Entities.AsNoTracking().Where(entity => entity.ParentEntityId == albumId)
            .OrderBy(entity => entity.SortOrder).ToArrayAsync();
        Assert.Equal([firstId, secondId], tracks.Select(track => track.Id));
        Assert.Equal(["First song", "Second song"], tracks.Select(track => track.Title));
        Assert.All(tracks, track => Assert.False(track.IsWanted));
        foreach (var (id, content) in new[] { (firstId, "first-song"), (secondId, "second-song") }) {
            var source = await db.EntityFiles.SingleAsync(file => file.EntityId == id && file.Role == EntityFileRole.Source);
            Assert.Equal(content, await File.ReadAllTextAsync(source.Path));
        }
    }
}
