using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed partial class ManagedTrackingPostgresTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OneRemoteBookCombinesReviewedRenditionsWithoutLosingTheEbookWork(bool audioFirst) {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var connectionId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var ebookId = Guid.NewGuid();
        var audioBookId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var ebookFileId = Guid.NewGuid();
        var audioFileId = Guid.NewGuid();
        var audioFolder = Directory.CreateDirectory(Path.Combine(workspace, "audio"));
        var ebookPath = Path.Combine(workspace, "example.epub");
        var audioPath = Path.Combine(audioFolder.FullName, "example.m4b");
        await File.WriteAllBytesAsync(ebookPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(audioPath, [4, 5, 6]);
        db.IntegrationConnections.Add(new() { Id = connectionId, PluginId = "fixture", Name = "Fixture",
            BaseUrl = "http://manager.test/", Enabled = true, Status = ConnectionStatus.Ready, Revision = 1 });
        db.LibraryRoots.Add(new() { Id = rootId, Path = workspace, Label = "Books", Enabled = true, ScanBooks = true });
        db.ExternalLibraryMounts.Add(new() { Id = Guid.NewGuid(), ConnectionId = connectionId, LibraryRootId = rootId,
            RemoteRootId = "books", RemotePath = "/books", LocalPath = workspace });
        db.Entities.AddRange(
            new() { Id = ebookId, KindCode = EntityKind.Book.ToCode(), Title = "Example" },
            new() { Id = audioBookId, KindCode = EntityKind.Book.ToCode(), Title = "Example" },
            new() { Id = trackId, ParentEntityId = audioBookId, KindCode = EntityKind.AudioTrack.ToCode(), Title = "Example" });
        db.BookDetails.AddRange(
            new() { EntityId = ebookId, Format = BookFormat.Epub },
            new() { EntityId = audioBookId, Format = BookFormat.Audio });
        db.EntitySources.Add(new() { EntityId = audioBookId, Code = EntitySourceCode.Folder.ToCode(),
            Value = audioFolder.FullName, UpdatedAt = DateTimeOffset.UtcNow });
        db.EntityFiles.AddRange(
            new() { Id = ebookFileId, EntityId = ebookId, Path = ebookPath, SizeBytes = 3 },
            new() { Id = audioFileId, EntityId = trackId, Path = audioPath, SizeBytes = 3 });
        await db.SaveChangesAsync();

        var ids = new Dictionary<string, string> { ["fixture-book"] = "work-1" };
        var ebook = new ManagedItemInput(EntityKind.Book, "work-1", ids, BookRendition.Ebook);
        var audio = ebook with { BookRendition = BookRendition.Audiobook };
        var store = Store(db);
        var item = new ManagedLibraryItem("work-1", EntityKind.Book, "Example", null, ids, false, null, 1);
        var ebookSnapshot = new ManagedItemSnapshot(
            item, "/books",
            [new("ebook-file", "/books/example.epub", 3, null,
                [new("work-1", EntityKind.Book, "Example")])], DateTimeOffset.UtcNow);
        var audioSnapshot = new ManagedItemSnapshot(
            item, "/books/audio",
            [new("audio-file", "/books/audio/example.m4b", 3, null,
                [new("track-1", EntityKind.AudioTrack, "Example")])], DateTimeOffset.UtcNow);
        var ebookSelection = new ManagedBindingSelection("work-1", ebookId, ebookFileId);
        var audioSelection = new ManagedBindingSelection("track-1", trackId, audioFileId);
        var firstItem = audioFirst ? audio : ebook;
        var firstSnapshot = audioFirst ? audioSnapshot : ebookSnapshot;
        var firstSelection = audioFirst ? audioSelection : ebookSelection;
        var acceptedFirst = await store.CreateAsync(connectionId,
            new(Guid.NewGuid(), rootId, firstItem, [firstSelection]), "Example", default);
        var firstWork = (await store.FindAsync(acceptedFirst.Id, default))!;
        var firstObservation = await store.ObserveAsync(connectionId, firstSnapshot, default);
        var firstPlan = ManagedSourceAdoption.Plan(firstObservation.Files, firstWork.Selections, firstObservation.Sources);
        Assert.Null(firstPlan.ReviewReason);
        await store.ApplyAsync(firstWork, firstObservation, firstPlan.Bindings, [], default);

        var secondItem = audioFirst ? ebook : audio;
        var secondSelection = audioFirst ? ebookSelection : audioSelection;
        var error = await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(connectionId,
            new(Guid.NewGuid(), rootId, secondItem, [secondSelection]), "Example", default));
        Assert.Contains("separate Book works", error.Message);
        Assert.Single(await db.ManagedHoldings.AsNoTracking().ToArrayAsync());

        var unexpectedFile = new EntityFileRow { Id = Guid.NewGuid(), EntityId = audioFirst ? ebookId : audioBookId,
            Path = Path.Combine(workspace, "unreviewed.bin"), Role = EntityFileRole.Cover, SizeBytes = 1 };
        db.EntityFiles.Add(unexpectedFile);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(connectionId,
            new(Guid.NewGuid(), rootId, secondItem, [secondSelection], CombineBookWorks: true), "Example", default));
        db.EntityFiles.Remove(unexpectedFile);
        await db.SaveChangesAsync();

        var reviewedRequest = new TrackManagedHoldingRequest(Guid.NewGuid(), rootId, secondItem,
            [secondSelection], CombineBookWorks: true);
        var acceptedSecond = await store.CreateAsync(connectionId, reviewedRequest, "Example", default);
        Assert.Equal(acceptedSecond.Id, (await store.CreateAsync(connectionId, reviewedRequest, "Example", default)).Id);
        var canonicalId = audioFirst ? audioBookId : ebookId;
        var archivedId = audioFirst ? ebookId : audioBookId;
        Assert.Equal(canonicalId, acceptedSecond.BookWorkId);
        Assert.Equal(canonicalId, (await store.FindAsync(acceptedFirst.Id, default))!.Tracking.BookWorkId);
        Assert.Equal(canonicalId, (await db.Entities.AsNoTracking().SingleAsync(row => row.Id == trackId)).ParentEntityId);
        Assert.Equal(canonicalId, (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.Id == ebookFileId)).EntityId);
        Assert.True((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == archivedId)).IsLibraryArchived);
        Assert.Equal(BookFormat.Epub, (await db.BookDetails.AsNoTracking().SingleAsync(row => row.EntityId == canonicalId)).Format);
        Assert.Equal(canonicalId, (await db.EntitySources.AsNoTracking().SingleAsync(row => row.Code == EntitySourceCode.Folder.ToCode())).EntityId);
        Assert.Equal(2, await db.FulfillmentReservations.AsNoTracking().CountAsync(row => row.EntityId == canonicalId));

        var scanner = new LibraryScanPersistenceService(db);
        Assert.Equal(canonicalId, await scanner.UpsertSingleFileBookAsync(ebookPath, "Readable title", rootId,
            false, BookType.Novel, BookFormat.Epub, "application/epub+zip", null, null, default));
        Assert.Equal(canonicalId, await scanner.UpsertAudiobookBookAsync(audioFolder.FullName, "Audio folder title", rootId,
            false, BookType.Novel, BookFormat.Audio, default));
        Assert.Equal("Readable title", (await db.Entities.AsNoTracking().SingleAsync(row => row.Id == canonicalId)).Title);
        Assert.Equal(BookFormat.Epub, (await db.BookDetails.AsNoTracking().SingleAsync(row => row.EntityId == canonicalId)).Format);
        Assert.Equal(canonicalId, (await db.Entities.AsNoTracking().SingleAsync(row => row.Id == trackId)).ParentEntityId);
    }

    [Fact]
    public async Task OneBookCanHaveIndependentConnectedEbookAndAudiobookOwners() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var connectionId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var ebookFileId = Guid.NewGuid();
        var audioFileId = Guid.NewGuid();
        var ebookRootId = Guid.NewGuid();
        var audioRootId = Guid.NewGuid();
        var ebookFolder = Directory.CreateDirectory(Path.Combine(workspace, "ebook"));
        var audioFolder = Directory.CreateDirectory(Path.Combine(workspace, "audio"));
        var ebookPath = Path.Combine(ebookFolder.FullName, "example.epub");
        var audioPath = Path.Combine(audioFolder.FullName, "example.m4b");
        await File.WriteAllBytesAsync(ebookPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(audioPath, [4, 5, 6]);
        db.IntegrationConnections.Add(new() { Id = connectionId, PluginId = "fixture", Name = "Fixture",
            BaseUrl = "http://manager.test/", Enabled = true, Status = ConnectionStatus.Ready, Revision = 1 });
        db.LibraryRoots.AddRange(
            new() { Id = ebookRootId, Path = ebookFolder.FullName, Label = "Ebooks", Enabled = true, ScanBooks = true },
            new() { Id = audioRootId, Path = audioFolder.FullName, Label = "Audiobooks", Enabled = true, ScanBooks = true });
        db.ExternalLibraryMounts.AddRange(
            new() { Id = Guid.NewGuid(), ConnectionId = connectionId, LibraryRootId = ebookRootId,
                RemoteRootId = "ebook", RemotePath = "/books", LocalPath = ebookFolder.FullName },
            new() { Id = Guid.NewGuid(), ConnectionId = connectionId, LibraryRootId = audioRootId,
                RemoteRootId = "audio", RemotePath = "/audio", LocalPath = audioFolder.FullName });
        db.Entities.AddRange(
            new() { Id = bookId, KindCode = EntityKind.Book.ToCode(), Title = "Example" },
            new() { Id = trackId, ParentEntityId = bookId, KindCode = EntityKind.AudioTrack.ToCode(), Title = "Chapter 1" });
        db.EntityFiles.AddRange(
            new() { Id = ebookFileId, EntityId = bookId, Path = ebookPath, SizeBytes = 3 },
            new() { Id = audioFileId, EntityId = trackId, Path = audioPath, SizeBytes = 3 });
        await db.SaveChangesAsync();

        var identities = new Dictionary<string, string> { ["fixture-book"] = "work-1" };
        var ebookItem = new ManagedItemInput(EntityKind.Book, "work-1", identities, BookRendition.Ebook);
        var audioItem = ebookItem with { BookRendition = BookRendition.Audiobook };
        var item = new ManagedLibraryItem("work-1", EntityKind.Book, "Example", null, identities, false, null, 1);
        var ebook = new ManagedItemSnapshot(item, "/books/Example", [new("ebook-file", "/books/example.epub", 3, null,
            [new("work-1", EntityKind.Book, "Example")])], DateTimeOffset.UtcNow);
        var audio = new ManagedItemSnapshot(item, "/audio/Example", [new("audio-file", "/audio/example.m4b", 3, null,
            [new("track-1", EntityKind.AudioTrack, "Chapter 1")])], DateTimeOffset.UtcNow);
        var store = Store(db);

        async Task<Guid> Adopt(Guid rootId, ManagedItemInput input, ManagedItemSnapshot snapshot, string targetId, Guid entityId, Guid fileId) {
            var request = new TrackManagedHoldingRequest(Guid.NewGuid(), rootId, input, [new(targetId, entityId, fileId)]);
            var accepted = await store.CreateAsync(connectionId, request, "Example", default);
            var work = (await store.FindAsync(accepted.Id, default))!;
            var observation = await store.ObserveAsync(connectionId, snapshot, default);
            var plan = ManagedSourceAdoption.Plan(observation.Files, work.Selections, observation.Sources);
            Assert.Null(plan.ReviewReason);
            await store.ApplyAsync(work, observation, plan.Bindings, [], default);
            return accepted.Id;
        }

        var ebookHoldingId = await Adopt(ebookRootId, ebookItem, ebook, "work-1", bookId, ebookFileId);
        var audioHoldingId = await Adopt(audioRootId, audioItem, audio, "track-1", trackId, audioFileId);

        var holdings = await db.ManagedHoldings.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, holdings.Length);
        Assert.Equal([BookRendition.Ebook, BookRendition.Audiobook], holdings.Select(row => row.BookRendition).Order().ToArray());
        var reservations = await db.FulfillmentReservations.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, reservations.Length);
        Assert.All(reservations, reservation => Assert.Equal(bookId, reservation.EntityId));
        Assert.Equal([BookRendition.Ebook, BookRendition.Audiobook], reservations.Select(row => row.BookRendition).Order().ToArray());
        Assert.Equal(new[] { bookId, trackId }.Order(), (await db.ManagedSourceBindings.AsNoTracking().ToArrayAsync())
            .Select(row => row.EntityId).Order().ToArray());

        var movedAudioPath = Path.Combine(audioFolder.FullName, "renamed.m4b");
        File.Move(audioPath, movedAudioPath);
        var movedAudio = audio with { Files = [audio.Files[0] with { Path = "/audio/renamed.m4b" }] };
        var audioHolding = (await store.FindAsync(audioHoldingId, default))!;
        Assert.Equal(bookId, audioHolding.Tracking.BookWorkId);
        Assert.Equal(bookId, (await store.FindAsync(ebookHoldingId, default))!.Tracking.BookWorkId);
        var movedObservation = await store.ObserveAsync(connectionId, movedAudio, default);
        var movedPlan = ManagedSourceReconciliation.Plan(audioHolding.Tracking.Bindings, movedObservation.Files);
        Assert.Null(movedPlan.ReviewReason);
        await store.ApplyAsync(audioHolding, movedObservation, null, movedPlan.Changes, default);
        Assert.Equal(movedAudioPath, (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.Id == audioFileId)).Path);
        Assert.Equal(ebookPath, (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.Id == ebookFileId)).Path);

        audioHolding = (await store.FindAsync(audioHoldingId, default))!;
        File.Move(movedAudioPath, movedAudioPath + ".offline");
        await store.ConfirmRemovalAsync(audioHolding, "Removed upstream", default);
        Assert.True((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == trackId)).IsLibraryArchived);
        Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == bookId)).IsLibraryArchived);
        Assert.Equal(EntityFileRole.UnavailableSource,
            (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.Id == audioFileId)).Role);

        audioHolding = (await store.FindAsync(audioHoldingId, default))!;
        var release = Releases(db);
        var intent = new ReleaseManagedHoldingRequest(Guid.NewGuid(), audioHolding.Tracking.Revision,
            ManagedControlIdentity.From(audioHolding.Tracking).Fingerprint, null, RemoteItemAbsent: true);
        await release.BeginAsync(connectionId, audioHoldingId, intent, default);
        var releaseWork = (await release.FindAsync(audioHoldingId, default))!;
        await release.CompleteAsync(releaseWork, new(null, true, true, RemoteItemAbsent: true), default);

        Assert.Equal(ManagedTrackingStatus.Released, (await store.FindAsync(audioHoldingId, default))!.Tracking.Status);
        Assert.Equal(ManagedTrackingStatus.Tracking, (await store.FindAsync(ebookHoldingId, default))!.Tracking.Status);
        Assert.Equal(BookRendition.Ebook, Assert.Single(await db.FulfillmentReservations.AsNoTracking()
            .Where(row => row.ReleasedAt == null).ToArrayAsync()).BookRendition);
        Assert.Equal(bookId, Assert.Single(await db.ManagedSourceBindings.AsNoTracking().ToArrayAsync()).EntityId);
    }
}
